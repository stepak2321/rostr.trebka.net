using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace RobloxLauncher.Core;

/// <summary>
/// Downloads pictures (game icons, profile pictures) ourselves and hands WPF a finished, frozen bitmap.
/// Letting WPF fetch a web address by itself fails silently, which is why images used to stay empty.
/// </summary>
public static class ImageService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, BitmapImage> Images = new();
    private static readonly ConcurrentDictionary<long, BitmapImage> Headshots = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Rostr/1.2");
        return c;
    }

    /// <summary>Downloads one picture. Returns null when it cannot be loaded.</summary>
    public static async Task<BitmapImage?> LoadAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Images.TryGetValue(url, out var hit)) return hit;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            var bi = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
            }
            bi.Freeze();   // frozen = safe to hand to the UI thread
            Images[url] = bi;
            return bi;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not load image " + url + ": " + ex.Message);
            return null;
        }
    }

    public static BitmapImage? CachedHeadshot(long userId) =>
        Headshots.TryGetValue(userId, out var b) ? b : null;

    /// <summary>
    /// Profile pictures for the given users (batched, 100 per request). Roblox sometimes answers
    /// "Pending" for a picture it has not rendered yet, so those are asked again a few times.
    /// </summary>
    public static async Task<Dictionary<long, BitmapImage>> EnsureHeadshotsAsync(IEnumerable<long> userIds)
    {
        var want = userIds.Distinct().ToList();
        var missing = want.Where(id => !Headshots.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            var urls = await GetHeadshotUrlsAsync(missing).ConfigureAwait(false);
            using var gate = new SemaphoreSlim(8);
            await Task.WhenAll(urls.Select(async kv =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var img = await LoadAsync(kv.Value).ConfigureAwait(false);
                    if (img != null) Headshots[kv.Key] = img;
                }
                finally { gate.Release(); }
            })).ConfigureAwait(false);
        }

        var result = new Dictionary<long, BitmapImage>();
        foreach (var id in want)
            if (Headshots.TryGetValue(id, out var b)) result[id] = b;
        return result;
    }

    private static async Task<Dictionary<long, string>> GetHeadshotUrlsAsync(List<long> ids)
    {
        var result = new Dictionary<long, string>();
        var pending = ids;

        for (var attempt = 0; attempt < 4 && pending.Count > 0; attempt++)
        {
            if (attempt > 0) await Task.Delay(1500).ConfigureAwait(false);
            var again = new List<long>();

            foreach (var chunk in pending.Chunk(100))
            {
                try
                {
                    var url = "https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=" +
                              string.Join(",", chunk) + "&size=150x150&format=Png&isCircular=true";
                    using var doc = JsonDocument.Parse(await Http.GetStringAsync(url).ConfigureAwait(false));

                    foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
                    {
                        var id = d.GetProperty("targetId").GetInt64();
                        var state = d.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String
                            ? s.GetString() : "";
                        var img = d.TryGetProperty("imageUrl", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString() : null;

                        if (!string.IsNullOrEmpty(img)) result[id] = img;
                        else if (state == "Pending") again.Add(id);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Profile picture request failed: " + ex.Message);
                    again.AddRange(chunk);
                }
            }
            pending = again;
        }
        return result;
    }
}
