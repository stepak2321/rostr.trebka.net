using System.Net.Http;
using System.Text.Json;

namespace RobloxLauncher.Core;

public record GameInfo(long UniverseId, string Name, string Creator, long Playing, long Visits,
                       string? IconUrl, string? BannerUrl);
public record ServerInfo(string Id, int Playing, int MaxPlayers, int Ping, double Fps, DateTime? Created = null);
public record SearchResult(long UniverseId, long PlaceId, string Name, long Playing, int? LikePercent);
public record ServerPage(List<ServerInfo> Servers, string? NextCursor);

public static class RobloxApi
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Rostr/1.2");
        return c;
    }

    public static async Task<GameInfo> GetGameAsync(long placeId)
    {
        long universeId;
        using (var doc = JsonDocument.Parse(await Http.GetStringAsync(
            $"https://apis.roblox.com/universes/v1/places/{placeId}/universe")))
        {
            universeId = doc.RootElement.GetProperty("universeId").GetInt64();
        }

        string name = $"Game {placeId}", creator = "unknown";
        long playing = 0, visits = 0;

        using (var doc = JsonDocument.Parse(await Http.GetStringAsync(
            $"https://games.roblox.com/v1/games?universeIds={universeId}")))
        {
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() > 0)
            {
                var g = data[0];
                name = g.GetProperty("name").GetString() ?? name;
                if (g.TryGetProperty("playing", out var p)) playing = p.GetInt64();
                if (g.TryGetProperty("visits", out var v)) visits = v.GetInt64();
                if (g.TryGetProperty("creator", out var c) && c.TryGetProperty("name", out var cn))
                    creator = cn.GetString() ?? creator;
            }
        }

        var icon = await GetIconByUniverseAsync(universeId);

        // Roblox answers "Pending" (with an empty address) for a picture it has not rendered yet,
        // so ask again a few times instead of giving up on the first answer.
        string? banner = null;
        for (var attempt = 0; attempt < 3 && string.IsNullOrEmpty(banner); attempt++)
        {
            if (attempt > 0) await Task.Delay(1200);
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                    $"https://thumbnails.roblox.com/v1/games/multiget/thumbnails?universeIds={universeId}" +
                    "&countPerUniverse=1&defaults=true&size=768x432&format=Png&isCircular=false"));
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0)
                {
                    var th = data[0].GetProperty("thumbnails");
                    if (th.GetArrayLength() > 0 &&
                        th[0].TryGetProperty("imageUrl", out var u) && u.ValueKind == JsonValueKind.String)
                        banner = u.GetString();
                }
            }
            catch { }
        }
        if (string.IsNullOrEmpty(banner)) banner = null;

        return new GameInfo(universeId, name, creator, playing, visits, icon, banner);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> IconUrls = new();

    /// <summary>Icon address for a game, looked up from its place id. Used when a saved game has no icon yet.</summary>
    public static async Task<string?> GetIconUrlAsync(long placeId)
    {
        if (IconUrls.TryGetValue(placeId, out var cached)) return cached;
        try
        {
            long universeId;
            using (var doc = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://apis.roblox.com/universes/v1/places/{placeId}/universe")))
            {
                universeId = doc.RootElement.GetProperty("universeId").GetInt64();
            }
            var url = await GetIconByUniverseAsync(universeId);
            if (!string.IsNullOrEmpty(url)) IconUrls[placeId] = url;
            return url;
        }
        catch { return null; }
    }

    private static async Task<string?> GetIconByUniverseAsync(long universeId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(1200);
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                    $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}" +
                    "&returnPolicy=PlaceHolder&size=256x256&format=Png&isCircular=false"));
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0 &&
                    data[0].TryGetProperty("imageUrl", out var u) && u.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(u.GetString()))
                    return u.GetString();
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Finds games by name with Roblox's public search. Tries the current search service first and the older
    /// game list second, because Roblox has moved this endpoint before. Returns an empty list for no matches.
    /// </summary>
    public static async Task<List<SearchResult>> SearchGamesAsync(string query, int max = 12)
    {
        query = query.Trim();
        if (query.Length == 0) return new();

        try
        {
            var list = await SearchOmniAsync(query, max);
            if (list.Count > 0) return list;
        }
        catch (Exception ex)
        {
            Log.Warn("Game search (omni) failed: " + ex.Message);
        }

        return await SearchLegacyAsync(query, max);
    }

    private static async Task<List<SearchResult>> SearchOmniAsync(string query, int max)
    {
        var url = "https://apis.roblox.com/search-api/omni-search?searchQuery=" + Uri.EscapeDataString(query) +
                  "&pageType=all&sessionId=" + Guid.NewGuid();
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));

        var found = new List<SearchResult>();
        if (!doc.RootElement.TryGetProperty("searchResults", out var groups) ||
            groups.ValueKind != JsonValueKind.Array) return found;

        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("contentGroupType", out var t) || t.GetString() != "Game") continue;
            if (!group.TryGetProperty("contents", out var contents) ||
                contents.ValueKind != JsonValueKind.Array) continue;

            foreach (var g in contents.EnumerateArray())
            {
                var place = (long)Num(g, "rootPlaceId");
                var universe = (long)Num(g, "universeId");
                var name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (place == 0 || universe == 0 || name.Length == 0) continue;

                found.Add(new SearchResult(universe, place, name, (long)Num(g, "playerCount"),
                    Percent(Num(g, "totalUpVotes"), Num(g, "totalDownVotes"))));
                if (found.Count >= max) return found;
            }
        }
        return found;
    }

    private static async Task<List<SearchResult>> SearchLegacyAsync(string query, int max)
    {
        var url = "https://games.roblox.com/v1/games/list?model.keyword=" + Uri.EscapeDataString(query) +
                  "&model.maxRows=" + max;
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));

        var found = new List<SearchResult>();
        if (!doc.RootElement.TryGetProperty("games", out var games) ||
            games.ValueKind != JsonValueKind.Array) return found;

        foreach (var g in games.EnumerateArray())
        {
            var place = (long)Num(g, "placeId");
            var universe = (long)Num(g, "universeId");
            var name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (place == 0 || universe == 0 || name.Length == 0) continue;

            found.Add(new SearchResult(universe, place, name, (long)Num(g, "playerCount"),
                Percent(Num(g, "totalUpVotes"), Num(g, "totalDownVotes"))));
        }
        return found;
    }

    private static int? Percent(double up, double down) =>
        up + down <= 0 ? null : (int)Math.Round(up * 100.0 / (up + down));

    /// <summary>Icon addresses for many games at once (one request), keyed by universe id.</summary>
    public static async Task<Dictionary<long, string>> GetIconUrlsAsync(IEnumerable<long> universeIds)
    {
        var map = new Dictionary<long, string>();
        var ids = universeIds.Distinct().Take(50).ToList();
        if (ids.Count == 0) return map;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0) await Task.Delay(1200);
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                    "https://thumbnails.roblox.com/v1/games/icons?universeIds=" + string.Join(",", ids) +
                    "&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false"));

                foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    if (d.TryGetProperty("targetId", out var id) && id.ValueKind == JsonValueKind.Number &&
                        d.TryGetProperty("imageUrl", out var u) && u.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(u.GetString()))
                        map[id.GetInt64()] = u.GetString()!;
                }
                if (map.Count >= ids.Count) break;
            }
            catch (Exception ex)
            {
                Log.Warn("Search icons: " + ex.Message);
            }
        }
        return map;
    }

    /// <summary>One page (up to 100) of public servers. Pass the previous page's NextCursor for the next one.</summary>
    public static async Task<ServerPage> GetServersAsync(long placeId, bool ascending, string? cursor = null)
    {
        var order = ascending ? "Asc" : "Desc";
        var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public" +
                  $"?sortOrder={order}&excludeFullGames=true&limit=100";
        if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + Uri.EscapeDataString(cursor);

        using var res = await Http.GetAsync(url);
        if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Roblox says too many requests. Wait a few seconds and try again.");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<ServerInfo>();
        foreach (var s in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new ServerInfo(
                s.GetProperty("id").GetString() ?? "",
                (int)Num(s, "playing"),
                (int)Num(s, "maxPlayers"),
                (int)Num(s, "ping"),
                Num(s, "fps"),
                ReadDate(s, "created") ?? ReadDate(s, "createdAt")));
        }

        string? next = null;
        if (doc.RootElement.TryGetProperty("nextPageCursor", out var c) && c.ValueKind == JsonValueKind.String)
            next = c.GetString();
        return new ServerPage(list, string.IsNullOrEmpty(next) ? null : next);
    }

    // The public server list does not document a creation time today. If Roblox adds one, it is picked up.
    private static DateTime? ReadDate(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                                               System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d : null;

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}