using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxLauncher.Core;

/// <summary>
/// Finds the Fandom wiki of a Roblox game by its name. It searches the web for "[game name] wiki" and takes the first
/// result that is on fandom.com. If the search gives nothing, a few likely Fandom addresses are tried
/// ("Lumber Inc" -> lumber-inc.fandom.com); a subdomain that does not exist sends you to another site, so those are easy to tell apart.
/// </summary>
public static class WikiFinder
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 Rostr");
        return c;
    }

    /// <summary>The Fandom search page for a game, for when no wiki with a matching address exists.</summary>
    public static string SearchUrl(string gameName) =>
        "https://www.fandom.com/search?query=" + Uri.EscapeDataString(Words(gameName) + " roblox");

    /// <summary>The address of the game's wiki, or null when none was found.</summary>
    public static async Task<string?> FindAsync(string gameName)
    {
        var plain = Words(gameName);
        if (plain.Length == 0) return null;

        // The guessed addresses are checked while the search runs, so the fallback costs no extra waiting.
        var slugs = Slugs(gameName);
        var guessTask = Task.WhenAll(slugs.Select(CheckAsync));

        var found = await SearchFirstFandomAsync(plain + " wiki");
        if (found != null) return found;

        var guesses = await guessTask;
        return guesses.FirstOrDefault(r => r != null);   // keeps the order of the guesses
    }

    // ---------- Web search ----------

    private static async Task<string?> SearchFirstFandomAsync(string query)
    {
        var q = Uri.EscapeDataString(query);
        return await TrySearchAsync("https://html.duckduckgo.com/html/?q=" + q)
            ?? await TrySearchAsync("https://www.bing.com/search?q=" + q);
    }

    private static async Task<string?> TrySearchAsync(string searchUrl)
    {
        try
        {
            var html = await Http.GetStringAsync(searchUrl);
            foreach (var url in ResultLinks(html))
                if (IsWikiPage(url, out var uri)) return uri!.ToString();
        }
        catch { }
        return null;
    }

    // Every address in a results page, in the order they appear. Search engines wrap links in a redirect, so
    // the real address is read out of it (DuckDuckGo: uddg=..., Bing: u=a1 + base64).
    private static IEnumerable<string> ResultLinks(string html)
    {
        foreach (Match m in Regex.Matches(html, "href=\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value);
            if (href.StartsWith("//")) href = "https:" + href;
            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

            var inner = Param(href, "uddg") ?? BingTarget(Param(href, "u"));
            yield return inner ?? href;
        }
    }

    private static string? Param(string url, string name)
    {
        var m = Regex.Match(url, "[?&]" + name + "=([^&]+)");
        if (!m.Success) return null;
        try { return Uri.UnescapeDataString(m.Groups[1].Value); } catch { return null; }
    }

    private static string? BingTarget(string? u)
    {
        if (u == null || !u.StartsWith("a1") || u.Length < 4) return null;
        try
        {
            var b64 = u.Substring(2).Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch { return null; }
    }

    // A page on a Fandom wiki (not the Fandom front page or the Community Central help wiki).
    private static bool IsWikiPage(string url, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host.ToLowerInvariant();
        if (!host.EndsWith(".fandom.com")) return false;
        if (host is "www.fandom.com" or "community.fandom.com" or "support.fandom.com" or "about.fandom.com") return false;
        uri = u;
        return true;
    }

    private static async Task<string?> CheckAsync(string slug)
    {
        try
        {
            using var r = await Http.GetAsync($"https://{slug}.fandom.com/");
            var final = r.RequestMessage?.RequestUri;
            if (r.IsSuccessStatusCode && final != null &&
                final.Host.Equals($"{slug}.fandom.com", StringComparison.OrdinalIgnoreCase))
                return final.ToString();
        }
        catch { }
        return null;
    }

    // Game names carry tags like "[UPDATE]" or "(Beta)" and decoration. Only the plain words are kept.
    private static string Words(string name)
    {
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in name)
        {
            if (ch == '[' || ch == '(') { depth++; continue; }
            if (ch == ']' || ch == ')') { if (depth > 0) depth--; continue; }
            if (depth > 0) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<string> Slugs(string name)
    {
        var tokens = Words(name).ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray()))   // plain a-z, 0-9 only
            .Where(t => t.Length > 0)
            .ToList();
        tokens.RemoveAll(t => t == "roblox");
        if (tokens.Count == 0) return new();

        var list = new List<string> { string.Join('-', tokens), string.Join("", tokens), string.Join('-', tokens) + "-roblox" };
        if (tokens[0] == "the" && tokens.Count > 1)
        {
            list.Add(string.Join('-', tokens.Skip(1)));
            list.Add(string.Join("", tokens.Skip(1)));
        }
        return list.Where(s => s.Length is >= 2 and <= 60).Distinct().ToList();
    }
}
