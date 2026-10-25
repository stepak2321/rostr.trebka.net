using System.IO;
using System.Windows;
using RobloxLauncher.Core;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace RobloxLauncher;

public partial class LoginWindow : Window
{
    public string? Cookie { get; private set; }

    private readonly string _dataDir = Path.Combine(
        Path.GetTempPath(), "Rostr_login_" + Guid.NewGuid().ToString("N"));
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _checking;

    public LoginWindow()
    {
        InitializeComponent();
        Loc.Apply(this);
        _poll.Tick += async (_, _) => await CheckAsync();
        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => Cleanup();
    }

    private async Task InitAsync()
    {
        try
        {
            // A fresh profile every time, so adding a second account never reuses the first login.
            var env = await CoreWebView2Environment.CreateAsync(null, _dataDir);
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.Navigate("https://www.roblox.com/login");
            _poll.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                Loc.T("Could not start the embedded browser. The WebView2 runtime may be missing.\n\n" +
                ex.Message + "\n\nYou can paste a cookie in the Accounts window instead."),
                Loc.T("Add account"), MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    private async Task CheckAsync()
    {
        if (_checking || Web.CoreWebView2 == null) return;
        _checking = true;
        try
        {
            var cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var c = cookies.FirstOrDefault(x => x.Name == ".ROBLOSECURITY");
            if (c != null && c.Value.Length > 50)
            {
                Cookie = c.Value;
                _poll.Stop();
                DialogResult = true;
            }
        }
        catch { }
        finally
        {
            _checking = false;
        }
    }

    private void Cleanup()
    {
        _poll.Stop();
        try { Web.Dispose(); } catch { }

        var dir = _dataDir;
        _ = Task.Run(async () =>
        {
            // The browser process may keep files locked for a moment after closing.
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    return;
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        });
    }
}
