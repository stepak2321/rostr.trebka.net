namespace RobloxLauncher.Core;

/// <summary>
/// Shareable server links: rostr://join?place=&lt;placeId&gt;&amp;job=&lt;serverId&gt;.
/// A friend with Rostr installed opens the link and lands in the same server.
/// </summary>
public sealed record ShareLink(long PlaceId, string JobId)
{
    public const string Scheme = "rostr";

    public static string Build(long placeId, string jobId) =>
        $"{Scheme}://join?place={placeId}&job={Uri.EscapeDataString(jobId)}";

    public static bool IsShareLink(string? s) =>
        s != null && s.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when the text is not a well-formed join link (wrong host, bad place id, bad server id).</summary>
    public static ShareLink? TryParse(string? uri)
    {
        if (!IsShareLink(uri)) return null;
        if (!Uri.TryCreate(uri!.Trim().Trim('"'), UriKind.Absolute, out var u)) return null;
        if (!u.Host.Equals("join", StringComparison.OrdinalIgnoreCase)) return null;

        string? place = null, job = null;
        foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            if (i <= 0) continue;
            var k = part[..i];
            var v = Uri.UnescapeDataString(part[(i + 1)..]);
            if (k.Equals("place", StringComparison.OrdinalIgnoreCase)) place = v;
            else if (k.Equals("job", StringComparison.OrdinalIgnoreCase)) job = v;
        }

        if (!long.TryParse(place, out var placeId) || placeId <= 0) return null;
        // Roblox server ids are GUIDs. Anything else is rejected so nothing odd reaches the launch command.
        if (!Guid.TryParse(job, out var guid)) return null;
        return new ShareLink(placeId, guid.ToString("D"));
    }
}
