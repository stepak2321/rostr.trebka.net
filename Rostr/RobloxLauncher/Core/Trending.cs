using System.Net.Http;
using System.Text.Json;

namespace RobloxLauncher.Core;

public record TrendGame(long PlaceId, long UniverseId, string Name, long Playing, long Peak24h);
public record TrendPoint(DateTime Time, long Players);

/// <summary>
/// Reads the player counts that the GitHub workflow collects (trending/ folder of the repository).
/// Nothing is collected on your PC: the list is the place IDs in trending/places.json.
/// </summary>
public static class Trending
{
    /// <summary>Branch of the repository that holds the trending/ folder.</summary>
    public const string Branch = "main";

    private static readonly HttpClient Http = CreateClient();
    private static string BaseUrl => $"https://raw.githubusercontent.com/{ProjectInfo.GitHubRepo}/{Branch}/trending/";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);   // GitHub itself caches raw files for about 5 minutes
    private static (DateTime At, List<TrendGame> Games, DateTime Updated)? _index;
    private static readonly Dictionary<long, (DateTime At, List<TrendPoint> Points)> History = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Rostr/1.4");
        return c;
    }

    /// <summary>The games on the list, most players first, and when they were last counted (local time).</summary>
    public static async Task<(List<TrendGame> Games, DateTime Updated)> GetListAsync(bool force = false)
    {
        if (!force && _index is { } hit && DateTime.UtcNow - hit.At < Ttl)
            return (hit.Games, hit.Updated);

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(BaseUrl + "index.json"));
        var root = doc.RootElement;

        var updated = root.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(u.GetInt64()).LocalDateTime
            : DateTime.Now;

        var games = new List<TrendGame>();
        if (root.TryGetProperty("games", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in arr.EnumerateArray())
            {
                games.Add(new TrendGame(
                    Long(g, "placeId"), Long(g, "universeId"),
                    g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Long(g, "playing"), Long(g, "peak24h")));
            }
        }

        _index = (DateTime.UtcNow, games, updated);
        return (games, updated);
    }

    /// <summary>Every saved count of one game (up to 30 days, one per hour), oldest first, in local time.</summary>
    public static async Task<List<TrendPoint>> GetHistoryAsync(long placeId, bool force = false)
    {
        if (!force && History.TryGetValue(placeId, out var hit) && DateTime.UtcNow - hit.At < Ttl)
            return hit.Points;

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(BaseUrl + $"data/{placeId}.json"));
        var points = new List<TrendPoint>();
        if (doc.RootElement.TryGetProperty("points", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2) continue;
                points.Add(new TrendPoint(
                    DateTimeOffset.FromUnixTimeSeconds(p[0].GetInt64()).LocalDateTime,
                    p[1].GetInt64()));
            }
        }
        points.Sort((a, b) => a.Time.CompareTo(b.Time));

        History[placeId] = (DateTime.UtcNow, points);
        return points;
    }

    private static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
