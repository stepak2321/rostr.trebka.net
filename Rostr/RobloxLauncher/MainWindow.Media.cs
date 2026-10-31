using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobloxLauncher.Core;
using RobloxLauncher.Settings;

namespace RobloxLauncher;

/// <summary>One screenshot or video tile on the Media page.</summary>
public class MediaRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string FullPath { get; init; } = "";
    public string Name { get; init; } = "";
    public string Sub { get; init; } = "";
    public bool IsVideo { get; init; }
    public string Glyph => IsVideo ? "\uE714" : "\uE722";

    private bool _favorite;
    public bool IsFavorite
    {
        get => _favorite;
        set
        {
            _favorite = value;
            Raise(nameof(IsFavorite));
            Raise(nameof(StarVisibility));
        }
    }
    public Visibility StarVisibility => _favorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlayVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

    private Brush _thumbBrush = Brushes.Transparent;
    public Brush ThumbBrush => _thumbBrush;

    private bool _hasThumb;
    public Visibility GlyphVisibility => _hasThumb ? Visibility.Collapsed : Visibility.Visible;

    public void SetThumb(ImageSource img)
    {
        var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        _thumbBrush = brush;
        _hasThumb = true;
        Raise(nameof(ThumbBrush));
        Raise(nameof(GlyphVisibility));
    }
}

public partial class MainWindow
{
    // ---------- Media: screenshots and recordings of Roblox ----------

    private const int ShotHotkeyId = 0x5243;   // default Ctrl + Shift + F10
    private const int RecHotkeyId = 0x5244;    // default Ctrl + Shift + F11
    private const int ReplayHotkeyId = 0x5245; // default Ctrl + Shift + F9, only registered while Instant replay is on
    private int ReplayLengthSeconds => S.ReplaySeconds is 15 or 30 or 60 or 120 ? S.ReplaySeconds : 30;

    private static readonly HashSet<string> ImageExts = new() { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private static readonly HashSet<string> VideoExts = new() { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".wmv" };
    private static readonly SemaphoreSlim ThumbGate = new(1, 1);

    private readonly RobloxWindowRecorder _recorder = new();

    private readonly RobloxWindowRecorder _replay = new();
    private readonly DispatcherTimer _replayTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private RecordOptions? _replayOpt;
    private DateTime _replayStartedUtc, _replayNextTry;
    private int _replaySession, _replayW, _replayH;
    private int _replayLength;
    private bool _replayBusy, _replaySaving;
    private readonly DispatcherTimer _recTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _viewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _recStartedUtc;
    private int _recMisses;
    private bool _recBusy;
    private string _mediaFilter = "all";
    private List<MediaRow> _mediaRows = new();
    private MediaRow? _viewing;
    private bool _videoPlaying;
    private bool _seekSync;

    public static readonly DependencyProperty MediaColumnsProperty =
        DependencyProperty.Register(nameof(MediaColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(3));

    public int MediaColumns
    {
        get => (int)GetValue(MediaColumnsProperty);
        set => SetValue(MediaColumnsProperty, value);
    }

    private void MediaScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        MediaColumns = Math.Max(1, (int)((e.NewSize.Width - 24) / 210));

    private string MediaFolderPath => string.IsNullOrWhiteSpace(S.MediaFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Rostr")
        : S.MediaFolder;

    private void InitMedia()
    {
        _recTimer.Tick += (_, _) => RecordTick();
        _viewTimer.Tick += (_, _) => ViewTick();
        _replayTimer.Tick += (_, _) => ReplayTick();
        CleanReplayFiles(true);   // leftovers of a Rostr that was killed
        _replayTimer.Start();
    }

    // Called when Rostr really closes: ends the background recording and removes its temp files.
    private void ShutdownMedia()
    {
        _replayTimer.Stop();
        _replay.Stop(3000);
        CleanReplayFiles(true);
    }

    private string ShotLabel => HotkeyText(S.ShotHotkeyMods, S.ShotHotkeyVk);
    private string RecLabel => HotkeyText(S.RecHotkeyMods, S.RecHotkeyVk);
    private string ReplayLabel => HotkeyText(S.ReplayHotkeyMods, S.ReplayHotkeyVk);

    // "shot" or "rec" while the Settings > Gallery key box waits for the new keys.
    private string? _capturingMediaKey;

    private void RegisterMediaHotkeys(IntPtr hwnd)
    {
        // 0x4000 = no key repeat. The keys come from Settings > Gallery.
        if (!RegisterHotKey(hwnd, ShotHotkeyId, S.ShotHotkeyMods | 0x4000, S.ShotHotkeyVk))
            Log.Warn(ShotLabel + " (screenshot) is used by another app");
        if (!RegisterHotKey(hwnd, RecHotkeyId, S.RecHotkeyMods | 0x4000, S.RecHotkeyVk))
            Log.Warn(RecLabel + " (recording) is used by another app");
        if (S.ReplayEnabled && !RegisterHotKey(hwnd, ReplayHotkeyId, S.ReplayHotkeyMods | 0x4000, S.ReplayHotkeyVk))
            Log.Warn(ReplayLabel + " (instant replay) is used by another app");
    }

    private (int Id, uint Mods, uint Vk) MediaKey(string which) => which switch
    {
        "shot" => (ShotHotkeyId, S.ShotHotkeyMods, S.ShotHotkeyVk),
        "replay" => (ReplayHotkeyId, S.ReplayHotkeyMods, S.ReplayHotkeyVk),
        _ => (RecHotkeyId, S.RecHotkeyMods, S.RecHotkeyVk),
    };

    private void StoreMediaKey(string which, uint mods, uint vk)
    {
        switch (which)
        {
            case "shot": S.ShotHotkeyMods = mods; S.ShotHotkeyVk = vk; break;
            case "replay": S.ReplayHotkeyMods = mods; S.ReplayHotkeyVk = vk; break;
            default: S.RecHotkeyMods = mods; S.RecHotkeyVk = vk; break;
        }
    }

    // Moves one of the two global hotkeys. If Windows refuses the new keys (another app owns them), the old ones stay.
    private void ChangeMediaHotkey(string which, uint mods, uint vk)
    {
        if (_hotkeyHwnd == IntPtr.Zero) return;
        var (id, oldMods, oldVk) = MediaKey(which);
        var name = which switch { "shot" => "Screenshot", "replay" => "Instant replay", _ => "Recording" };
        // The instant replay key is only registered with Windows while the feature is on.
        var live = which != "replay" || S.ReplayEnabled;

        if (live) UnregisterHotKey(_hotkeyHwnd, id);
        if (!live || RegisterHotKey(_hotkeyHwnd, id, mods | 0x4000, vk))
        {
            StoreMediaKey(which, mods, vk);
            S.Save();
            SetStatus(name + " hotkey is now " + HotkeyText(mods, vk) + ".", Ok);
        }
        else
        {
            RegisterHotKey(_hotkeyHwnd, id, oldMods | 0x4000, oldVk);
            SetStatus(HotkeyText(mods, vk) + " is used by another app. Kept " + HotkeyText(oldMods, oldVk) + ".", Bad);
        }
        UpdateSettingsUi();
    }

    private void MediaKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string which } b) return;
        _capturingHotkey = false;
        _capturingMediaKey = which;
        UpdateSettingsUi();
        b.Focus();
    }

