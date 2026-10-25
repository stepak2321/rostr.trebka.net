using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobloxLauncher.Core;
using RobloxLauncher.Settings;

namespace RobloxLauncher;

public partial class MainWindow : Window
{
    private bool _ready;
    private bool _syncing;

    private LaunchLink? _link;
    private bool _linkUsed;
    private string? _versionDir;
    private string? _versionsRoot;
    private string? _currentGuid;
    private bool _clientReady;
    private int _loadId;
    private long _placeId;
    private bool _launching;

    private string _gameName = "";
    private string? _gameIcon;

    private bool _autoLaunchPending;
    private long _autoLaunchPlace;
    private string? _autoLaunchJob;
    private DateTime _autoLaunchUntil;

    private string _sort = "most";
    private bool SortAscending => _sort == "fewest";
    private List<ServerInfo> _servers = new();
    private List<ServerInfo> _shown = new();
    private string? _nextCursor;
    private bool _loadingMore;
    private int _serverReq;

    private bool _hasBaseline;
    private bool _baselineAsc;

    // Auto region detection: servers already probed this visit, and a pause after Roblox says slow down.
    private readonly HashSet<string> _regionTried = new();
    // Automatic detection runs once each time a game is opened. The Detect regions button is the manual retry.
    private bool _regionAutoDone;
    private DateTime _detectPausedUntil = DateTime.MinValue;

    private int _searchReq;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _playtimeTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private List<LibRow> _libRows = new();
    private int _settingsTab = 3;   // About is the first tab and opens first

    private bool _privateBusy;
    private int _friendsReq;
    private bool _friendsLoading;
    private bool _showOfflineFriends;
    private DateTime _friendsLoadedAt = DateTime.MinValue;
    private long _friendsLoadedFor;
    private List<FriendRow> _friendRows = new();

    // One session per running Roblox client (several can run with Multi Roblox).
    private class Session
    {
        public int Pid;
        public string AccountName = "";
        public long PlaceId;
        public string? Job;
        public string? Region;
        public string? RegionIp;
        public string? RelayIp;
        public LogWatcher Watcher = new();
        public OverlayWindow? Overlay;
        public DateTime? JoinedUtc;     // when the player got into a game (playtime counts from here)
        public long PlaytimePlace;      // the game the running playtime belongs to
        public DateTime? PresenceStartUtc;   // when this game was joined (for the Discord timer; never reset by the playtime flush)
        public long PresencePlace;
    }

    private readonly List<Session> _sessions = new();
    private readonly MultiInstance _multi = new();
    private readonly DiscordPresence _discord = new();
    private TrayIcon? _tray;
    private bool _startHidden;
    private bool _reallyExit;      // true when Exit was chosen, or Windows is shutting down
    private bool _trayHintShown;   // the "still running" balloon is shown once per run
    private readonly DateTime _launcherStartUtc = DateTime.UtcNow;   // timer on the idle Discord status
    private readonly Dictionary<long, GameInfo> _discordGames = new();
    private OverlayWindow? _previewOverlay;
    private CancellationTokenSource? _detectCts;

    private const int HotkeyId = 0x5242;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static AppSettings S => AppSettings.Current;

