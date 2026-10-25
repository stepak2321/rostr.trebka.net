using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using RobloxLauncher.Core;

namespace RobloxLauncher;

// The change log page: the release notes of one version, read from the GitHub release and shown inside the app.
// It shows the version that is about to be installed; with no update waiting it shows the installed version.
public partial class MainWindow : Window
{
    private HtmlPane? _changelogPane;
    private Version? _changelogVersion;
    private int _changelogReturnPage = 2;
    private bool _changelogBusy;
    private readonly Dictionary<Version, (bool Found, string Html, string Url)> _changelogCache = new();

    /// <summary>Opens the change log of a version (default: the update waiting to be installed, else the running version).</summary>
    private void OpenChangelog(Version? version = null)
    {
        _changelogVersion = version ?? _availableUpdate?.Version ?? Updater.CurrentVersion;
        if (_page != 5) _changelogReturnPage = _page;
        ShowPage(5);
    }

    private void SeeChangelog_Click(object sender, RoutedEventArgs e) => OpenChangelog();

    private void ChangelogBack_Click(object sender, RoutedEventArgs e) => ShowPage(_changelogReturnPage);

    private void ChangelogReload_Click(object sender, RoutedEventArgs e) => _ = LoadChangelogAsync(true);

    private void ChangelogGitHub_Click(object sender, RoutedEventArgs e)
    {
        var v = _changelogVersion;
        if (v != null && _changelogCache.TryGetValue(v, out var c) && c.Url.Length > 0) OpenUrl(c.Url);
        else OpenUrl($"https://github.com/{ReadmeRepo}/releases");
    }

    private void ChangelogInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate != null) StartUpdate(_availableUpdate);
    }

    private async Task LoadChangelogAsync(bool force)
    {
        if (_changelogBusy || _changelogVersion == null) return;
        _changelogBusy = true;
        var version = _changelogVersion;
        try
        {
            _changelogPane ??= new HtmlPane(ChangelogHost, OpenUrl);

            var waiting = _availableUpdate != null && _availableUpdate.Version == version;
            ChangelogTitle.Text = Loc.T($"Change log {version}");
            ChangelogSub.Text = waiting
                ? Loc.T($"What is new in version {version}, the update waiting to be installed")
                : Loc.T($"Version {version}");
            ChangelogInstallBtn.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            if (waiting) ChangelogInstallBtn.Content = Loc.T($"Install {version}");

            if (force || !_changelogCache.ContainsKey(version))
            {
                ChangelogMsg.Text = Loc.T("Loading...");
                var fetched = await FetchReleaseNotesAsync(version);
                if (fetched == null)
                {
                    ChangelogMsg.Text = Loc.T("Could not reach GitHub.");
                    if (!_changelogCache.ContainsKey(version))
                    {
                        await _changelogPane.ShowAsync("<p class=\"note\">" +
                            Loc.T("The change log could not be loaded. Check your internet connection and press Reload.") +
                            "</p>", "error");
                        return;
                    }
                }
                else
                {
                    _changelogCache[version] = fetched.Value;
                    ChangelogMsg.Text = "";
                }
            }

            var (found, html, _) = _changelogCache[version];
            string body;
            if (!found)
                body = "<p class=\"note\">" + Loc.T($"No GitHub release was found for version {version}.") + "</p>";
            else if (string.IsNullOrWhiteSpace(html))
                body = "<p class=\"note\">" + Loc.T("No change log was written for this release.") + "</p>";
            else
                body = html;

            await _changelogPane.ShowAsync(body, version + body.GetHashCode().ToString());
        }
        catch (Exception ex)
        {
            Log.Warn("Change log page failed: " + ex.Message);
            ChangelogMsg.Text = ex.Message;
        }
        finally
        {
            _changelogBusy = false;
            // The version may have been switched while this was loading (for example by a new update check).
            if (_page == 5 && _changelogVersion != version) _ = LoadChangelogAsync(false);
        }
    }

    /// <summary>Reads the release whose tag matches the version. Null = GitHub could not be reached.</summary>
    private static async Task<(bool Found, string Html, string Url)?> FetchReleaseNotesAsync(Version version)
    {
        try
        {
            using var resp = await GitHubHttp.GetAsync($"https://api.github.com/repos/{ReadmeRepo}/releases?per_page=50");
            if (resp.StatusCode == HttpStatusCode.NotFound) return (false, "", "");
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var tag = Str(r, "tag_name");
                var m = Regex.Match(tag, @"^[vV]?(\d+(\.\d+){1,3})");
                if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) continue;
                if (new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) != version) continue;

                var html = Str(r, "body_html");
                if (html.Length == 0)
                {
                    var text = Str(r, "body");
                    if (text.Length > 0) html = "<pre>" + WebUtility.HtmlEncode(text) + "</pre>";
                }
                return (true, html, Str(r, "html_url"));
            }
            return (false, "", "");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not download the change log: " + ex.Message);
            return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
}
