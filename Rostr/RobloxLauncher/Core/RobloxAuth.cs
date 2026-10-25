using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RobloxLauncher.Core;

public record RobloxUser(long Id, string Name, string DisplayName);
public record ProbeResult(string? Ip, bool RateLimited);
public record FriendInfo(long Id, string Name, string DisplayName);
public record FriendPresence(long UserId, int Type, string Location, long PlaceId, string? GameId);
public record PrivateServerRef(long PlaceId, string LinkCode);

/// <summary>
/// Talks to Roblox on behalf of a saved account. The cookie is only ever sent to *.roblox.com.
/// </summary>
public static class RobloxAuth
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // No cookie container: we set the Cookie header by hand for each request.
        var handler = new HttpClientHandler { UseCookies = false };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Roblox/WinInet");
        return c;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string cookie, string? body, string? csrf, string referer)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Cookie", ".ROBLOSECURITY=" + cookie);
        req.Headers.TryAddWithoutValidation("Referer", referer);
        req.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
        if (csrf != null) req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await Http.SendAsync(req);
    }

    // Roblox answers the first POST with 403 plus an X-CSRF-TOKEN header; resend with that token.
    private static async Task<HttpResponseMessage> PostAsync(
        string url, string cookie, string body, string referer)
    {
        var res = await SendAsync(HttpMethod.Post, url, cookie, body, null, referer);
        if (res.StatusCode == HttpStatusCode.Forbidden &&
            res.Headers.TryGetValues("x-csrf-token", out var values))
        {
            var token = values.First();
            res.Dispose();
            res = await SendAsync(HttpMethod.Post, url, cookie, body, token, referer);
        }
        return res;
    }

    /// <summary>Returns the account the cookie belongs to, or null when the cookie is not valid.</summary>
    public static async Task<RobloxUser?> GetUserAsync(string cookie)
    {
        using var res = await SendAsync(HttpMethod.Get,
            "https://users.roblox.com/v1/users/authenticated", cookie, null, null, "https://www.roblox.com/");
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        var id = r.GetProperty("id").GetInt64();
        var name = r.GetProperty("name").GetString() ?? "";
        var display = r.TryGetProperty("displayName", out var d) ? (d.GetString() ?? name) : name;
        return new RobloxUser(id, name, display);
    }

    /// <summary>Gets a one-time login ticket for the client. Each launch needs a fresh one.</summary>
    public static async Task<string> GetTicketAsync(string cookie, long placeId)
    {
        using var res = await PostAsync("https://auth.roblox.com/v1/authentication-ticket",
            cookie, "{}", placeId > 0 ? $"https://www.roblox.com/games/{placeId}/" : "https://www.roblox.com/home");

        if (res.Headers.TryGetValues("rbx-authentication-ticket", out var t))
        {
            var ticket = t.FirstOrDefault();
            if (!string.IsNullOrEmpty(ticket)) return ticket;
        }

        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("This account's login expired. Remove it and add it again.");
        throw new InvalidOperationException($"Roblox did not give a login ticket (HTTP {(int)res.StatusCode}).");
    }

    /// <summary>Builds the same launch data the website's Play button would produce.</summary>
    public static LaunchLink BuildLink(string ticket, long placeId)
    {
        var tracker = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();
        var placeUrl =
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame" +
            $"&browserTrackerId={tracker}&placeId={placeId}&isPlayTogetherGame=false";
        var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        return new LaunchLink(ticket, placeUrl, tracker, launchTime, "en_us", "en_us", placeId);
    }

    /// <summary>Launch data that follows a user into whatever server they are in (the website's Join button).</summary>
    public static LaunchLink BuildFollowLink(string ticket, long userId, long placeIdHint)
    {
        var tracker = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();
        var placeUrl =
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser" +
            $"&browserTrackerId={tracker}&userId={userId}";
        var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        return new LaunchLink(ticket, placeUrl, tracker, launchTime, "en_us", "en_us", placeIdHint);
    }

    /// <summary>Launch data for a private server, given the access code Roblox issued for the link code.</summary>
    public static LaunchLink BuildPrivateLink(string ticket, long placeId, string accessCode, string linkCode)
    {
        var tracker = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();
        var placeUrl =
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame" +
            $"&browserTrackerId={tracker}&placeId={placeId}" +
            $"&accessCode={Uri.EscapeDataString(accessCode)}&linkCode={Uri.EscapeDataString(linkCode)}";
        var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        return new LaunchLink(ticket, placeUrl, tracker, launchTime, "en_us", "en_us", placeId);
    }

    private static readonly Regex PrivateCodeRx = new(
        @"joinPrivateGame\(\s*\d+\s*,\s*['""]([0-9a-fA-F\-]{36})['""]\s*,\s*['""]([0-9A-Za-z]+)['""]",
        RegexOptions.Compiled);

    /// <summary>
    /// Turns what the user pasted into a place id plus link code. Handles
    /// roblox.com/games/ID/name?privateServerLinkCode=CODE directly, and roblox.com/share?code=...&amp;type=Server
    /// by asking Roblox to resolve it. Returns null when the text is neither.
    /// </summary>
    public static async Task<PrivateServerRef?> ResolvePrivateLinkAsync(string cookie, string text)
    {
        text = text.Trim();

        var direct = Regex.Match(text, @"roblox\.com/games/(\d+)[^?\s]*\?[^\s]*privateServerLinkCode=([0-9A-Za-z]+)",
            RegexOptions.IgnoreCase);
        if (direct.Success)
            return new PrivateServerRef(long.Parse(direct.Groups[1].Value), direct.Groups[2].Value);

        var share = Regex.Match(text, @"roblox\.com/share\?[^\s]*code=([0-9A-Za-z]+)[^\s]*type=Server",
            RegexOptions.IgnoreCase);
        if (!share.Success)
            share = Regex.Match(text, @"roblox\.com/share\?[^\s]*type=Server[^\s]*code=([0-9A-Za-z]+)",
                RegexOptions.IgnoreCase);
        if (!share.Success) return null;

        var body = JsonSerializer.Serialize(new { linkId = share.Groups[1].Value, linkType = "Server" });
        using var res = await PostAsync("https://apis.roblox.com/sharelinks/v1/resolve-link",
            cookie, body, "https://www.roblox.com/");
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Roblox could not resolve that share link (HTTP {(int)res.StatusCode}).");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("privateServerInviteData", out var d) &&
            d.ValueKind == JsonValueKind.Object &&
            d.TryGetProperty("placeId", out var pid) && pid.TryGetInt64(out var placeId) &&
            d.TryGetProperty("linkCode", out var lc) && lc.ValueKind == JsonValueKind.String)
        {
            return new PrivateServerRef(placeId, lc.GetString() ?? "");
        }
        throw new InvalidOperationException("That share link is not a private server invite, or it has expired.");
    }

    /// <summary>
    /// Private servers need an access code that Roblox puts in the game page's join script for the link code.
    /// The page layout is not documented, so a missing code produces a clear error instead of a broken launch.
    /// </summary>
    public static async Task<string> GetPrivateAccessCodeAsync(string cookie, long placeId, string linkCode)
    {
        using var res = await SendAsync(HttpMethod.Get,
            $"https://www.roblox.com/games/{placeId}/?privateServerLinkCode={Uri.EscapeDataString(linkCode)}",
            cookie, null, null, "https://www.roblox.com/");

        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("This account's login expired. Remove it and add it again.");

        var html = await res.Content.ReadAsStringAsync();
        var m = PrivateCodeRx.Match(html);
        if (m.Success) return m.Groups[1].Value;

        Log.Warn($"Private server: no access code found on the game page (HTTP {(int)res.StatusCode}, {html.Length} chars).");
        throw new InvalidOperationException(
            "Roblox did not hand out an access code for that private server. The link may be invalid, " +
            "the account may not be allowed in, or Roblox changed its page. Try Browser login mode with the link instead.");
    }

    public static async Task<List<FriendInfo>> GetFriendsAsync(string cookie, long userId)
    {
        using var res = await SendAsync(HttpMethod.Get,
            $"https://friends.roblox.com/v1/users/{userId}/friends", cookie, null, null, "https://www.roblox.com/");
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Roblox says too many requests. Wait a minute and refresh again.");
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Could not load friends (HTTP {(int)res.StatusCode}).");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<FriendInfo>();
        foreach (var f in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = f.GetProperty("id").GetInt64();
            var name = f.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            var display = f.TryGetProperty("displayName", out var d) ? (d.GetString() ?? name) : name;
            list.Add(new FriendInfo(id, name, display));
        }

        // Roblox's friends list often comes back without names, so look them up (100 per request).
        var missing = list.Where(f => string.IsNullOrEmpty(f.Name)).Select(f => f.Id).ToList();
        if (missing.Count > 0)
        {
            var names = new Dictionary<long, (string Name, string Display)>();
            foreach (var chunk in missing.Chunk(100))
            {
                try
                {
                    var body = JsonSerializer.Serialize(new { userIds = chunk, excludeBannedUsers = false });
                    using var r = await PostAsync("https://users.roblox.com/v1/users", cookie, body, "https://www.roblox.com/");
                    if (!r.IsSuccessStatusCode) continue;
                    using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                    foreach (var u in d.RootElement.GetProperty("data").EnumerateArray())
                    {
                        var uid = u.GetProperty("id").GetInt64();
                        var un = u.TryGetProperty("name", out var a) && a.ValueKind == JsonValueKind.String ? (a.GetString() ?? "") : "";
                        var ud = u.TryGetProperty("displayName", out var b) && b.ValueKind == JsonValueKind.String ? (b.GetString() ?? un) : un;
                        names[uid] = (un, ud);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Friend names: " + ex.Message);
                }
            }
            list = list.Select(f => names.TryGetValue(f.Id, out var n)
                ? f with { Name = n.Name, DisplayName = string.IsNullOrEmpty(n.Display) ? n.Name : n.Display }
                : f).ToList();
        }
        return list;
    }

    /// <summary>Presence of up to 100 users per request: 0 offline, 1 online, 2 in game, 3 in Studio.</summary>
    public static async Task<Dictionary<long, FriendPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds)
    {
        var result = new Dictionary<long, FriendPresence>();
        foreach (var chunk in userIds.Chunk(100))
        {
            var body = JsonSerializer.Serialize(new { userIds = chunk });
            using var res = await PostAsync("https://presence.roblox.com/v1/presence/users",
                cookie, body, "https://www.roblox.com/");
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("Roblox says too many requests. Wait a minute and refresh again.");
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Could not load friend status (HTTP {(int)res.StatusCode}).");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            foreach (var p in doc.RootElement.GetProperty("userPresences").EnumerateArray())
            {
                var uid = p.GetProperty("userId").GetInt64();
                int type = p.TryGetProperty("userPresenceType", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                string loc = p.TryGetProperty("lastLocation", out var l) && l.ValueKind == JsonValueKind.String ? (l.GetString() ?? "") : "";
                long place = 0;
                if (p.TryGetProperty("placeId", out var pl) && pl.ValueKind == JsonValueKind.Number) place = pl.GetInt64();
                else if (p.TryGetProperty("rootPlaceId", out var rp) && rp.ValueKind == JsonValueKind.Number) place = rp.GetInt64();
                string? gid = p.TryGetProperty("gameId", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
                result[uid] = new FriendPresence(uid, type, loc, place, gid);
            }
        }
        return result;
    }

    /// <summary>
    /// Asks Roblox's join service which address a server uses, without starting the game.
    /// This is how the server list can show regions before you join. The response is parsed
    /// defensively because Roblox does not document it.
    /// </summary>
    public static async Task<ProbeResult> ProbeServerAddressAsync(string cookie, long placeId, string jobId)
    {
        var body = JsonSerializer.Serialize(new
        {
            placeId,
            isTeleport = false,
            gameId = jobId,
            gameJoinAttemptId = Guid.NewGuid().ToString()
        });

        using var res = await PostAsync("https://gamejoin.roblox.com/v1/join-game-instance",
            cookie, body, $"https://www.roblox.com/games/{placeId}/");

        if (res.StatusCode == HttpStatusCode.TooManyRequests) return new ProbeResult(null, true);
        if (!res.IsSuccessStatusCode) return new ProbeResult(null, false);

        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("joinScript", out var js) ||
                js.ValueKind != JsonValueKind.Object)
                return new ProbeResult(null, false);

            foreach (var listName in new[] { "UdmuxEndpoints", "ServerConnections" })
            {
                if (js.TryGetProperty(listName, out var list) &&
                    list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 &&
                    list[0].ValueKind == JsonValueKind.Object &&
                    list[0].TryGetProperty("Address", out var a) &&
                    a.ValueKind == JsonValueKind.String)
                    return new ProbeResult(a.GetString(), false);
            }

            if (js.TryGetProperty("MachineAddress", out var m) && m.ValueKind == JsonValueKind.String)
                return new ProbeResult(m.GetString(), false);
        }
        catch { }

        return new ProbeResult(null, false);
    }
}