    private static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xF2, 0x5C, 0x5C));
    private static readonly Brush Busy = new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x3D));
    private static Brush Accent => ThemeManager.B("ThAccent");
    private static Brush Dim => ThemeManager.B("ThDim");
    private static Brush NavActive => ThemeManager.B("ThLine");
    private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42));
    private static Brush PingNone => ThemeManager.B("ThDisabled");
    private static Brush Muted => ThemeManager.B("ThMuted");
    private static Brush PresetOnCard => ThemeManager.B("ThAccentBg");
    private static Brush PresetOffCard => ThemeManager.B("ThBg2");
    private static Brush PresetOffEdge => ThemeManager.B("ThLine");

    // Ping colors in the server list: green is good, amber is playable, red is laggy.
    private const int PingGoodMax = 80;
    private const int PingMidMax = 150;

    public MainWindow()
    {
        InitializeComponent();

        ProtocolHandler.EnsureRegistered();   // no manual Register needed
        StartupManager.Refresh();             // keeps the start-with-Windows entry pointing at this exe
        S.LastRunVersion = Updater.CurrentVersion.ToString();
        S.Save();
        Shortcuts.RecordInstalledVersion();   // the saved installed version always matches the copy on disk

        // Started by Windows at logon: stay in the tray instead of opening the window.
        var args = Environment.GetCommandLineArgs();
        _startHidden = args.Contains(StartupManager.Arg) &&
                       !args.Any(a => a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase));
        if (_startHidden)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        SetFps(S.FpsCap);
        UpdateSortButtons();
        UpdateSettingsUi();
        UpdateRegisterText();
        UpdateInstallText();
        LoadBackground();
        ShowPage(0);
        ShowSettingsTab(3);
        BuildAppearance();
        Loc.Apply(this);
        _ready = true;

        OverlayWindow.Hidden = S.OverlayHidden;
        _multi.Status += msg => Dispatcher.InvokeAsync(() => SetStatus(msg, Ok));
        if (S.MultiRoblox) _multi.Start();
        UpdateAccountUi();

        FlagsBox.Text = FastFlagManager.Shared.ToJson();
        RefreshPresetCards();
        UpdateVersionText();

        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            var text = PlaceBox.Text.Trim();
            if (text.Length >= 2 && !LooksLikeId(text)) _ = SearchGamesAsync(text);
        };

        // Playtime is saved once a minute, so a crash or a killed launcher loses at most that.
        _playtimeTimer.Tick += (_, _) => TickPlaytime();
        _playtimeTimer.Start();

        _tray = new TrayIcon(BuildTrayMenu);
        _tray.OpenRequested += ShowFromTray;
        Application.Current.SessionEnding += (_, _) => _reallyExit = true;   // never block a Windows shutdown

        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _playtimeTimer.Stop();
            _multi.Dispose();
            foreach (var session in _sessions.ToList()) EndSession(session);
            _discord.Dispose();
            _previewOverlay?.Close();
            Application.Current.Shutdown();
        };

        var startLink = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(a => a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase));

        Loaded += async (_, _) =>
        {
            if (_startHidden)
            {
                Hide();
                WindowState = WindowState.Normal;   // so opening it from the tray shows it normally
                ShowInTaskbar = true;
            }
            if (startLink != null) HandleLink(startLink);
            _ = RefreshDiscordAsync();   // show the idle status right away
            _ = AutoUpdateLoopAsync();   // checks GitHub for a newer Rostr now and every few hours
            await InitClientAsync();

        };
    }

    // ---------- Global hotkey: hide or show the overlay ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (source == null) return;
        source.AddHook(WndProc);

        // 0x4000 = no key repeat. The keys come from Settings > Overlay.
        _hotkeyHwnd = source.Handle;
        if (!RegisterHotKey(source.Handle, HotkeyId, S.OverlayHotkeyMods | 0x4000, S.OverlayHotkeyVk))
            SetStatus(HotkeyText(S.OverlayHotkeyMods, S.OverlayHotkeyVk) +
                      " is used by another app, so the overlay hotkey is off.", Busy);
    }

    private IntPtr _hotkeyHwnd;
    private bool _capturingHotkey;

    private static string HotkeyText(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & 0x2) != 0) parts.Add("Ctrl");
        if ((mods & 0x1) != 0) parts.Add("Alt");
        if ((mods & 0x4) != 0) parts.Add("Shift");
        if ((mods & 0x8) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey((int)vk).ToString();
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key[1].ToString();
        parts.Add(key);
        return string.Join(" + ", parts);
    }

    private string HotkeyLabel => HotkeyText(S.OverlayHotkeyMods, S.OverlayHotkeyVk);

    // Moves the global hotkey. If Windows refuses the new keys (another app owns them), the old ones stay.
    private void ChangeHotkey(uint mods, uint vk)
    {
        if (_hotkeyHwnd == IntPtr.Zero) return;
        var oldMods = S.OverlayHotkeyMods;
        var oldVk = S.OverlayHotkeyVk;

        UnregisterHotKey(_hotkeyHwnd, HotkeyId);
        if (RegisterHotKey(_hotkeyHwnd, HotkeyId, mods | 0x4000, vk))
        {
            S.OverlayHotkeyMods = mods;
            S.OverlayHotkeyVk = vk;
            S.Save();
            SetStatus("Overlay hotkey is now " + HotkeyLabel + ".", Ok);
        }
        else
        {
            RegisterHotKey(_hotkeyHwnd, HotkeyId, oldMods | 0x4000, oldVk);
            SetStatus(HotkeyText(mods, vk) + " is used by another app. Kept " + HotkeyLabel + ".", Bad);
        }
        UpdateSettingsUi();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturingHotkey)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                _capturingHotkey = false;
                UpdateSettingsUi();
                return;
            }

            // Wait until a real key comes with the modifiers.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
                Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None) return;

            var mods = (uint)Keyboard.Modifiers;   // same bits as Win32: 1 Alt, 2 Ctrl, 4 Shift, 8 Win
            var isFKey = key >= Key.F1 && key <= Key.F24;
            if (mods == 0 && !isFKey)
            {
                SetStatus("Hold Ctrl, Alt or Shift together with the key.", Busy);
                return;
            }

            _capturingHotkey = false;
            ChangeHotkey(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void HotkeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        UpdateSettingsUi();
        HotkeyBtn.Focus();
    }

    private void HotkeyBtn_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_capturingHotkey) return;
        _capturingHotkey = false;
        UpdateSettingsUi();
    }

    private void HotkeyReset_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = false;
        ChangeHotkey(0x2 | 0x4, 0x4F);
    }


    // ---------- Which lines the overlay shows ----------

    private void ApplyOverlayFields(OverlayWindow? o) =>
        o?.SetFields(S.OverlayShowPing, S.OverlayShowCpu, S.OverlayShowRam,
                     S.OverlayShowTime, S.OverlayShowRegion, S.OverlayShowUser);

    private void OverlayField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton t) return;
        var on = t.IsChecked == true;
        switch (t.Tag as string)
        {
            case "ping": S.OverlayShowPing = on; break;
            case "cpu": S.OverlayShowCpu = on; break;
            case "ram": S.OverlayShowRam = on; break;
            case "time": S.OverlayShowTime = on; break;
            case "region": S.OverlayShowRegion = on; break;
            case "user": S.OverlayShowUser = on; break;
        }
        S.Save();
        foreach (var session in _sessions) ApplyOverlayFields(session.Overlay);
        ApplyOverlayFields(_previewOverlay);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            if (!_capturingHotkey) ToggleOverlayHidden();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlayHidden()
    {
        S.OverlayHidden = !S.OverlayHidden;
        OverlayWindow.Hidden = S.OverlayHidden;
        S.Save();
        UpdateSettingsUi();
        SetStatus(S.OverlayHidden ? $"Overlay hidden. {HotkeyLabel} shows it again." : "Overlay shown.", Ok);
    }


    // ---------- Messages from a second launch ----------

    public void HandleMessage(string msg)
    {
        if (msg.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
        {
            HandleLink(msg);
        }
        else if (msg.StartsWith("show:", StringComparison.OrdinalIgnoreCase) &&
                 Version.TryParse(msg[5..], out var newer) && newer > Updater.CurrentVersion &&
                 !_sessions.Any())
        {
            // A newer Rostr.exe was started while this older one runs in the tray: close, so the new one takes over.
            // Not while a game is being tracked, then this one just opens.
            Log.Info($"Closing {Updater.CurrentVersion} so {newer} can start");
            _reallyExit = true;
            Close();
            return;
        }
        BringToFront();
    }

    private void UpdateVersionText()
    {
        var text = $"Version {Updater.CurrentVersion}";
        if (Shortcuts.HasInstalledCopy && S.InstalledVersion.Length > 0)
            text += "\nInstalled copy: " + S.InstalledVersion;
        if (!string.IsNullOrEmpty(Environment.ProcessPath))
            text += "\nRunning from: " + Environment.ProcessPath;
        if (_availableUpdate != null)
            text += "\nNew version available: " + _availableUpdate.Version;
        VersionText.Text = text;
    }

    private void ShowFromTray() => BringToFront();

    // The tray menu is rebuilt each time it opens, so the names and check marks are always current.
    private IEnumerable<TrayEntry> BuildTrayMenu()
    {
        yield return new TrayEntry($"Rostr  {Updater.CurrentVersion}", Header: true);
        yield return new TrayEntry(Loc.T("Open Rostr"), ShowFromTray, Bold: true);

        var last = S.Recent.FirstOrDefault();
        if (last != null)
        {
            var name = string.IsNullOrWhiteSpace(last.Name) ? $"game {last.PlaceId}" : last.Name;
            if (name.Length > 26) name = name[..25] + "...";
            var place = last.PlaceId;
            yield return new TrayEntry(Loc.T("Play " + name), () =>
            {
                if (ActiveAccount() == null) ShowFromTray();   // the "pick an account" message needs a visible window
                _ = QuickRandomAsync(place);
            });

            var job = !string.IsNullOrEmpty(last.LastJobId) && Regex.IsMatch(last.LastJobId, @"^[0-9a-fA-F\-]{8,64}$")
                ? last.LastJobId : null;
            if (job != null)
                yield return new TrayEntry(Loc.T("Rejoin last server"), () =>
                {
                    if (ActiveAccount() == null) ShowFromTray();
                    else _ = LaunchAsync(place, job);
                });
        }

        yield return TrayEntry.Sep();
        yield return new TrayEntry(Loc.T("Show overlay"), ToggleOverlayHidden, Checked: !S.OverlayHidden);
        yield return new TrayEntry(Loc.T("Discord status"), () =>
        {
            S.DiscordEnabled = !S.DiscordEnabled;
            S.Save();
            UpdateSettingsUi();
            _ = RefreshDiscordAsync();
        }, Checked: S.DiscordEnabled);
        yield return new TrayEntry(Loc.T("Skip choosing a server"), () =>
        {
            S.AutoBestServer = !S.AutoBestServer;
            S.Save();
            UpdateSettingsUi();
        }, Checked: S.AutoBestServer);

        yield return TrayEntry.Sep();
        yield return new TrayEntry(Loc.T("Settings"), () => { ShowFromTray(); ShowPage(2); });
        yield return new TrayEntry(Loc.T("Exit"), () => { _reallyExit = true; Close(); });
    }

    // The X button hides Rostr in the tray (when the setting is on). Exit in the tray menu really closes it.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (S.CloseToTray && !_reallyExit)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray?.ShowBalloon("Rostr is still running", "Click the tray icon to open it. Right-click it to exit.");
            }
            return;
        }
        base.OnClosing(e);
    }

    private void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        // An update was found while Rostr sat hidden in the tray: ask now that the window is open.
        if (_updatePromptPending && _availableUpdate != null)
        {
            var pending = _availableUpdate;
            Dispatcher.BeginInvoke(new Action(() => AskToInstall(pending)));
        }
    }


    // ---------- Navigation ----------

    private int _page;

    private void ShowPage(int page)
    {
        _page = page;
        PlayPage.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        FriendsPage.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
        ReadmePage.Visibility = page == 4 ? Visibility.Visible : Visibility.Collapsed;
        ChangelogPage.Visibility = page == 5 ? Visibility.Visible : Visibility.Collapsed;
        NavPlayBtn.Background = page == 0 ? NavActive : Brushes.Transparent;
        NavLibraryBtn.Background = page == 1 ? NavActive : Brushes.Transparent;
        NavSettingsBtn.Background = page == 2 ? NavActive : Brushes.Transparent;
        NavFriendsBtn.Background = page == 3 ? NavActive : Brushes.Transparent;
        NavReadmeBtn.Background = page == 4 ? NavActive : Brushes.Transparent;

        if (page == 1) RefreshLibrary();
        if (page == 2) ShowSettingsTab(_settingsTab);
        if (page == 3) _ = RefreshFriendsAsync(false);
        if (page == 4) _ = LoadReadmeAsync(false);
        if (page == 5) _ = LoadChangelogAsync(false);
    }


    // ---------- Own title bar ----------

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var max = WindowState == WindowState.Maximized;
        MaxIcon.Text = max ? "\uE923" : "\uE922";
        // A maximized borderless window is drawn a few pixels bigger than the screen; pull it back in.
        WindowBorder.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        WindowBorder.BorderThickness = new Thickness(max ? 0 : 1);
    }

    private void NavFriends_Click(object sender, RoutedEventArgs e) => ShowPage(3);

    private void NavPlay_Click(object sender, RoutedEventArgs e) => ShowPage(0);
    private void NavLibrary_Click(object sender, RoutedEventArgs e) => ShowPage(1);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage(2);
    private void NavReadme_Click(object sender, RoutedEventArgs e) => ShowPage(4);
    private void NavWeb_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordPresence.SiteUrl);

    private void OpenRobloxBtn_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://www.roblox.com/home");

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("Could not open the browser: " + ex.Message, Bad);
        }
    }

    private void LoadBackground()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Rostr");
            var file = new[] { "background.png", "background.jpg", "background.jpeg" }
                .Select(n => Path.Combine(dir, n))
                .FirstOrDefault(File.Exists);
            if (file == null) return;

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(file);
            bi.EndInit();
            BgImage.Source = bi;
        }
        catch { }
    }


    // ---------- Status ----------

    // Lists that build their rows from a template: translate the row texts once they are drawn.
    private static void TranslateList(ItemsControl list) =>
        list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Loc.Apply(list, true));

    private void SetStatus(string text, Brush dot)
    {
        if (ReferenceEquals(dot, Bad)) Log.Warn("Status: " + text);
        StatusText.Text = Loc.T(text);
        StatusDot.Fill = dot;
    }
}
