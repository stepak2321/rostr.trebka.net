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
    // ---------- Settings ----------

    private void SetFps(int value)
    {
        value = Math.Clamp(value, 30, 1000);
        _syncing = true;
        FpsBox.Text = value.ToString();
        FpsSlider.Value = Math.Min(value, FpsSlider.Maximum);
        _syncing = false;
    }

    private int CurrentFps() =>
        int.TryParse(FpsBox.Text, out var v) ? Math.Clamp(v, 30, 1000) : (int)FpsSlider.Value;

    private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncing) return;
        SetFps((int)e.NewValue);
    }

    private void FpsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        if (int.TryParse(FpsBox.Text, out var v))
        {
            _syncing = true;
            FpsSlider.Value = Math.Clamp(v, FpsSlider.Minimum, FpsSlider.Maximum);
            _syncing = false;
        }
    }

    private void FpsBox_LostFocus(object sender, RoutedEventArgs e) => SetFps(CurrentFps());

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var fps = CurrentFps();
        SetFps(fps);
        S.FpsCap = fps;
        S.Save();

        var applied = GlobalSettings.TrySetFramerateCap(fps, out var note);
        SetStatus($"Saved: {fps} FPS. {note}", applied ? Ok : Bad);
    }

    private static void SetToggle(ToggleButton b, bool on) => b.IsChecked = on;


    // ---------- Appearance: language, theme, Discord ----------

    private void BuildAppearance()
    {
        LangPanel.Children.Clear();
        ThemePanel.Children.Clear();
        for (int i = 0; i < Loc.Codes.Length; i++)
            LangPanel.Children.Add(MakeChoice(Loc.Names[i], Loc.Codes[i], Loc.Codes[i] == Loc.Current, LangBtn_Click));
        foreach (var name in ThemeManager.Themes.Keys)
            ThemePanel.Children.Add(MakeChoice(name, name, name == ThemeManager.Current, ThemeBtn_Click));
    }

    private static Button MakeChoice(string label, string tag, bool on, RoutedEventHandler click)
    {
        var b = new Button { Content = label, Tag = tag, FontSize = 12, Padding = new Thickness(12, 7, 12, 7),
                             Margin = new Thickness(0, 0, 6, 6), Background = on ? Accent : Dim };
        b.Click += click;
        return b;
    }

    private void LangBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string code }) return;
        S.Language = code;
        S.Save();
        Loc.Set(code);
        Loc.Apply(this);
        foreach (var list in new ItemsControl[] { ServerList, FavList, RecentList, FriendsList, SearchResults })
            Loc.Apply(list, true);
        BuildAppearance();
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        S.Theme = name;
        S.Save();
        ThemeManager.Apply(name);
        // Colours that code sets by hand are refreshed here; everything else follows the theme by itself.
        UpdateSortButtons();
        ShowPage(_page);
        ApplyFilter();
        RefreshPresetCards();
        BuildAppearance();
    }

    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b) ShowSettingsTab(Convert.ToInt32(b.Tag));
    }

    private void ShowSettingsTab(int tab)
    {
        _settingsTab = tab;
        var panels = new (int Id, StackPanel Panel, Button Btn)[]
        {
            (0, SetPerfPanel, TabPerfBtn),
            (1, SetOverlayPanel, TabOverlayBtn),
            (2, SetGeneralPanel, TabGeneralBtn),
            (3, SetAboutPanel, TabAboutBtn),
            (4, SetAppearancePanel, TabAppearanceBtn),
            (5, SetDiscordPanel, TabDiscordBtn),
            (6, SetStoragePanel, TabStorageBtn),
        };
        foreach (var (id, panel, btn) in panels)
        {
            panel.Visibility = id == tab ? Visibility.Visible : Visibility.Collapsed;
            btn.Background = id == tab ? NavActive : Brushes.Transparent;
        }

        if (tab == 0) RefreshPresetCards();
        if (tab == 6) _ = RefreshDiskAsync();
    }

    private void UpdateSettingsUi()
    {
        SetToggle(OverlayToggle, S.OverlayEnabled);
        SetToggle(OverlayShowToggle, !S.OverlayHidden);
        SetToggle(FieldPingToggle, S.OverlayShowPing);
        SetToggle(FieldCpuToggle, S.OverlayShowCpu);
        SetToggle(FieldRamToggle, S.OverlayShowRam);
        SetToggle(FieldTimeToggle, S.OverlayShowTime);
        SetToggle(FieldRegionToggle, S.OverlayShowRegion);
        SetToggle(FieldUserToggle, S.OverlayShowUser);
        HotkeyBtn.Content = _capturingHotkey ? Loc.T("Press keys...") : HotkeyLabel;
        OverlayShowDesc.Text = Loc.T($"Hotkey: {HotkeyLabel}. Turns the overlay on screen off without closing it.");
        SetToggle(MultiToggle, S.MultiRoblox);
        SetToggle(MinimizeToggle, S.MinimizeOnLaunch);
        SetToggle(TrayToggle, S.CloseToTray);
        SetToggle(StartupToggle, S.RunAtStartup && StartupManager.IsEnabled);
        SetToggle(AutoBestToggle, S.AutoBestServer);
        SetToggle(CleanupToggle, S.AutoCleanup);
        SetToggle(DiscordToggle, S.DiscordEnabled);
        SetToggle(DiscordHideToggle, S.DiscordHideGame);

        CornerTL.Background = S.OverlayCorner == 0 ? Accent : Dim;
        CornerTR.Background = S.OverlayCorner == 1 ? Accent : Dim;
        CornerBL.Background = S.OverlayCorner == 2 ? Accent : Dim;
        CornerBR.Background = S.OverlayCorner == 3 ? Accent : Dim;
    }

    private void OverlayToggle_Click(object sender, RoutedEventArgs e)
    {
        S.OverlayEnabled = !S.OverlayEnabled;
        S.Save();
        UpdateSettingsUi();
        foreach (var session in _sessions)
        {
            if (!S.OverlayEnabled)
            {
                session.Overlay?.StopOverlay();
                session.Overlay?.Close();
                session.Overlay = null;
            }
            else if (session.Overlay == null)
            {
                AttachOverlay(session);
            }
        }
    }

    private void MinimizeToggle_Click(object sender, RoutedEventArgs e)
    {
        S.MinimizeOnLaunch = !S.MinimizeOnLaunch;
        S.Save();
        UpdateSettingsUi();
    }

    private void AutoBestToggle_Click(object sender, RoutedEventArgs e)
    {
        S.AutoBestServer = !S.AutoBestServer;
        S.Save();
        UpdateSettingsUi();
    }

    private void TrayToggle_Click(object sender, RoutedEventArgs e)
    {
        S.CloseToTray = !S.CloseToTray;
        S.Save();
        UpdateSettingsUi();
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        var turnOn = !(S.RunAtStartup && StartupManager.IsEnabled);
        try
        {
            if (turnOn) StartupManager.Enable(); else StartupManager.Disable();
            S.RunAtStartup = turnOn;
            S.Save();
            SetStatus(turnOn ? "Rostr will start with Windows." : "Rostr will no longer start with Windows.", Ok);
        }
        catch (Exception ex)
        {
            Log.Error("Start with Windows failed", ex);
            SetStatus("Could not change it: " + ex.Message, Bad);
        }
        UpdateSettingsUi();
    }

    private void CleanupToggle_Click(object sender, RoutedEventArgs e)
    {
        S.AutoCleanup = !S.AutoCleanup;
        S.Save();
        UpdateSettingsUi();
    }

    private void Corner_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        S.OverlayCorner = Convert.ToInt32(b.Tag);
        S.Save();
        UpdateSettingsUi();
        foreach (var session in _sessions) session.Overlay?.SetCorner(S.OverlayCorner);
        _previewOverlay?.SetCorner(S.OverlayCorner);
    }

    private void OverlayShowToggle_Click(object sender, RoutedEventArgs e) => ToggleOverlayHidden();

    private void MultiToggle_Click(object sender, RoutedEventArgs e)
    {
        S.MultiRoblox = !S.MultiRoblox;
        S.Save();
        UpdateSettingsUi();

        if (S.MultiRoblox)
        {
            _multi.Start();
            SetStatus("Multi Roblox is on. Clients you start now can run side by side.", Ok);
        }
        else
        {
            _multi.Stop();
            SetStatus("Multi Roblox is off.", Busy);
        }
    }

    private void OverlayPreview_Click(object sender, RoutedEventArgs e)
    {
        _previewOverlay ??= new OverlayWindow();
        _previewOverlay.SetCorner(S.OverlayCorner);
        _previewOverlay.SetAccount(null);
        ApplyOverlayFields(_previewOverlay);
        _previewOverlay.SetServer(null, "Preview");
        _previewOverlay.Preview(6);
    }


    // ---------- Register and unregister ----------

    private void UpdateRegisterText()
    {
        if (ProtocolHandler.IsRegistered())
        {
            RegisterState.Text = Loc.T("Status: registered. Play on roblox.com opens this launcher.");
            RegisterState.Foreground = Ok;
        }
        else if (ProtocolHandler.IsRegisteredElsewhere())
        {
            RegisterState.Text = Loc.T("Status: registered through an older setup or another copy. Press Register to point it at this exe.");
            RegisterState.Foreground = Busy;
        }
        else
        {
            RegisterState.Text = Loc.T("Status: not registered. Roblox's own launcher handles Play.");
            RegisterState.Foreground = Busy;
        }
    }

    private void RegisterBtn_Click(object sender, RoutedEventArgs e)
    {
        var cmd = ProtocolHandler.BuildCommand();
        if (cmd == null)
        {
            SetStatus("Registering needs the published Rostr.exe. Run build.bat and start it from dist.", Bad);
            return;
        }

        try
        {
            ProtocolHandler.Register(cmd);
            S.AutoRegister = true;
            S.Save();
            UpdateRegisterText();
            SetStatus("Registered. Play on roblox.com now opens this launcher.", Ok);
        }
        catch (Exception ex)
        {
            Log.Error("Register failed", ex);
            SetStatus("Could not register: " + ex.Message, Bad);
        }
    }

    private void UnregisterBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProtocolHandler.Unregister();
            S.AutoRegister = false;   // stay unregistered until the user presses Register again
            S.Save();
            UpdateRegisterText();
            SetStatus("Restored. Roblox's own launcher handles Play again.", Ok);
        }
        catch (Exception ex)
        {
            Log.Error("Unregister failed", ex);
            SetStatus("Could not restore: " + ex.Message, Bad);
        }
    }


    // ---------- Install: Start menu and desktop shortcuts ----------

    private void UpdateInstallText()
    {
        var running = Updater.CurrentVersion;
        var installedVer = Shortcuts.InstalledVersion();
        var installedText = (installedVer ?? running).ToString();
        var label = Loc.T("Install (Start menu + desktop)");
        var enable = true;

        if (Shortcuts.HasStartMenu || Shortcuts.HasDesktop)
        {
            if (Shortcuts.PointsHere())
            {
                if (installedVer != null && running > installedVer)
                {
                    InstallState.Text = Loc.T($"Status: installed, but the installed copy is version {installedVer} and this Rostr is version {running}. Press Update to install {running}.");
                    InstallState.Foreground = Busy;
                    label = Loc.T($"Update installed copy to {running}");
                }
                else if (Shortcuts.HasInstalledCopy)
                {
                    InstallState.Text = Loc.T($"Status: installed, version {installedText}. A copy of Rostr is kept in your Rostr data folder, so the shortcuts work even if you delete the downloaded file.");
                    InstallState.Foreground = Ok;
                    label = Loc.T($"Installed (version {installedText})");
                    enable = false;   // nothing to install: it is already there and up to date
                }
                else
                {
                    InstallState.Text = Loc.T("Status: installed. Search for Rostr in Windows, or use the desktop shortcut.");
                    InstallState.Foreground = Ok;
                }
            }
            else
            {
                InstallState.Text = Loc.T("Status: the shortcuts point to another copy of Rostr. Press Install to point them here.");
                InstallState.Foreground = Busy;
            }
        }
        else
        {
            InstallState.Text = Loc.T("Status: not installed. Rostr will not show up in Windows search yet.");
            InstallState.Foreground = Busy;
        }

        InstallBtn.Content = label;
        InstallBtn.IsEnabled = enable;
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Shortcuts.Install();                           // also copies Rostr.exe into the data folder
            if (S.RunAtStartup) StartupManager.Enable();   // can be turned off in Settings > General
            ProtocolHandler.EnsureRegistered();            // Play on roblox.com opens the installed copy
            UpdateRegisterText();
            UpdateInstallText();
            UpdateVersionText();
            UpdateSettingsUi();

            var msg = S.RunAtStartup
                ? Loc.T($"Installed version {S.InstalledVersion}. Rostr will also start with Windows (turn that off in Settings > General).")
                : Loc.T($"Installed version {S.InstalledVersion}. Search for Rostr in Windows, or use the shortcut on your desktop.");

            // Running from the downloaded exe: reload from the installed copy, so what you see and use from now on
            // is the installed version. Not while a game is being tracked.
            if (Shortcuts.HasInstalledCopy && !Shortcuts.RunningFromInstalledCopy() && !_sessions.Any())
            {
                SetStatus(msg + " " + Loc.T("Reloading from the installed copy..."), Ok);
                await Task.Delay(1200);
                if (Shortcuts.LaunchInstalledCopy())
                {
                    _reallyExit = true;
                    Application.Current.Shutdown();
                    return;
                }
            }
            SetStatus(msg, Ok);
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            SetStatus("Could not install: " + ex.Message, Bad);
        }
    }

    private void RemoveShortcutsBtn_Click(object sender, RoutedEventArgs e)
    {
        Shortcuts.Uninstall();
        StartupManager.Disable();
        ProtocolHandler.EnsureRegistered();   // points back at the exe that is running
        UpdateRegisterText();
        UpdateInstallText();
        UpdateVersionText();
        UpdateSettingsUi();
        SetStatus("Shortcuts removed. Rostr no longer starts with Windows.", Ok);
    }

    private void OpenLogBtn_Click(object sender, RoutedEventArgs e)
    {
        Log.Info("Log opened from Settings");   // makes sure the file exists
        OpenUrl(Log.FilePath);
    }


    // ---------- Client install and cleanup ----------

    private async Task InitClientAsync()
    {
        try
        {
            SetStatus("Checking for the latest version...", Busy);
            var info = await new VersionChecker().GetLatestAsync();

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Rostr", "Versions");
            var versionDir = Path.Combine(root, info.VersionGuid);

            var complete = File.Exists(Path.Combine(versionDir, "RobloxPlayerBeta.exe")) &&
                           File.Exists(Path.Combine(versionDir, "AppSettings.xml"));
            if (!complete)
            {
                var progress = new Progress<string>(s => SetStatus("Installing " + s, Busy));
                await new ClientInstaller().InstallAsync(info.VersionGuid, root, progress);
            }

            _versionsRoot = root;
            _currentGuid = info.VersionGuid;
            _versionDir = versionDir;
            _clientReady = true;
            UpdatePlayButtons();
            SetStatus($"Ready. Roblox {info.Version}", Ok);

            if (S.AutoCleanup) await CleanAsync(false);
            TryAutoLaunch();
        }
        catch (Exception ex)
        {
            Log.Error("Client install failed", ex);
            SetStatus("Error: " + ex.Message, Bad);
        }
    }

    private async Task CleanAsync(bool manual)
    {
        if (_versionsRoot == null || _currentGuid == null) return;
        var root = _versionsRoot;
        var guid = _currentGuid;

        var (removed, freed, skipped) = await Task.Run(() => VersionCleaner.Clean(root, guid));

        if (skipped)
        {
            if (manual) SetStatus("Close Roblox first, then clean.", Bad);
        }
        else if (removed > 0)
        {
            SetStatus($"Removed {removed} old version(s), freed {FormatBytes(freed)}.", Ok);
        }
        else if (manual)
        {
            SetStatus("Nothing to clean.", Ok);
        }
        await RefreshDiskAsync();
    }

    private async Task RefreshDiskAsync()
    {
        if (_versionsRoot == null) return;
        var root = _versionsRoot;
        var (count, bytes) = await Task.Run(() => VersionCleaner.Measure(root));
        DiskText.Text = Loc.T($"{count} client version(s) installed, {FormatBytes(bytes)}");
    }

    private static string FormatBytes(long b) =>
        b >= 1073741824 ? $"{b / 1073741824.0:0.0} GB" : $"{b / 1048576.0:0} MB";

    private async void CleanNowBtn_Click(object sender, RoutedEventArgs e) => await CleanAsync(true);


    // ---------- Updates ----------

    private UpdateInfo? _availableUpdate;
    private Version? _declinedVersion;       // a version you said "not now" to: the automatic check does not ask again
    private bool _updatePromptPending;       // found while Rostr was hidden in the tray
    private bool _updateBusy;

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdateAsync(manual: true);

    // Checks GitHub shortly after start and then every few hours (Rostr often lives in the tray for days).
    private async Task AutoUpdateLoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (true)
        {
            await CheckForUpdateAsync(manual: false);
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateBusy) return;
        if (!Updater.CanSelfUpdate())
        {
            if (manual) SetStatus("Updating only works from the published Rostr.exe, not a development build.", Busy);
            return;
        }

        _updateBusy = true;
        CheckUpdateBtn.IsEnabled = false;
        try
        {
            if (manual) SetStatus("Checking for updates...", Busy);
            var update = await Updater.CheckAsync();
            _availableUpdate = update;
            UpdateVersionText();

            if (update == null)
            {
                if (manual) SetStatus($"You have the newest version ({Updater.CurrentVersion}).", Ok);
                return;
            }

            // The automatic check does not nag about a version you already said no to.
            if (!manual && _declinedVersion == update.Version) return;

            if (!manual && !IsVisible)
            {
                // Hidden in the tray: do not pop a box over whatever you are doing. Ask when Rostr is opened.
                _updatePromptPending = true;
                _tray?.ShowBalloon("Rostr update", $"Version {update.Version} is available. Open Rostr to install it.");
                return;
            }

            AskToInstall(update);
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed: " + ex.Message);
            if (manual) SetStatus("Could not check for updates: " + ex.Message, Bad);
        }
        finally
        {
            _updateBusy = false;
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private void AskToInstall(UpdateInfo update)
    {
        _updatePromptPending = false;

        if (_sessions.Any())
        {
            SetStatus($"Version {update.Version} is available. Finish your game first, then press Check for updates.", Busy);
            return;
        }

        BringToFront();
        var dialog = new UpdatePromptWindow(this, update, Updater.CurrentVersion);
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case UpdateChoice.Install:
                StartUpdate(update);
                break;

            case UpdateChoice.Changelog:
                // The change log page has an Install button, so the automatic check does not ask again.
                _declinedVersion = update.Version;
                SetStatus($"Version {update.Version} is available. Press Check for updates when you want it.", Busy);
                OpenChangelog(update.Version);
                break;

            default:
                _declinedVersion = update.Version;
                SetStatus($"Version {update.Version} is available. Press Check for updates when you want it.", Busy);
                break;
        }
    }

    private void StartUpdate(UpdateInfo update)
    {
        if (_sessions.Any())
        {
            SetStatus($"Version {update.Version} is available. Finish your game first, then install it.", Busy);
            return;
        }

        try
        {
            Updater.StartUpdaterWindow(update);   // opens the CMD window, it waits for Rostr to close
            _reallyExit = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the update", ex);
            SetStatus("Could not start the update: " + ex.Message, Bad);
        }
    }
}
