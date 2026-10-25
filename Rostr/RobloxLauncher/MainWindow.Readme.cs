using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using RobloxLauncher.Core;

namespace RobloxLauncher;

// The "Open README" page: the README of the GitHub repository, shown inside the app (no browser window).
// It is fetched from the GitHub API, cached on disk for offline use, and styled with the current theme.
public partial class MainWindow : Window
{
    private const string ReadmeRepo = ProjectInfo.GitHubRepo;

    // Asks GitHub for ready-made HTML (README body, release notes).
    private static readonly HttpClient GitHubHttp = CreateGitHubClient();

    private static HttpClient CreateGitHubClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Rostr-Readme/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.html+json");
        return c;
    }

    private static readonly string ReadmeCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rostr", "readme-cache.html");

    private HtmlPane? _readmePane;
    private string? _readmeHtml;     // the README as GitHub rendered it
    private bool _readmeBusy;

    private void ReadmeReload_Click(object sender, RoutedEventArgs e) => _ = LoadReadmeAsync(true);

    private void ReadmeGitHub_Click(object sender, RoutedEventArgs e) =>
        OpenUrl($"https://github.com/{ReadmeRepo}#readme");

    private async Task LoadReadmeAsync(bool force)
    {
        if (_readmeBusy) return;
        _readmeBusy = true;
        try
        {
            _readmePane ??= new HtmlPane(ReadmeHost, OpenUrl);

            if (!force && _readmeHtml != null && _readmePane.IsShowing(_readmeHtml.GetHashCode().ToString())) return;

            if (force || _readmeHtml == null)
            {
                ReadmeMsg.Text = Loc.T("Loading...");
                var fetched = await FetchReadmeAsync();
                if (fetched != null)
                {
                    _readmeHtml = fetched;
                    ReadmeMsg.Text = "";
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(ReadmeCachePath)!);
                        File.WriteAllText(ReadmeCachePath, fetched);
                    }
                    catch { /* the cache is only a bonus */ }
                }
                else
                {
                    if (_readmeHtml == null && File.Exists(ReadmeCachePath))
                    {
                        try { _readmeHtml = File.ReadAllText(ReadmeCachePath); } catch { }
                    }
                    ReadmeMsg.Text = _readmeHtml != null
                        ? Loc.T("Could not reach GitHub, showing the saved copy.")
                        : "";
                }
            }

            var body = _readmeHtml ??
                "<p>" + Loc.T("The README could not be loaded. Check your internet connection and press Reload.") + "</p>";
            await _readmePane.ShowAsync(body, body.GetHashCode().ToString());
        }
        catch (Exception ex)
        {
            Log.Warn("README page failed: " + ex.Message);
            ReadmeMsg.Text = ex.Message;
        }
        finally
        {
            _readmeBusy = false;
        }
    }

    private static async Task<string?> FetchReadmeAsync()
    {
        try
        {
            using var resp = await GitHubHttp.GetAsync($"https://api.github.com/repos/{ReadmeRepo}/readme");
            if (!resp.IsSuccessStatusCode) return null;
            var html = await resp.Content.ReadAsStringAsync();

            // Relative images and links in the README only make sense on GitHub; point them there.
            html = Regex.Replace(html, "(<img[^>]*?\\ssrc=\")(?!https?:|data:|//)/?([^\"]+)\"",
                m => $"{m.Groups[1].Value}https://raw.githubusercontent.com/{ReadmeRepo}/HEAD/{m.Groups[2].Value}\"",
                RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "(<a[^>]*?\\shref=\")(?!https?:|#|mailto:|//)/?([^\"]+)\"",
                m => $"{m.Groups[1].Value}https://github.com/{ReadmeRepo}/blob/HEAD/{m.Groups[2].Value}\"",
                RegexOptions.IgnoreCase);
            return html;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not download the README: " + ex.Message);
            return null;
        }
    }
}
