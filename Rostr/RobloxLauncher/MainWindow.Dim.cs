using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RobloxLauncher.Core;
using RobloxLauncher.Settings;

namespace RobloxLauncher;

public partial class MainWindow : Window
{
    // ---------- Dim menu: darkens the whole Roblox window while you press the key, with friends and the wiki on top ----------
    //
    // The key is NOT a global hotkey. A global hotkey would take Ctrl + Tab away from every other app (browser tabs...).
    // Instead the keys are read while Roblox is the window in front, and nothing is touched anywhere else.

    private static class DimNative
    {
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    }

    // Left and right Shift, Ctrl, Alt and the two Windows keys.
    private static readonly uint[] DimModifierKeys = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };
    private static readonly List<uint> DimDefaultKeys = new() { 0xA2, 0x09 };   // Left Ctrl + Tab

    private readonly DispatcherTimer _dimTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private DimOverlayWindow? _dim;
    private DimControlsWindow? _dimControls;     // key hint and close button (right top corner)
    private DimDockWindow? _dimDock;             // buttons on the left side (friends, wiki)
    private DimWikiWindow? _dimWiki;             // the Fandom wiki of the game
    private bool _dimWikiOpen;                   // stays open while the dim is switched off and on again
    private bool _dimWikiBusy;
    private long _dimWikiPlace;                  // the game the wiki page belongs to
    private DimNotesWindow? _dimNotes;           // the note pad of the game
    private bool _dimNotesOpen;                  // stays open while the dim is switched off and on again
    private long _dimNotesPlace = -1;            // the game whose note is in the window now
    private IntPtr _dimGame;                     // the Roblox window the dim was last shown over
    private DimFriendsWindow? _dimFriends;       // friends who are playing, in the middle
    private List<FriendRow>? _dimRowsSource;     // the _friendRows list the panel shows
    private DateTime _dimFriendsPollAt = DateTime.MinValue;
    private bool _dimOn;              // switched on by the key; stays on while you look at other windows
    private bool _dimComboWasDown;
    private bool _capturingDimKey;
    private int _dimTick;
    private int _dimFgPid = -1;       // cache: is the window in front a Roblox client?
    private bool _dimFgIsRoblox;
    private int _dimPid;              // the client the dim was last shown over
    private int _dimStartPid;
    private DateTime _dimStartUtc;
    private int _dimSecond = -1;
    private string _dimClock = "", _dimPlaying = "";

    private void InitDim()
    {
        _dimTimer.Tick += (_, _) => DimTick();
        _dimTimer.Start();
        Closed += (_, _) =>
        {
            _dimTimer.Stop();
            try { _dim?.Close(); } catch { }
            try { _dimControls?.Close(); } catch { }
            try { _dimDock?.Close(); } catch { }
            try { _dimFriends?.Close(); } catch { }
            try { _dimWiki?.Close(); } catch { }
        };
    }

    private static string DimKeyName(uint vk) => vk switch
    {
        0xA0 => "Left Shift", 0xA1 => "Right Shift",
        0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl",
        0xA4 => "Left Alt", 0xA5 => "Right Alt",
        0x5B => "Left Win", 0x5C => "Right Win",
        _ => KeyName(vk)
    };

    private static string KeyName(uint vk)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)vk).ToString();
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key[1].ToString();
        return key;
    }

    private string DimKeyLabel
    {
        get
        {
            var keys = S.DimHotkey is { Count: > 0 } ? S.DimHotkey : DimDefaultKeys;
            return string.Join(" + ", keys.Select(DimKeyName));
        }
    }

    private bool DimComboDown()
    {
        var keys = S.DimHotkey;
        if (keys == null || keys.Count == 0) return false;

        foreach (var vk in keys)
            if ((DimNative.GetAsyncKeyState((int)vk) & 0x8000) == 0) return false;

        // Ctrl + Shift + Tab must not count as Left Ctrl + Tab: no other modifier may be held.
        foreach (var m in DimModifierKeys)
            if (!keys.Contains(m) && (DimNative.GetAsyncKeyState((int)m) & 0x8000) != 0) return false;

        return true;
    }

    private void DimTick()
    {
        if (!S.DimEnabled || _capturingDimKey)
        {
            _dimOn = false;
            _dimComboWasDown = false;
            HideDimAll();
            return;
        }

        // Which window is in front, and is it a Roblox client?
        var fg = DimNative.GetForegroundWindow();
        var pid = 0;
        if (fg != IntPtr.Zero)
        {
            DimNative.GetWindowThreadProcessId(fg, out var p);
            pid = (int)p;
        }
        if (pid != _dimFgPid)
        {
            _dimFgPid = pid;
            _dimFgIsRoblox = false;
            if (pid > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    _dimFgIsRoblox = proc.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }
        }
        var robloxFront = _dimFgIsRoblox && !DimNative.IsIconic(fg);
        if (robloxFront)
        {
            _dimPid = pid;
            _dimGame = fg;
        }
        else if (_dimOn && (_dimWiki is { IsVisible: true, IsActive: true } || _dimNotes is { IsVisible: true, IsActive: true }) &&
                 _dimGame != IntPtr.Zero && !DimNative.IsIconic(_dimGame))
        {
            // The wiki or the notes have the keyboard (to type in them). The game is still the window the dim belongs to.
            robloxFront = true;
            fg = _dimGame;
            pid = _dimPid;
        }

        // The key counts once per press, and only while Roblox is in front.
        var down = DimComboDown();
        if (down && !_dimComboWasDown && robloxFront) _dimOn = !_dimOn;
        _dimComboWasDown = down;

        // Once a second: if the client the dim belongs to is gone, the dim switches itself off.
        if (_dimOn && !robloxFront && ++_dimTick % 25 == 0)
        {
            try { using var alive = Process.GetProcessById(_dimPid); }
            catch { _dimOn = false; }
        }

        if (!_dimOn || !robloxFront)
        {
            HideDimAll();
            return;
        }

        UpdateDimTexts(pid);
        _dim ??= new DimOverlayWindow();
        var appeared = _dim.Present(fg, _dimClock, _dimPlaying);

        // Close key and close button in the right top corner.
        _dimControls ??= CreateDimControls();
        _dimControls.Present(fg, "Press " + DimKeyLabel + " to close");

        // The dock on the left side, with the friends and wiki buttons.
        _dimDock ??= CreateDimDock();
        _dimDock.Present(fg, S.DimFriendsOpen, _dimWikiOpen, _dimNotesOpen);

        // Friends who are playing, in the middle (or where it was moved to).
        if (S.DimFriendsOpen)
        {
            _dimFriends ??= CreateDimFriends();
            if (appeared) _dimFriendsPollAt = DateTime.MinValue;   // fresh list as soon as the dim opens
            UpdateDimFriends();
            _dimFriends.Present(fg, S.DimFriendsX, S.DimFriendsY);
        }
        else
        {
            _dimFriends?.HidePanel();
        }

        // The wiki browser.
        if (_dimWikiOpen)
        {
            _dimWiki ??= CreateDimWiki();
            _dimWiki.Present(fg, S.DimWikiX, S.DimWikiY);

            // The client is in another game than the page shows (for example one started from the Roblox app).
            var nowPlace = _sessions.FirstOrDefault(x => x.Pid == _dimPid)?.PlaceId ?? 0;
            if (nowPlace > 0 && _dimWiki.HasPage && _dimWikiPlace != nowPlace && !_dimWikiBusy)
                _ = OpenDimWikiAsync();
        }
        else
        {
            _dimWiki?.HideWiki();
        }

        // The notes of the game you are in.
        if (_dimNotesOpen)
        {
            _dimNotes ??= CreateDimNotes();
            var notePlace = DimSession()?.PlaceId ?? 0;
            if (notePlace != _dimNotesPlace)
            {
                _dimNotesPlace = notePlace;
                _dimNotes.SetNote(notePlace, S.GameNotes.TryGetValue(notePlace, out var note) ? note : "");
            }
            _dimNotes.SetTitle(notePlace > 0 ? GameNameFor(notePlace) : null);
            if (_dimNotes.Present(fg, S.DimNotesX, S.DimNotesY)) _dimNotes.BringToTop();
        }
        else if (_dimNotes != null)
        {
            // Closed with its X while it had the keyboard: give the keyboard back to the game.
            if (_dimNotes is { IsVisible: true, IsActive: true } && _dimGame != IntPtr.Zero)
                DimNative.SetForegroundWindow(_dimGame);
            _dimNotes.HideNotes();
        }

        if (appeared)
        {
            // The small stats box (ping, CPU...) must stay readable above the dim, and the dim's own
            // controls and panel go above that.
            foreach (var s in _sessions) s.Overlay?.BringToTop();
            _dimControls.BringToTop();
            _dimDock.BringToTop();
            _dimFriends?.BringToTop();
            _dimWiki?.BringToTop();
            _dimNotes?.BringToTop();
        }
    }

    private void HideDimAll()
    {
        // If the wiki had the keyboard, give it back to the game before the wiki goes away.
        if (_dimWiki is { IsVisible: true, IsActive: true } && _dimGame != IntPtr.Zero)
            DimNative.SetForegroundWindow(_dimGame);
        _dimWiki?.HideWiki();
        if (_dimNotes is { IsVisible: true, IsActive: true } && _dimGame != IntPtr.Zero)
            DimNative.SetForegroundWindow(_dimGame);
        _dimNotes?.HideNotes();
        _dim?.HideDim();
        _dimControls?.HideControls();
        _dimDock?.HideDock();
        _dimFriends?.HidePanel();
    }

    private DimControlsWindow CreateDimControls()
    {
        var w = new DimControlsWindow();
        w.CloseRequested += () => { _dimOn = false; HideDimAll(); };
        return w;
    }

    private DimDockWindow CreateDimDock()
    {
        var w = new DimDockWindow();
        w.FriendsClicked += () =>
        {
            S.DimFriendsOpen = !S.DimFriendsOpen;   // remembered, also after a restart
            S.Save();
            _dimFriendsPollAt = DateTime.MinValue;
        };
        w.WikiClicked += () =>
        {
            _dimWikiOpen = !_dimWikiOpen;
            if (_dimWikiOpen) _ = OpenDimWikiAsync();
        };
        w.NotesClicked += () => _dimNotesOpen = !_dimNotesOpen;
        w.HopClicked += () => DimQuickAction(true);
        w.RejoinClicked += () => DimQuickAction(false);
        w.CopyLinkClicked += DimCopyLink;
        return w;
    }

    // ---------- Quick actions of the dock (for the client the dim is over) ----------

    private Session? DimSession() => _sessions.FirstOrDefault(x => x.Pid == _dimPid);

    private static bool IsJobId(string? job) => !string.IsNullOrEmpty(job) && Regex.IsMatch(job, @"^[0-9a-fA-F\-]{8,64}$");

    // The server the client is in now, else the last one Rostr knows for this game.
    private string? DimJob(Session s) =>
        IsJobId(s.Job) ? s.Job : S.Recent.FirstOrDefault(r => r.PlaceId == s.PlaceId && IsJobId(r.LastJobId))?.LastJobId;

    // Server hop (hop = true) or Rejoin of the game you are in. The dim goes away, so it does not cover the new client.
    private void DimQuickAction(bool hop)
    {
        var s = DimSession();
        if (s == null || s.PlaceId <= 0) return;
        var place = s.PlaceId;

        var job = hop ? null : DimJob(s);
        if (!hop && job == null) return;

        if (ActiveAccount() == null) ShowFromTray();   // the "pick an account" message needs a visible window

        _dimOn = false;
        HideDimAll();

        if (hop) _ = ServerHopAsync(place, s);
        else _ = LaunchAsync(place, job);
    }

    // Puts the join link of your current server on the clipboard. False when there is no server to link to.
    private bool DimCopyLink()
    {
        var s = DimSession();
        if (s == null || s.PlaceId <= 0) return false;
        var job = DimJob(s);
        if (job == null) return false;
        try
        {
            System.Windows.Clipboard.SetText(ShareLink.Build(s.PlaceId, job));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Dim: could not copy the join link: " + ex.Message);
            return false;
        }
    }

    private DimNotesWindow CreateDimNotes()
    {
        var w = new DimNotesWindow();
        w.CloseRequested += () => _dimNotesOpen = false;
        w.Moved += (x, y) =>
        {
            S.DimNotesX = x;
            S.DimNotesY = y;
            S.Save();
        };
        w.Edited += (place, text) =>
        {
            if (string.IsNullOrWhiteSpace(text)) S.GameNotes.Remove(place);
            else S.GameNotes[place] = text;
            S.Save();
        };
        return w;
    }

    // The name of a game for the notes title: from Recent or Favorites, else the cached game info.
    private string? GameNameFor(long place)
    {
        var known = (GameEntry?)S.Recent.FirstOrDefault(r => r.PlaceId == place)
                    ?? S.Favorites.FirstOrDefault(f => f.PlaceId == place);
        if (!string.IsNullOrWhiteSpace(known?.Name)) return known!.Name;
        return _discordGames.TryGetValue(place, out var g) ? g.Name : null;
    }

    private DimWikiWindow CreateDimWiki()
    {
        var w = new DimWikiWindow();
        w.CloseRequested += () => _dimWikiOpen = false;
        w.Moved += (x, y) =>
        {
            S.DimWikiX = x;
            S.DimWikiY = y;
            S.Save();
        };
        return w;
    }

    // Finds the Fandom wiki of the game the dim is over and shows it. The page stays while you hop between
    // closing and opening the wiki, and is replaced when you are in another game.
    private async Task OpenDimWikiAsync()
    {
        if (_dimWikiBusy) return;
        _dimWikiBusy = true;
        try
        {
            _dimWiki ??= CreateDimWiki();
            var w = _dimWiki;
            var placeId = _sessions.FirstOrDefault(x => x.Pid == _dimPid)?.PlaceId ?? 0;
            if (w.HasPage && _dimWikiPlace == placeId) return;

            _dimWikiPlace = placeId;
            w.ShowStatus(Loc.T("Looking for the game wiki..."));

            string? name = null;
            if (placeId > 0)
            {
                var known = S.Recent.FirstOrDefault(r => r.PlaceId == placeId) ?? S.Favorites.FirstOrDefault(f => f.PlaceId == placeId);
                name = known?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    try { name = (await RobloxApi.GetGameAsync(placeId)).Name; }
                    catch { name = null; }
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                // The game is not known, so start at Fandom and let the person search.
                w.SetUrls(null, "https://www.fandom.com/");
                w.Load("https://www.fandom.com/");
                return;
            }

            var search = WikiFinder.SearchUrl(name);
            var url = await WikiFinder.FindAsync(name);
            w.SetUrls(url, search);
            w.Load(url ?? search);   // no wiki with that address: the Fandom search for the game
        }
        catch (Exception ex)
        {
            Log.Warn("Wiki: " + ex.Message);
            _dimWiki?.ShowStatus(Loc.T("Could not open the wiki.") + " " + ex.Message);
        }
        finally
        {
            _dimWikiBusy = false;
        }
    }

    private DimFriendsWindow CreateDimFriends()
    {
        var w = new DimFriendsWindow();
        w.CloseRequested += () =>
        {
            S.DimFriendsOpen = false;
            S.Save();
        };
        w.Moved += (x, y) =>
        {
            S.DimFriendsX = x;
            S.DimFriendsY = y;
            S.Save();
        };
        w.JoinRequested += row =>
        {
            if (ActiveAccount() == null)
            {
                SetStatus("Pick an account in the sidebar first.", Bad);
                return;
            }
            // The dim belongs to the client that is about to be replaced, so it switches off with it.
            _dimOn = false;
            HideDimAll();
            _ = LaunchFriendAsync(row);
        };
        return w;
    }

    // Keeps the friends list of the panel up to date (it uses the same data as the Friends page).
    private void UpdateDimFriends()
    {
        if (_dimFriends == null) return;

        if ((DateTime.Now - _dimFriendsPollAt).TotalSeconds >= 10)
        {
            _dimFriendsPollAt = DateTime.Now;
            _ = RefreshFriendsAsync(false);   // Roblox rate limits it, so it only reloads when the data is old enough
        }

        var acc = ActiveAccount();
        var loaded = acc != null && _friendsLoadedFor == acc.UserId;
        if (ReferenceEquals(_dimRowsSource, _friendRows) && loaded == _dimRowsLoaded) return;
        _dimRowsSource = _friendRows;
        _dimRowsLoaded = loaded;

        var playing = loaded
            ? _friendRows.Where(r => r.Order == 0)
                         .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<FriendRow>();
        var empty = acc == null ? "Pick an account in Rostr to see its friends."
                  : !loaded ? "Loading friends..."
                  : "None of your friends are playing right now.";
        _dimFriends.SetRows(playing, Loc.T(empty));
    }
    private bool _dimRowsLoaded;

    // The local time and how long this client has been playing. Worked out once a second.
    private void UpdateDimTexts(int pid)
    {
        var now = DateTime.Now;
        if (now.Second == _dimSecond && _dimStartPid == pid && _dimPlaying.Length > 0) return;
        _dimSecond = now.Second;

        if (_dimStartPid != pid)
        {
            _dimStartPid = pid;
            _dimStartUtc = DateTime.UtcNow;
            try { using var proc = Process.GetProcessById(pid); _dimStartUtc = proc.StartTime.ToUniversalTime(); }
            catch { }
        }

        // Playing counts from when the player got into the game, else from when the client started.
        var start = _sessions.FirstOrDefault(x => x.Pid == pid)?.JoinedUtc ?? _dimStartUtc;
        var played = DateTime.UtcNow - start;
        if (played < TimeSpan.Zero) played = TimeSpan.Zero;

        _dimClock = now.ToString("t", CultureInfo.CurrentCulture);
        _dimPlaying = "Playing for " + $"{(int)played.TotalHours}:{played.Minutes:00}:{played.Seconds:00}";
    }

    // ---------- Settings ----------

    private void UpdateDimUi()
    {
        SetToggle(DimToggle, S.DimEnabled);
        DimKeyBtn.Content = _capturingDimKey ? Loc.T("Press keys...") : DimKeyLabel;
    }

    private void DimToggle_Click(object sender, RoutedEventArgs e)
    {
        S.DimEnabled = !S.DimEnabled;
        if (!S.DimEnabled) _dimOn = false;
        S.Save();
        UpdateSettingsUi();
    }

    private void DimKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = false;
        _capturingMediaKey = null;
        _capturingDimKey = true;
        UpdateSettingsUi();
        DimKeyBtn.Focus();
    }

    private void DimKeyBtn_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_capturingDimKey) return;
        _capturingDimKey = false;
        UpdateSettingsUi();
    }

    private void DimKeyReset_Click(object sender, RoutedEventArgs e)
    {
        _capturingDimKey = false;
        S.DimHotkey = new List<uint>(DimDefaultKeys);
        S.Save();
        UpdateSettingsUi();
        SetStatus("Dim menu key is now " + DimKeyLabel + ".", Ok);
    }

    // Called from OnPreviewKeyDown while the key box waits for a new key.
    private void HandleDimCapture(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _capturingDimKey = false;
            UpdateSettingsUi();
            return;
        }

        // Wait until a real key comes with the modifiers.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None) return;

        // Left and right modifiers are kept apart, so "Left Ctrl + Tab" really means the left Ctrl.
        var keys = new List<uint>();
        foreach (var mod in new[] { Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift,
                                    Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin })
            if (Keyboard.IsKeyDown(mod)) keys.Add((uint)KeyInterop.VirtualKeyFromKey(mod));

        var isFKey = key >= Key.F1 && key <= Key.F24;
        if (keys.Count == 0 && !isFKey)
        {
            SetStatus("Hold Ctrl, Alt or Shift together with the key.", Busy);
            return;
        }

        keys.Add((uint)KeyInterop.VirtualKeyFromKey(key));
        S.DimHotkey = keys;
        S.Save();
        _capturingDimKey = false;
        UpdateSettingsUi();
        SetStatus("Dim menu key is now " + DimKeyLabel + ".", Ok);
    }
}
