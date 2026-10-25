using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RobloxLauncher.Core;

namespace RobloxLauncher;

/// <summary>
/// A click-through stats box that sits inside one Roblox window. It only shows while that
/// window is the one in front, so it never floats over other apps.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Toggled by the global hotkey. While true no overlay is shown.</summary>
    public static bool Hidden { get; set; }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _start = DateTime.Now;
    private int _corner = 3;
    private int _tick;

    private int _pid;
    private Process? _proc;
    private DateTime _previewUntil = DateTime.MinValue;

    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt;

    private PingTarget? _target;
    private CancellationTokenSource? _pingCts;
    private bool _pinging;

    public OverlayWindow()
    {
        InitializeComponent();
        Loc.Apply(this);
        _timer.Tick += OnTick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void SetCorner(int corner) => _corner = corner;

    private bool _hasAccount;
    private bool _showPing = true, _showUser = true, _anyField = true;

    public void SetAccount(string? name)
    {
        _hasAccount = !string.IsNullOrEmpty(name);
        AccountText.Text = name ?? "";
        ApplyUserRow();
        if (_hasAccount && _showUser) _anyField = true;
    }

    private void ApplyUserRow()
    {
        var v = _hasAccount && _showUser ? Visibility.Visible : Visibility.Collapsed;
        AccountText.Visibility = v;
        AccountLabel.Visibility = v;
    }

    /// <summary>Chooses which lines the overlay shows.</summary>
    public void SetFields(bool ping, bool cpu, bool ram, bool time, bool region, bool user)
    {
        static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        PingLabel.Visibility = PingText.Visibility = V(ping);
        CpuLabel.Visibility = CpuText.Visibility = V(cpu);
        RamLabel.Visibility = RamText.Visibility = V(ram);
        TimeLabel.Visibility = TimeText.Visibility = V(time);
        RegionLabel.Visibility = RegionText.Visibility = V(region);
        _showPing = ping;
        _showUser = user;
        _anyField = ping || cpu || ram || time || region || (user && _hasAccount);
        ApplyUserRow();
    }

    /// <summary>Sets the address to ping and, optionally, the region text.</summary>
    public void SetServer(string? ip, string? region)
    {
        _pingCts?.Cancel();
        _pingCts = null;
        _target = null;
        PingText.Text = "-";
        PingText.Foreground = Brushes.White;
        RegionText.Text = region ?? "-";

        if (ip == null) return;
        var cts = _pingCts = new CancellationTokenSource();
        _ = ResolveTargetAsync(ip, cts.Token);
    }

    public void SetRegion(string region) => RegionText.Text = region;

    private async Task ResolveTargetAsync(string ip, CancellationToken ct)
    {
        PingText.Text = "...";
        try
        {
            var t = await PingProbe.FindTargetAsync(ip, ct);
            if (ct.IsCancellationRequested) return;
            _target = t;
            if (t == null) PingText.Text = "n/a";
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested) PingText.Text = "n/a";
        }
    }

    /// <summary>Follow the Roblox client with this process id.</summary>
    public void Attach(int pid)
    {
        _proc?.Dispose();
        _pid = pid;
        try { _proc = Process.GetProcessById(pid); }
        catch { _proc = null; }

        _start = DateTime.Now;
        _lastCpuAt = default;
        _timer.Start();
    }

    /// <summary>Show for a few seconds in the corner of the screen, without a game running.</summary>
    public void Preview(int seconds)
    {
        _previewUntil = DateTime.UtcNow.AddSeconds(seconds);
        if (_pid == 0) _start = DateTime.Now;
        _timer.Start();
        UpdatePlacement();
    }

    public void StopOverlay()
    {
        _timer.Stop();
        _pingCts?.Cancel();
        _pingCts = null;
        _target = null;
        _proc?.Dispose();
        _proc = null;
        _pid = 0;
        _previewUntil = DateTime.MinValue;
        if (IsVisible) Hide();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var previewing = DateTime.UtcNow < _previewUntil;
        if (_pid == 0 && !previewing)
        {
            StopOverlay();
            return;
        }

        UpdatePlacement();
        if (!IsVisible) return;

        TimeText.Text = (DateTime.Now - _start).ToString(@"hh\:mm\:ss");
        if (_tick++ % 2 == 0) UpdateStats();
        if (_tick % 4 == 0 && _target != null && !_pinging && _showPing) _ = PingAsync();
    }

    private void UpdateStats()
    {
        try
        {
            if (_proc == null || _proc.HasExited) return;
            _proc.Refresh();

            RamText.Text = $"{_proc.WorkingSet64 / 1048576:N0} MB";

            var cpu = _proc.TotalProcessorTime;
            var now = DateTime.UtcNow;
            if (_lastCpuAt != default)
            {
                var used = (cpu - _lastCpu).TotalMilliseconds;
                var wall = (now - _lastCpuAt).TotalMilliseconds * Environment.ProcessorCount;
                if (wall > 0) CpuText.Text = $"{Math.Clamp(used / wall * 100, 0, 100):0}%";
            }
            _lastCpu = cpu;
            _lastCpuAt = now;
        }
        catch { }
    }

    private async Task PingAsync()
    {
        var target = _target;
        if (target == null) return;

        _pinging = true;
        try
        {
            var rtt = await PingProbe.MeasureAsync(target);
            if (target != _target) return;

            if (rtt == null)
            {
                PingText.Text = Loc.T("no reply");
                PingText.Foreground = Brushes.White;
                return;
            }

            PingText.Text = $"{(target.Estimated ? "~" : "")}{rtt} ms";
            PingText.Foreground = rtt < 80 ? Green : rtt < 150 ? Amber : Red;
        }
        finally
        {
            _pinging = false;
        }
    }

    private static readonly Brush Green = Frozen(0x3D, 0xD6, 0x8C);
    private static readonly Brush Amber = Frozen(0xF5, 0xB8, 0x3D);
    private static readonly Brush Red = Frozen(0xF2, 0x5C, 0x5C);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // Shows the overlay in the client area of its Roblox window, or hides it when that window
    // is not in front. Pixel positions are used on purpose so display scaling cannot offset it.
    private void UpdatePlacement()
    {
        var previewing = DateTime.UtcNow < _previewUntil;
        IntPtr game = IntPtr.Zero;

        if (_proc != null)
        {
            try
            {
                _proc.Refresh();
                if (!_proc.HasExited) game = _proc.MainWindowHandle;
            }
            catch { }
        }

        bool inFront = game != IntPtr.Zero && !IsIconic(game) && GetForegroundWindow() == game;
        bool show = _anyField && (previewing || (inFront && !Hidden));

        if (!show)
        {
            if (IsVisible) Hide();
            return;
        }

        if (!IsVisible) Show();

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        int margin = (int)Math.Round(14 * dpi.DpiScaleX);

        int left, top, right, bottom;
        if (inFront && GetClientRect(game, out var rc))
        {
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(game, ref origin);
            left = origin.X; top = origin.Y;
            right = origin.X + rc.Right; bottom = origin.Y + rc.Bottom;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            left = (int)(wa.Left * dpi.DpiScaleX); top = (int)(wa.Top * dpi.DpiScaleY);
            right = (int)(wa.Right * dpi.DpiScaleX); bottom = (int)(wa.Bottom * dpi.DpiScaleY);
        }

        int x = (_corner == 1 || _corner == 3) ? right - w - margin : left + margin;
        int y = (_corner == 2 || _corner == 3) ? bottom - h - margin : top + margin;

        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