    private void MediaKeyBtn_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturingMediaKey == null) return;
        _capturingMediaKey = null;
        UpdateSettingsUi();
    }

    private void MediaKeyReset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string which }) return;
        _capturingHotkey = false;
        _capturingMediaKey = null;
        ChangeMediaHotkey(which, 0x2 | 0x4, which switch { "shot" => 0x79u, "replay" => 0x78u, _ => 0x7Au });
    }

    // Key boxes and folder path in Settings > Gallery, and the hint on the Gallery page.
    private void UpdateMediaKeysUi()
    {
        ShotKeyBtn.Content = _capturingMediaKey == "shot" ? Loc.T("Press keys...") : ShotLabel;
        RecKeyBtn.Content = _capturingMediaKey == "rec" ? Loc.T("Press keys...") : RecLabel;
        ReplayKeyBtn.Content = _capturingMediaKey == "replay" ? Loc.T("Press keys...") : ReplayLabel;
        MediaFolderText.Text = MediaFolderPath;
        MediaHotkeyHint.Text = Loc.T($"Hotkeys: {ShotLabel} takes a screenshot, {RecLabel} starts and stops a recording. " +
                                     "They work while you are in the game. Change them in Settings > Gallery.") +
                               (S.ReplayEnabled ? " " + Loc.T($"{ReplayLabel} saves the last {ReplayLengthSeconds} seconds.") : "");
        UpdateRecordingUi();
        UpdateReplayIndicator();
    }

    // The Recording and Instant replay cards in Settings > Gallery, and the Save replay button on the Gallery page.
    private void UpdateRecordingUi()
    {
        RecFps30Btn.Background = S.RecFps != 60 ? Accent : Dim;
        RecFps60Btn.Background = S.RecFps == 60 ? Accent : Dim;
        RecQLowBtn.Background = S.RecQuality == 0 ? Accent : Dim;
        RecQMedBtn.Background = S.RecQuality is not (0 or 2) ? Accent : Dim;
        RecQHighBtn.Background = S.RecQuality == 2 ? Accent : Dim;
        SetToggle(RecAudioToggle, S.RecAudio);
        SetToggle(ReplayToggle, S.ReplayEnabled);
        MediaReplayBtn.Visibility = S.ReplayEnabled ? Visibility.Visible : Visibility.Collapsed;
        Replay15Btn.Background = ReplayLengthSeconds == 15 ? Accent : Dim;
        Replay30Btn.Background = ReplayLengthSeconds == 30 ? Accent : Dim;
        Replay60Btn.Background = ReplayLengthSeconds == 60 ? Accent : Dim;
        Replay120Btn.Background = ReplayLengthSeconds == 120 ? Accent : Dim;
        ReplayLengthTitle.Text = Loc.T($"Keep the last {ReplayLengthSeconds} seconds");
        ReplayDescription.Text = Loc.T($"While Roblox is open, Rostr records in the background. Press the hotkey to save the last {ReplayLengthSeconds} seconds as a clip. This uses some CPU and a little disk space, and it pauses while you record a video yourself.");
    }

    private void RecFps_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string t }) return;
        S.RecFps = t == "60" ? 60 : 30;
        S.Save();
        UpdateRecordingUi();
    }

    private void RecQuality_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string t }) return;
        S.RecQuality = t == "0" ? 0 : t == "2" ? 2 : 1;
        S.Save();
        UpdateRecordingUi();
    }

    private void RecAudioToggle_Click(object sender, RoutedEventArgs e)
    {
        S.RecAudio = !S.RecAudio;
        S.Save();
        UpdateRecordingUi();
    }

    private void ReplayLength_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } ||
            !int.TryParse(value, out var seconds) ||
            seconds is not (15 or 30 or 60 or 120)) return;

        S.ReplaySeconds = seconds;
        S.Save();
        UpdateMediaKeysUi();
        ReplayTick();
    }

    private RecordOptions BuildRecordOptions(bool segments) =>
        new(S.RecFps == 60 ? 60 : 30, RecordOptions.CrfFor(S.RecQuality), S.RecAudio, segments);

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length > 40 ? name[..40].TrimEnd() : name;
    }

    // "Game name 2026-10-26 18-30-12": the game that is running, or just Roblox.
    private string NewMediaName()
    {
        var place = _sessions.LastOrDefault(s => s.PlaceId != 0)?.PlaceId ?? 0;
        var game = place != 0 ? S.Recent.FirstOrDefault(r => r.PlaceId == place)?.Name : null;
        var label = string.IsNullOrWhiteSpace(game) ? "Roblox" : SafeFileName(game!);
        if (label.Length == 0) label = "Roblox";
        return $"{label} {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
    }

    private static string UniqueFile(string dir, string baseName, string ext)
    {
        var file = Path.Combine(dir, baseName + ext);
        for (int i = 2; File.Exists(file); i++) file = Path.Combine(dir, $"{baseName} ({i}){ext}");
        return file;
    }

    private bool EnsureMediaFolder(out string dir)
    {
        dir = MediaFolderPath;
        try
        {
            Directory.CreateDirectory(dir);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Media folder", ex);
            SetStatus("Could not use the media folder: " + ex.Message, Bad);
            return false;
        }
    }

    // Status line, plus a tray notification when Rostr is not the window in front (hotkey used inside the game).
    private void MediaNotice(string text, Brush dot)
    {
        SetStatus(text, dot);
        if (!IsActive) _tray?.ShowBalloon("Rostr", Loc.T(text));
    }

    private bool Ask(string text, string title) =>
        (IsVisible
            ? MessageBox.Show(this, Loc.T(text), Loc.T(title), MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(Loc.T(text), Loc.T(title), MessageBoxButton.YesNo, MessageBoxImage.Question))
        == MessageBoxResult.Yes;


    // ---------- Screenshot ----------

    private void MediaShot_Click(object sender, RoutedEventArgs e) => TakeScreenshot(false);

    private async void TakeScreenshot(bool fromHotkey)
    {
        try
        {
            var hwnd = MediaCapture.FindRobloxWindow();
            if (hwnd == IntPtr.Zero)
            {
                MediaNotice("Roblox is not running, so there is nothing to capture.", Bad);
                return;
            }
            if (!EnsureMediaFolder(out var dir)) return;

            // Pressed inside Rostr, Rostr itself is in front: copying pixels off the screen would capture Rostr.
            var allowScreenCopy = fromHotkey || !IsActive;
            var file = UniqueFile(dir, NewMediaName(), ".png");

            var saved = await Task.Run(() =>
            {
                using var bmp = MediaCapture.Capture(hwnd, allowScreenCopy);
                if (bmp == null) return false;
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                return true;
            });

            if (!saved)
            {
                MediaNotice(allowScreenCopy
                    ? "Could not capture the Roblox window. Is it minimized?"
                    : $"Roblox's picture came out black. Press {ShotLabel} while Roblox is in front instead.", Bad);
                return;
            }
            MediaNotice("Screenshot saved: " + Path.GetFileName(file), Ok);
            if (_page == 6) RefreshMedia();
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot", ex);
            SetStatus("Screenshot failed: " + ex.Message, Bad);
        }
    }


    // ---------- Recording ----------

    private void MediaRec_Click(object sender, RoutedEventArgs e) => ToggleRecording(false);

    private async void ToggleRecording(bool fromHotkey)
    {
        if (_recBusy) return;
        _recBusy = true;
        try
        {
            if (_recorder.IsRecording)
            {
                await StopRecordingAsync();
                return;
            }

            var hwnd = MediaCapture.FindRobloxWindow();
            if (hwnd == IntPtr.Zero)
            {
                MediaNotice("Roblox is not running, so there is nothing to record.", Bad);
                return;
            }
            if (!await EnsureFfmpegAsync()) return;
            var ffmpeg = Ffmpeg.Find();
            if (ffmpeg == null || !EnsureMediaFolder(out var dir)) return;

            // Only one capture runs at a time: the instant replay buffer pauses and comes back after the recording.
            await _replay.StopAsync();

            var opt = BuildRecordOptions(false);
            var file = UniqueFile(dir, NewMediaName(), ".mp4");
            var (started, error) = await Task.Run(() =>
            {
                var ok = _recorder.Start(ffmpeg, hwnd, file, opt, out var err);
                return (ok, err);
            });
            if (!started)
            {
                MediaNotice("Could not start recording: " + error, Bad);
                return;
            }

            _recStartedUtc = DateTime.UtcNow;
            _recMisses = 0;
            _recTimer.Start();
            UpdateRecordUi();
            MediaNotice("Recording started. Press " + RecLabel + " to stop.", Busy);
        }
        catch (Exception ex)
        {
            Log.Error("Recording", ex);
            SetStatus("Recording failed: " + ex.Message, Bad);
        }
        finally { _recBusy = false; }
    }

    private async Task StopRecordingAsync()
    {
        _recTimer.Stop();
        SetStatus("Saving the recording...", Busy);
        var file = await _recorder.StopAsync();
        UpdateRecordUi();

        if (file == null)
        {
            MediaNotice("The recording could not be saved. The log file has details.", Bad);
            return;
        }
        MediaNotice("Recording saved: " + Path.GetFileName(file), Ok);
        if (_page == 6) RefreshMedia();
    }

    private async Task<bool> EnsureFfmpegAsync()
    {
        if (Ffmpeg.Find() != null) return true;

        if (!Ask("Recording videos needs ffmpeg, a free tool (about 80 MB). Download it now?\n\n" +
                 "It is saved inside Rostr's data folder and only used for recording.", "Download ffmpeg"))
            return false;

        try
        {
            await Ffmpeg.DownloadAsync(new Progress<string>(m => SetStatus(m, Busy)));
            SetStatus("ffmpeg is ready. Press Record again to start.", Ok);
        }
        catch (Exception ex)
        {
            Log.Error("ffmpeg download", ex);
            SetStatus("Could not download ffmpeg: " + ex.Message, Bad);
        }
        return false;   // the download took a while: the player should choose a moment to start recording
    }

    private void RecordTick()
    {
        UpdateRecordUi();

        // Roblox was closed: finish the file instead of recording the desktop.
        if (MediaCapture.FindRobloxWindow() == IntPtr.Zero) _recMisses++; else _recMisses = 0;
        if ((_recMisses >= 3 || !_recorder.IsRecording) && !_recBusy)
            _ = StopFromTimerAsync();
    }

    private async Task StopFromTimerAsync()
    {
        _recBusy = true;
        try { await StopRecordingAsync(); }
        finally { _recBusy = false; }
    }

    private void UpdateRecordUi()
    {
        var rec = _recorder.IsRecording;
        MediaRecGlyph.Text = rec ? "\uE71A" : "\uE714";
        MediaRecText.Text = rec
            ? Loc.T("Stop recording") + "  " + FormatTime(DateTime.UtcNow - _recStartedUtc)
            : Loc.T("Record");
        if (rec) MediaRecBtn.Background = Bad;
        else MediaRecBtn.ClearValue(Button.BackgroundProperty);
        UpdateReplayIndicator();
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");


    // ---------- Instant replay ----------

    private static string ReplayDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rostr", "replay");

    private void MediaReplay_Click(object sender, RoutedEventArgs e) => SaveReplay(false);

    private async void ReplayToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!S.ReplayEnabled)
            {
                if (Ffmpeg.Find() == null)
                {
                    await EnsureFfmpegAsync();   // asks, and downloads when the player agrees
                    if (Ffmpeg.Find() == null) { UpdateRecordingUi(); return; }   // declined or failed: stays off
                }

                S.ReplayEnabled = true;
                S.Save();
                UpdateMediaKeysUi();
                if (_hotkeyHwnd != IntPtr.Zero &&
                    !RegisterHotKey(_hotkeyHwnd, ReplayHotkeyId, S.ReplayHotkeyMods | 0x4000, S.ReplayHotkeyVk))
                    Log.Warn(ReplayLabel + " (instant replay) is used by another app");
                SetStatus(Loc.T($"Instant replay is on. It records while Roblox is open. Press {ReplayLabel} to save the last {ReplayLengthSeconds} seconds."), Ok);
                ReplayTick();
            }
            else
            {
                S.ReplayEnabled = false;
                S.Save();
                UpdateMediaKeysUi();
                if (_hotkeyHwnd != IntPtr.Zero) UnregisterHotKey(_hotkeyHwnd, ReplayHotkeyId);
                await _replay.StopAsync();
                CleanReplayFiles(true);
                SetStatus("Instant replay is off.", Ok);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Instant replay switch", ex);
            SetStatus("Instant replay failed: " + ex.Message, Bad);
        }
        UpdateSettingsUi();
    }

    // Every few seconds: keeps the background recording in step with Roblox (start when its window is there,
    // stop when it closes or is minimized, restart when it was resized or the settings changed), and trims old segments.
    private async void ReplayTick()
    {
        if (_replayBusy) return;
        _replayBusy = true;
        try
        {
            if (!S.ReplayEnabled)
            {
                await _replay.StopAsync();
                return;
            }

            CleanReplayFiles(false);

            // One capture at a time: a video the player records themselves wins.
            if (_recorder.IsRecording || _recBusy)
            {
                await _replay.StopAsync();
                return;
            }

            int x = 0, y = 0, w = 0, h = 0;
            var hwnd = MediaCapture.FindRobloxWindow();
            var visible = hwnd != IntPtr.Zero && MediaCapture.TryGetClientRect(hwnd, out x, out y, out w, out h);
            if (!visible)
            {
                await _replay.StopAsync();
                return;
            }

            var opt = BuildRecordOptions(true);
            if (_replay.IsRecording && opt == _replayOpt && ReplayLengthSeconds == _replayLength &&
                (w & ~1) == _replayW && (h & ~1) == _replayH) return;

            // ffmpeg stopped by itself soon after it started: wait before trying again instead of looping.
            if (_replayOpt != null && !_replay.IsRecording && (DateTime.UtcNow - _replayStartedUtc).TotalSeconds < 15)
                _replayNextTry = DateTime.UtcNow.AddSeconds(30);

            await _replay.StopAsync();   // also lets go of a finished or resized capture
            _replayOpt = null;
            if (DateTime.UtcNow < _replayNextTry) return;

            var ffmpeg = Ffmpeg.Find();
            if (ffmpeg == null) return;

            Directory.CreateDirectory(ReplayDir);
            var session = ++_replaySession;
            var pattern = Path.Combine(ReplayDir, $"s{session}_%05d.ts");
            var (ok, error) = await Task.Run(() =>
            {
                var started = _replay.Start(ffmpeg, hwnd, pattern, opt, out var err);
                return (started, err);
            });

            if (ok)
            {
                _replayOpt = opt;
                _replayLength = ReplayLengthSeconds;
                _replayW = w & ~1;
                _replayH = h & ~1;
                _replayStartedUtc = DateTime.UtcNow;
            }
            else
            {
                Log.Warn("Instant replay could not start: " + error);
                _replayNextTry = DateTime.UtcNow.AddSeconds(30);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Instant replay", ex);
        }
        finally
        {
            _replayBusy = false;
            UpdateReplayIndicator();
        }
    }

    private void UpdateReplayIndicator()
    {
        if (ReplayStatusPanel == null) return;
        ReplayStatusPanel.Visibility = S.ReplayEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!S.ReplayEnabled) return;

        if (_recorder.IsRecording || _recBusy)
        {
            ReplayStatusText.Text = Loc.T("Replay paused while recording");
            ReplayStatusDot.Fill = Busy;
        }
        else if (_replay.IsRecording)
        {
            ReplayStatusText.Text = Loc.T("Replay buffer active");
            ReplayStatusDot.Fill = Ok;
        }
        else
        {
            ReplayStatusText.Text = Loc.T(MediaCapture.FindRobloxWindow() == IntPtr.Zero
                ? "Waiting for Roblox window"
                : "Starting replay buffer");
            ReplayStatusDot.Fill = Busy;
        }
    }

    // The newest segments of the running capture, oldest first. The last file is still being written, so it is left out.
    private List<string> ReplaySegments()
    {
        var prefix = $"s{_replaySession}_";
        var files = new DirectoryInfo(ReplayDir).EnumerateFiles(prefix + "*.ts")
            .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name).ToList();
        if (files.Count > 0 && _replay.IsRecording) files.RemoveAt(files.Count - 1);
        return files.TakeLast(ReplayLengthSeconds).Select(f => f.Name).ToList();
    }

    private void CleanReplayFiles(bool all)
    {
        if (_replaySaving) return;
        try
        {
            var dir = new DirectoryInfo(ReplayDir);
            if (!dir.Exists) return;
            var limit = DateTime.UtcNow.AddSeconds(-(ReplayLengthSeconds + 15));
            foreach (var f in dir.EnumerateFiles())
            {
                try { if (all || f.LastWriteTimeUtc < limit) f.Delete(); }
                catch { }   // the segment ffmpeg is writing right now cannot be deleted, which is fine
            }
        }
        catch (Exception ex) { Log.Warn("Instant replay folder: " + ex.Message); }
    }

    private async void SaveReplay(bool fromHotkey)
    {
        if (_replaySaving) return;
        if (!S.ReplayEnabled)
        {
            MediaNotice("Instant replay is off. Turn it on in Settings > Gallery.", Busy);
            return;
        }
        if (_recorder.IsRecording)
        {
            MediaNotice("Instant replay is paused while you record.", Busy);
            return;
        }
        if (!_replay.IsRecording)
        {
            MediaNotice("Instant replay is not running. It records while Roblox is open.", Bad);
            return;
        }

        _replaySaving = true;
        try
        {
            var ffmpeg = Ffmpeg.Find();
            if (ffmpeg == null || !EnsureMediaFolder(out var dir)) return;

            var names = ReplaySegments();
            if (names.Count < 2)
            {
                MediaNotice("Not enough footage yet. Wait a few seconds and try again.", Busy);
                return;
            }

            var file = UniqueFile(dir, NewMediaName() + " replay", ".mp4");
            var saved = await Task.Run(() => Ffmpeg.Concat(ffmpeg, ReplayDir, names, file));
            if (!saved)
            {
                MediaNotice("Could not save the instant replay. The log file has details.", Bad);
                return;
            }

            MediaNotice("Instant replay saved: " + Path.GetFileName(file), Ok);
            if (_page == 6) RefreshMedia();
        }
        catch (Exception ex)
        {
            Log.Error("Save instant replay", ex);
            SetStatus("Could not save the instant replay: " + ex.Message, Bad);
        }
        finally { _replaySaving = false; }
    }


    // ---------- The list ----------

    private static string FormatSize(long bytes) =>
        bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:0.#} MB" : $"{Math.Max(1, bytes / 1000)} KB";

    private void RefreshMedia()
    {
        var dir = MediaFolderPath;
        MediaFolderText.Text = dir;
        UpdateMediaKeysUi();

        try
        {
            _mediaRows = !Directory.Exists(dir)
                ? new()
                : new DirectoryInfo(dir).EnumerateFiles()
                    .Where(f => ImageExts.Contains(f.Extension.ToLowerInvariant()) ||
                                VideoExts.Contains(f.Extension.ToLowerInvariant()))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => new MediaRow
                    {
                        FullPath = f.FullName,
                        Name = Path.GetFileNameWithoutExtension(f.Name),
                        Sub = f.LastWriteTime.ToString("g") + "  |  " + FormatSize(f.Length),
                        IsVideo = VideoExts.Contains(f.Extension.ToLowerInvariant()),
                        IsFavorite = IsFavoritePath(f.FullName),
                    })
                    .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn("Media list: " + ex.Message);
            _mediaRows = new();
        }

        UpdateMediaFilterUi();
        ApplyMediaFilter();
        UpdateRecordUi();
        foreach (var row in _mediaRows) _ = LoadMediaThumbAsync(row);
    }

    private void ApplyMediaFilter()
    {
        var shown = _mediaFilter switch
        {
            "favs" => _mediaRows.Where(r => r.IsFavorite).ToList(),
            "shots" => _mediaRows.Where(r => !r.IsVideo).ToList(),
            "videos" => _mediaRows.Where(r => r.IsVideo).ToList(),
            _ => _mediaRows,
        };
        MediaList.ItemsSource = shown;
        MediaCount.Text = shown.Count.ToString();
        MediaEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMediaFilterUi()
    {
        MediaAllBtn.Background = _mediaFilter == "all" ? NavActive : Brushes.Transparent;
        MediaFavsBtn.Background = _mediaFilter == "favs" ? NavActive : Brushes.Transparent;
        MediaShotsBtn.Background = _mediaFilter == "shots" ? NavActive : Brushes.Transparent;
        MediaVideosBtn.Background = _mediaFilter == "videos" ? NavActive : Brushes.Transparent;
    }

    private void MediaFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        _mediaFilter = b.Tag as string ?? "all";
        UpdateMediaFilterUi();
        ApplyMediaFilter();
    }

    private static BitmapImage? LoadImage(string path, int decodeWidth)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;   // the file is not kept open
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception ex)
        {
            Log.Warn("Media image: " + ex.Message);
            return null;
        }
    }

    private async Task LoadMediaThumbAsync(MediaRow row)
    {
        try
        {
            string? source = row.FullPath;
            if (row.IsVideo)
            {
                var thumbDir = Path.Combine(MediaFolderPath, ".thumbs");
                var thumb = Path.Combine(thumbDir, Path.GetFileNameWithoutExtension(row.FullPath) + ".jpg");
                if (!File.Exists(thumb) && Ffmpeg.Find() is { } ffmpeg)
                {
                    await ThumbGate.WaitAsync();
                    try
                    {
                        await Ffmpeg.MakeThumbAsync(ffmpeg, row.FullPath, thumb);
                        try { new DirectoryInfo(thumbDir).Attributes |= FileAttributes.Hidden; } catch { }
                    }
                    finally { ThumbGate.Release(); }
                }
                source = File.Exists(thumb) ? thumb : null;
            }
            if (source == null) return;

            var img = await Task.Run(() => LoadImage(source, 480));
            if (img != null) row.SetThumb(img);
        }
        catch (Exception ex)
        {
            Log.Warn("Media thumbnail: " + ex.Message);
        }
    }


    // ---------- Folder ----------

    private void MediaFolderChange_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Where should screenshots and recordings be saved?"),
            InitialDirectory = Directory.Exists(MediaFolderPath) ? MediaFolderPath : "",
        };
        if (dlg.ShowDialog(this) != true) return;

        S.MediaFolder = dlg.FolderName;
        S.Save();
        SetStatus("Media folder: " + dlg.FolderName, Ok);
        UpdateMediaKeysUi();
        if (_page == 6) RefreshMedia();
    }

    private void MediaFolderOpen_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMediaFolder(out var dir)) return;
        OpenUrl(dir);
    }


    // ---------- Viewer: click a tile to look at the picture or play the video here ----------

    private void MediaOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string path) return;
        var row = _mediaRows.FirstOrDefault(r => r.FullPath == path);
        if (row != null) OpenViewer(row);
    }

    private void OpenViewer(MediaRow row)
    {
        CloseViewer();
        _viewing = row;
        ViewTitle.Text = row.Name;
        UpdateViewerFavUi();
        MediaViewer.Visibility = Visibility.Visible;

        if (row.IsVideo)
        {
            ImageView.Visibility = Visibility.Collapsed;
            VideoView.Visibility = Visibility.Visible;
            VideoBar.Visibility = Visibility.Visible;
            SeekSlider.Value = 0;
            TimeText.Text = DurText.Text = "0:00";
            VideoView.Volume = VolSlider.Value;
            VideoView.Source = new Uri(row.FullPath);
            VideoView.Play();
            _videoPlaying = true;
            PlayGlyph.Text = "\uE769";
            _viewTimer.Start();
        }
        else
        {
            VideoView.Visibility = Visibility.Collapsed;
            VideoBar.Visibility = Visibility.Collapsed;
            ImageView.Visibility = Visibility.Visible;
            ImageView.Source = LoadImage(row.FullPath, 0);
        }
    }

    private void CloseViewer()
    {
        CancelRename();
        _viewTimer.Stop();
        try
        {
            VideoView.Stop();
            VideoView.Close();
            VideoView.Source = null;   // lets go of the file so it can be deleted
        }
        catch { }
        ImageView.Source = null;
        MediaViewer.Visibility = Visibility.Collapsed;
        _viewing = null;
        _videoPlaying = false;
    }

    private void ViewClose_Click(object sender, RoutedEventArgs e) => CloseViewer();

    private void ViewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewing == null) return;
        try { Process.Start("explorer.exe", $"/select,\"{_viewing.FullPath}\""); }
        catch (Exception ex) { SetStatus("Could not open the folder: " + ex.Message, Bad); }
    }

    private void ViewCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_viewing == null) return;
        try
        {
            if (_viewing.IsVideo)
            {
                var files = new System.Collections.Specialized.StringCollection { _viewing.FullPath };
                Clipboard.SetFileDropList(files);
            }
            else if (LoadImage(_viewing.FullPath, 0) is { } img)
            {
                Clipboard.SetImage(img);
            }
            SetStatus("Copied. You can paste it into Discord or a chat.", Ok);
        }
        catch (Exception ex)
        {
            SetStatus("Could not copy: " + ex.Message, Bad);
        }
    }

    private void ViewDelete_Click(object sender, RoutedEventArgs e)
    {
        var row = _viewing;
        if (row == null) return;
        if (!Ask($"Delete {row.Name}?\n\nThe file is removed from your disk.", "Delete")) return;

        CloseViewer();
        try
        {
            File.Delete(row.FullPath);
            if (IsFavoritePath(row.FullPath)) { SetFavoritePath(row.FullPath, false); S.Save(); }
            var thumb = Path.Combine(Path.GetDirectoryName(row.FullPath) ?? "", ".thumbs",
                Path.GetFileNameWithoutExtension(row.FullPath) + ".jpg");
            if (File.Exists(thumb)) File.Delete(thumb);
            SetStatus("Deleted " + row.Name, Ok);
        }
        catch (Exception ex)
        {
            SetStatus("Could not delete: " + ex.Message, Bad);
        }
        RefreshMedia();
    }


    // ----- favorites and rename (in the viewer) -----

    private bool IsFavoritePath(string path) =>
        S.MediaFavorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    private void SetFavoritePath(string path, bool favorite)
    {
        S.MediaFavorites.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (favorite) S.MediaFavorites.Add(path);
    }

    private void UpdateViewerFavUi()
    {
        var fav = _viewing?.IsFavorite == true;
        ViewFavGlyph.Text = fav ? "\uE735" : "\uE734";
        if (fav) ViewFavGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x3D));
        else ViewFavGlyph.ClearValue(TextBlock.ForegroundProperty);
    }

    private void ViewFav_Click(object sender, RoutedEventArgs e)
    {
        var row = _viewing;
        if (row == null) return;
        row.IsFavorite = !row.IsFavorite;
        SetFavoritePath(row.FullPath, row.IsFavorite);
        S.Save();
        UpdateViewerFavUi();
        if (_mediaFilter == "favs") ApplyMediaFilter();   // the tile is behind the viewer; keep the list right
    }

    private void ViewRename_Click(object sender, RoutedEventArgs e)
    {
        if (_viewing == null) return;
        if (ViewTitleBox.Visibility == Visibility.Visible) { CommitRename(); return; }
        ViewTitleBox.Text = _viewing.Name;
        ViewTitle.Visibility = Visibility.Collapsed;
        ViewTitleBox.Visibility = Visibility.Visible;
        ViewTitleBox.Focus();
        ViewTitleBox.SelectAll();
    }

    private void ViewTitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitRename();
    }

    // Clicking somewhere else finishes the edit too, like renaming a file in Explorer.
    private void ViewTitleBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitRename();

    private void CancelRename()
    {
        ViewTitleBox.Visibility = Visibility.Collapsed;
        ViewTitle.Visibility = Visibility.Visible;
    }

    private static string CleanClipName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.', ' ');
        return name.Length > 100 ? name[..100].TrimEnd() : name;
    }

    private void CommitRename()
    {
        if (ViewTitleBox.Visibility != Visibility.Visible) return;
        var row = _viewing;
        var name = CleanClipName(ViewTitleBox.Text);
        CancelRename();
        if (row == null || name.Length == 0 || name == row.Name) return;
        RenameClip(row, name);
    }

    private void RenameClip(MediaRow row, string newName)
    {
        var oldPath = row.FullPath;
        var dir = Path.GetDirectoryName(oldPath) ?? "";
        var ext = Path.GetExtension(oldPath);
        var newPath = Path.Combine(dir, newName + ext);

        var sameFile = string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(newPath))
        {
            SetStatus("A file with that name already exists.", Bad);
            return;
        }

        CloseViewer();   // a playing video keeps its file open, and an open file cannot be renamed
        var landed = oldPath;
        try
        {
            File.Move(oldPath, newPath);
            landed = newPath;

            var thumbDir = Path.Combine(dir, ".thumbs");
            var oldThumb = Path.Combine(thumbDir, Path.GetFileNameWithoutExtension(oldPath) + ".jpg");
            var newThumb = Path.Combine(thumbDir, newName + ".jpg");
            try
            {
                if (File.Exists(oldThumb))
                {
                    if (File.Exists(newThumb) && !sameFile) File.Delete(newThumb);
                    File.Move(oldThumb, newThumb);
                }
            }
            catch { }   // a thumbnail that is left behind is made again when needed

            if (IsFavoritePath(oldPath))
            {
                SetFavoritePath(oldPath, false);
                SetFavoritePath(newPath, true);
                S.Save();
            }
            SetStatus("Renamed to " + newName, Ok);
        }
        catch (Exception ex)
        {
            Log.Warn("Rename: " + ex.Message);
            SetStatus("Could not rename: " + ex.Message, Bad);
        }

        RefreshMedia();
        var again = _mediaRows.FirstOrDefault(r => string.Equals(r.FullPath, landed, StringComparison.OrdinalIgnoreCase));
        if (again != null) OpenViewer(again);
    }

    // ----- video controls -----

    private void TogglePlayback()
    {
        if (_viewing is not { IsVideo: true }) return;
        if (_videoPlaying) VideoView.Pause(); else VideoView.Play();
        _videoPlaying = !_videoPlaying;
        PlayGlyph.Text = _videoPlaying ? "\uE769" : "\uE768";
    }

    private void ViewPlay_Click(object sender, RoutedEventArgs e) => TogglePlayback();

    private void VideoView_Click(object sender, MouseButtonEventArgs e) => TogglePlayback();

    private void VideoView_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!VideoView.NaturalDuration.HasTimeSpan) return;
        var d = VideoView.NaturalDuration.TimeSpan;
        SeekSlider.Maximum = Math.Max(1, d.TotalSeconds);
        DurText.Text = FormatTime(d);
    }

    private void VideoView_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoView.Position = TimeSpan.Zero;
        VideoView.Pause();
        _videoPlaying = false;
        PlayGlyph.Text = "\uE768";
    }

    // Windows could not play it (a codec it does not have): hand it to the default player instead.
    private void VideoView_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        var path = _viewing?.FullPath;
        Log.Warn("Video could not play: " + e.ErrorException?.Message);
        CloseViewer();
        if (path == null) return;
        SetStatus("This video cannot play inside Rostr, so it opened in your default player.", Busy);
        OpenUrl(path);
    }

    private void ViewTick()
    {
        if (_viewing is not { IsVideo: true } || !VideoView.NaturalDuration.HasTimeSpan) return;
        _seekSync = true;
        SeekSlider.Value = Math.Min(SeekSlider.Maximum, VideoView.Position.TotalSeconds);
        _seekSync = false;
        TimeText.Text = FormatTime(VideoView.Position);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seekSync || VideoView == null || _viewing is not { IsVideo: true }) return;
        VideoView.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        TimeText.Text = FormatTime(VideoView.Position);
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VideoView != null) VideoView.Volume = VolSlider.Value;
    }
}
