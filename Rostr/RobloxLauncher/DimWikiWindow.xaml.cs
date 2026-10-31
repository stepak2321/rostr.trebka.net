using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace RobloxLauncher;

/// <summary>
/// A small browser inside the dim, for the Fandom wiki of the game. Unlike the other dim windows it can take the
/// keyboard (so you can type in the wiki search); MainWindow keeps the dim on while this window is the active one.
/// </summary>
public partial class DimWikiWindow : Window
{
    // The wiki takes this share of the game window.
    private const double ShareW = 0.70, ShareH = 0.76;

    private int _gx, _gy, _gw, _gh;
    private int _px = int.MinValue, _py, _pw, _ph, _lastW, _lastH;

    private bool _drag;
    private OverlayNative.POINT _dragStart;
    private int _dragWinX, _dragWinY;

    private string? _homeUrl, _searchUrl;
    private bool _ready;
    private string? _pending;

    public event Action? CloseRequested;
    /// <summary>Raised after a drag, with the new position as 0..1 across the free space of the game window.</summary>
    public event Action<double, double>? Moved;

    /// <summary>True once a page was asked for (so reopening the wiki does not start over).</summary>
    public bool HasPage { get; private set; }

    public DimWikiWindow()
    {
        InitializeComponent();
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); CopyGlyph.Text = "\uE8C8"; };
        Loaded += async (_, _) => await InitWebAsync();
        Closed += (_, _) => { try { Web.Dispose(); } catch { } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        OverlayNative.MakeToolWindow(hwnd);
        OverlayNative.RoundCorners(hwnd);
    }

    private async Task InitWebAsync()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rostr", "WebView2Wiki");
            var env = await CoreWebView2Environment.CreateAsync(null, dir);
            await Web.EnsureCoreWebView2Async(env);

            var core = Web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.DocumentTitleChanged += (_, _) => TitleText.Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Game wiki" : core.DocumentTitle;
            core.HistoryChanged += (_, _) => BackBtn.IsEnabled = core.CanGoBack;
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };   // links that want a new tab open here
            core.NavigationCompleted += (_, _) => StatusText.Visibility = Visibility.Collapsed;
            core.SourceChanged += (_, _) => ShowUrl(core.Source);
            core.NavigationStarting += (_, e) => ShowUrl(e.Uri);

            _ready = true;
            if (_pending != null) { core.Navigate(_pending); _pending = null; }
        }
        catch (Exception ex)
        {
            ShowStatus("Could not start the embedded browser. The WebView2 runtime may be missing.\n\n" + ex.Message);
        }
    }

    private void ShowUrl(string? url)
    {
        if (!string.IsNullOrEmpty(url)) UrlBox.Text = url;
    }

    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Clipboard.SetText(url);
            CopyGlyph.Text = "\uE73E";   // check mark for a moment, so you see it worked
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
        catch { }   // the clipboard can be busy; pressing the button again works
    }

    /// <summary>The wiki's start page (null when none was found) and the Fandom search page for the game.</summary>
    public void SetUrls(string? home, string search)
    {
        _homeUrl = home;
        _searchUrl = search;
        HomeBtn.IsEnabled = home != null;
    }

    public void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Load(string url)
    {
        HasPage = true;
        ShowUrl(url);
        if (_ready) Web.CoreWebView2.Navigate(url);
        else _pending = url;
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (_ready && Web.CoreWebView2.CanGoBack) Web.CoreWebView2.GoBack(); }
    private void Reload_Click(object sender, RoutedEventArgs e) { if (_ready) Web.CoreWebView2.Reload(); }
    private void Home_Click(object sender, RoutedEventArgs e) { if (_homeUrl != null) Load(_homeUrl); }
    private void Search_Click(object sender, RoutedEventArgs e) { if (_searchUrl != null) Load(_searchUrl); }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    // ---------- Moving ----------

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_px == int.MinValue) return;
        OverlayNative.GetCursorPos(out _dragStart);
        _dragWinX = _px;
        _dragWinY = _py;
        _drag = true;
        Header.CaptureMouse();
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drag) return;
        OverlayNative.GetCursorPos(out var now);
        var x = Math.Clamp(_dragWinX + now.X - _dragStart.X, _gx, Math.Max(_gx, _gx + _gw - _pw));
        var y = Math.Clamp(_dragWinY + now.Y - _dragStart.Y, _gy, Math.Max(_gy, _gy + _gh - _ph));
        if (x == _px && y == _py) return;
        _px = x; _py = y;
        OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0,
            OverlayNative.SWP_NOSIZE | OverlayNative.SWP_NOZORDER | OverlayNative.SWP_NOACTIVATE);
    }

    private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        Header.ReleaseMouseCapture();
        var fx = Math.Clamp((_px - _gx) / (double)Math.Max(1, _gw - _pw), 0, 1);
        var fy = Math.Clamp((_py - _gy) / (double)Math.Max(1, _gh - _ph), 0, 1);
        Moved?.Invoke(fx, fy);
    }

    // ---------- Showing ----------

    /// <summary>Places the wiki over the game window at the saved spot. Returns true when it just appeared.</summary>
    public bool Present(IntPtr game, double fx, double fy)
    {
        if (_drag) return false;
        if (!OverlayNative.GameRect(game, out _gx, out _gy, out _gw, out _gh)) return false;

        _pw = Math.Max(Math.Min(_gw, 480), (int)(_gw * ShareW));
        _ph = Math.Max(Math.Min(_gh, 360), (int)(_gh * ShareH));
        var x = _gx + (int)Math.Round(Math.Clamp(fx, 0, 1) * Math.Max(0, _gw - _pw));
        var y = _gy + (int)Math.Round(Math.Clamp(fy, 0, 1) * Math.Max(0, _gh - _ph));

        var appeared = !IsVisible;
        if (appeared) Show();

        if (appeared || x != _px || y != _py || _pw != _lastW || _ph != _lastH)
        {
            _px = x; _py = y; _lastW = _pw; _lastH = _ph;
            OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle,
                appeared ? OverlayNative.HWND_TOPMOST : IntPtr.Zero, x, y, _pw, _ph,
                OverlayNative.SWP_NOACTIVATE | (appeared ? 0u : OverlayNative.SWP_NOZORDER));
        }
        return appeared;
    }

    /// <summary>Moves this window back above the other topmost windows (without taking the focus).</summary>
    public void BringToTop()
    {
        if (!IsVisible) return;
        OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle, OverlayNative.HWND_TOPMOST, 0, 0, 0, 0,
            OverlayNative.SWP_NOSIZE | OverlayNative.SWP_NOMOVE | OverlayNative.SWP_NOACTIVATE);
    }

    public void HideWiki()
    {
        if (!IsVisible) return;
        if (_drag) { _drag = false; Header.ReleaseMouseCapture(); }
        Hide();
        _px = int.MinValue;
    }
}
