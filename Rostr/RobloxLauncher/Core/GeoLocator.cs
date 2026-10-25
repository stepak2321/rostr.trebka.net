using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace RobloxLauncher.Core;

public static class GeoLocator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, string> Cache = new();

    // Tries ipinfo.io first, then ipwho.is, so one provider being down or rate limited is not fatal.
    public static async Task<string?> LookupAsync(string ip)
    {
        if (!IPAddress.TryParse(ip, out _)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(ip, out var cached)) return cached;
        }

        var text = await FromIpInfoAsync(ip) ?? await FromIpWhoIsAsync(ip);
        if (text != null)
        {
            lock (Cache) Cache[ip] = text;
        }
        return text;
    }

    private static string? Join(params string[] parts)
    {
        var text = string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        return text.Length == 0 ? null : text;
    }

    private static async Task<string?> FromIpInfoAsync(string ip)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://ipinfo.io/{ip}/json"));
            var r = doc.RootElement;
            string Get(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "") : "";
            return Join(Get("city"), Get("region"), Get("country"));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> FromIpWhoIsAsync(string ip)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://ipwho.is/{ip}"));
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            string Get(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "") : "";
            return Join(Get("city"), Get("region"), Get("country_code"));
        }
        catch
        {
            return null;
        }
    }
}
