using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace RobloxLauncher.Core;

/// <summary>
/// A themed HTML viewer inside the app (WebView2) for GitHub pages such as the README and the change log.
/// Links open in the normal browser; the viewer itself never navigates away.
/// </summary>
public sealed class HtmlPane
{
    private readonly Border _host;
    private readonly Action<string> _openUrl;
    private Microsoft.Web.WebView2.Wpf.WebView2? _web;
    private string? _shownKey;

    public HtmlPane(Border host, Action<string> openUrl)
    {
        _host = host;
        _openUrl = openUrl;
    }

    /// <summary>True when this exact content is already on screen in the current theme.</summary>
    public bool IsShowing(string key) => _web?.CoreWebView2 != null && _shownKey == key + "|" + ThemeManager.Current;

    /// <summary>Shows the HTML. Returns false when the embedded browser cannot start.</summary>
    public async Task<bool> ShowAsync(string bodyHtml, string key)
    {
        if (!await EnsureAsync()) return false;
        if (IsShowing(key)) return true;
        _web!.CoreWebView2.NavigateToString(BuildPage(bodyHtml));
        _shownKey = key + "|" + ThemeManager.Current;
        return true;
    }

    private async Task<bool> EnsureAsync()
    {
        if (_web?.CoreWebView2 != null) return true;
        try
        {
            if (_web == null)
            {
                var bg = ((SolidColorBrush)ThemeManager.B("ThCard")).Color;
                _web = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B)
                };
                _host.Child = _web;
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rostr", "readme-web");
            var env = await CoreWebView2Environment.CreateAsync(null, dir);
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            core.NavigationStarting += (_, a) =>
            {
                var uri = a.Uri ?? "";
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                a.Cancel = true;
                if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) _openUrl(uri);
            };
            core.NewWindowRequested += (_, a) =>
            {
                a.Handled = true;
                if (a.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) _openUrl(a.Uri);
            };
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Embedded browser failed: " + ex.Message);
            _host.Child = new TextBlock
            {
                Text = Loc.T("Could not start the embedded browser. The WebView2 runtime may be missing."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20),
                Foreground = ThemeManager.B("ThMuted")
            };
            _web = null;
            return false;
        }
    }

    private static string Hex(string key)
    {
        var c = ((SolidColorBrush)ThemeManager.B(key)).Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static string BuildPage(string body)
    {
        string bg = Hex("ThCard"), bg2 = Hex("ThBg"), line = Hex("ThLine"), text = Hex("ThText2"),
               head = Hex("ThText"), muted = Hex("ThMuted"), accent = Hex("ThAccent");
        return $@"<!doctype html><html><head><meta charset=""utf-8"">
<style>
  html {{ color-scheme: {(ThemeManager.Current == "Light" ? "light" : "dark")}; }}
  body {{ margin:0; background:{bg}; color:{text}; font:14px/1.65 'Segoe UI',Arial,sans-serif; }}
  .wrap {{ padding:22px 28px 40px; max-width:900px; }}
  h1,h2,h3,h4,h5,h6 {{ color:{head}; line-height:1.3; margin:1.6em 0 .6em; }}
  h1 {{ font-size:26px; padding-bottom:.3em; border-bottom:1px solid {line}; margin-top:0; }}
  h2 {{ font-size:21px; padding-bottom:.3em; border-bottom:1px solid {line}; }}
  h3 {{ font-size:17px; }}
  a {{ color:{accent}; text-decoration:none; }} a:hover {{ text-decoration:underline; }}
  .anchor {{ display:none; }}
  img {{ max-width:100%; }}
  code {{ background:{bg2}; padding:2px 6px; border-radius:5px; font:12.5px Consolas,monospace; }}
  pre {{ background:{bg2}; padding:14px 16px; border-radius:9px; overflow:auto; }}
  pre code {{ background:none; padding:0; }}
  blockquote {{ margin:1em 0; padding:2px 16px; border-left:4px solid {line}; color:{muted}; }}
  table {{ border-collapse:collapse; margin:1em 0; display:block; overflow:auto; }}
  th,td {{ border:1px solid {line}; padding:7px 13px; }} th {{ background:{bg2}; color:{head}; }}
  hr {{ border:0; border-top:1px solid {line}; margin:1.8em 0; }}
  ul,ol {{ padding-left:1.6em; }} li {{ margin:.25em 0; }}
  .note {{ color:{muted}; }}
  ::-webkit-scrollbar {{ width:10px; height:10px; }}
  ::-webkit-scrollbar-thumb {{ background:{line}; border-radius:6px; }}
</style></head><body><div class=""wrap"">{body}</div>
<script>
  // Table-of-contents links jump inside the page. GitHub prefixes heading ids with ""user-content-"".
  document.addEventListener('click', function (e) {{
    var a = e.target.closest ? e.target.closest('a[href^=""#""]') : null;
    if (!a) return;
    e.preventDefault();
    var id = decodeURIComponent(a.getAttribute('href').slice(1));
    var el = document.getElementById('user-content-' + id) || document.getElementById(id);
    if (el) el.scrollIntoView();
  }});
</script></body></html>";
    }
}
