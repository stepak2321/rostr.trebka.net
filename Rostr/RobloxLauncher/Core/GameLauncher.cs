using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace RobloxLauncher.Core;

public record LaunchLink(
    string Ticket, string PlaceUrl, string? Tracker,
    string? LaunchTime, string? Rloc, string? Gloc, long PlaceId);

public static class GameLauncher
{
    public static LaunchLink Parse(string uri)
    {
        if (!uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a roblox-player link.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri["roblox-player:".Length..].Split('+'))
        {
            var i = part.IndexOf(':');
            if (i > 0) values[part[..i]] = part[(i + 1)..];
        }

        values.TryGetValue("gameinfo", out var ticket);
        values.TryGetValue("placelauncherurl", out var placeUrl);
        values.TryGetValue("browsertrackerid", out var tracker);
        values.TryGetValue("launchtime", out var launchTime);
        values.TryGetValue("robloxLocale", out var rloc);
        values.TryGetValue("gameLocale", out var gloc);

        if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(placeUrl))
            throw new InvalidOperationException("The link is missing the login ticket or place URL.");

        placeUrl = Uri.UnescapeDataString(placeUrl);

        // Safety: only accept Roblox's own servers
        if (!Uri.TryCreate(placeUrl, UriKind.Absolute, out var parsed) || parsed == null ||
            !parsed.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The link points to a non-Roblox server. Launch cancelled.");

        var m = Regex.Match(placeUrl, @"[?&]placeId=(\d+)", RegexOptions.IgnoreCase);
        var request = QueryValue(parsed, "request");
        var followUser = string.Equals(request, "RequestFollowUser", StringComparison.OrdinalIgnoreCase);
        if (followUser && (!long.TryParse(QueryValue(parsed, "userId"), out var userId) || userId <= 0))
            throw new InvalidOperationException("The friend-join link is missing a valid user ID.");
        if (!m.Success && !followUser)
            throw new InvalidOperationException("Could not find the game ID in the link.");

        var placeId = m.Success ? long.Parse(m.Groups[1].Value) : 0;
        return new LaunchLink(ticket, placeUrl, tracker, launchTime, rloc, gloc, placeId);
    }

    public static bool IsDirectJoin(LaunchLink link)
    {
        if (!Uri.TryCreate(link.PlaceUrl, UriKind.Absolute, out var uri) || uri == null) return false;

        var request = QueryValue(uri, "request");
        if (string.Equals(request, "RequestFollowUser", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(QueryValue(uri, "userId"), out var userId) && userId > 0;

        var gameId = QueryValue(uri, "gameId") ?? QueryValue(uri, "gameInstanceId");
        if (!string.IsNullOrEmpty(gameId) && Guid.TryParse(gameId, out _)) return true;

        return string.Equals(request, "RequestPrivateGame", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(QueryValue(uri, "accessCode"));
    }

    public static bool IsDirectJoinUri(string uri)
    {
        try { return IsDirectJoin(Parse(uri)); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 ||
                !Uri.UnescapeDataString(part[..separator]).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return Uri.UnescapeDataString(part[(separator + 1)..]);
        }
        return null;
    }

    public static Process? Launch(LaunchLink link, string versionDir, string? jobId = null)
    {
        var placeUrl = link.PlaceUrl;

        if (!string.IsNullOrEmpty(jobId))
        {
            if (!Regex.IsMatch(jobId, @"^[0-9a-fA-F-]{8,64}$"))
                throw new ArgumentException("Invalid server id.");

            placeUrl = Regex.Replace(placeUrl, @"&gameId=[^&]*", "", RegexOptions.IgnoreCase);
            placeUrl = Regex.Replace(placeUrl, @"request=RequestGame(Job|PrivateServer)?",
                "request=RequestGameJob", RegexOptions.IgnoreCase);
            placeUrl += "&gameId=" + jobId;
        }

        var exe = Path.Combine(versionDir, "RobloxPlayerBeta.exe");
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = versionDir, UseShellExecute = false };

        psi.ArgumentList.Add("--app");
        psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(link.Ticket);
        psi.ArgumentList.Add("-j"); psi.ArgumentList.Add(placeUrl);
        if (!string.IsNullOrEmpty(link.Tracker)) { psi.ArgumentList.Add("-b"); psi.ArgumentList.Add(link.Tracker); }
        if (!string.IsNullOrEmpty(link.LaunchTime)) psi.ArgumentList.Add($"--launchtime={link.LaunchTime}");
        if (!string.IsNullOrEmpty(link.Rloc)) { psi.ArgumentList.Add("--rloc"); psi.ArgumentList.Add(link.Rloc); }
        if (!string.IsNullOrEmpty(link.Gloc)) { psi.ArgumentList.Add("--gloc"); psi.ArgumentList.Add(link.Gloc); }

        return Process.Start(psi);
    }
}