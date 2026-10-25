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
        if (!Uri.TryCreate(placeUrl, UriKind.Absolute, out var parsed) ||
            !parsed.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The link points to a non-Roblox server. Launch cancelled.");

        var m = Regex.Match(placeUrl, @"[?&]placeId=(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            throw new InvalidOperationException("Could not find the game ID in the link.");

        return new LaunchLink(ticket, placeUrl, tracker, launchTime, rloc, gloc, long.Parse(m.Groups[1].Value));
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