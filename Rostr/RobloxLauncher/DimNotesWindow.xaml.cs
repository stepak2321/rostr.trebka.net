using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RobloxLauncher;

/// <summary>
/// A small note pad inside the dim, one text per game. Like the wiki it can take the keyboard (MainWindow keeps
/// the dim on while this window is the active one). The text is saved a moment after you stop typing.
/// </summary>
public partial class DimNotesWindow : Window
{
    private int _gx, _gy, _gw, _gh;
    private int _px = int.MinValue, _py, _pw, _ph, _lastW, _lastH;

    private bool _drag;
    private OverlayNative.POINT _dragStart;
    private int _dragWinX, _dragWinY;

    private bool _loading;   // true while the text is replaced by code (not typed), so it is not saved again
    private bool _dirty;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>The game (place id) the text in the box belongs to; 0 when no game is known.</summary>
    public long Place { get; private set; }

    public event Action? CloseRequested;
    /// <summary>Raised after a drag, with the new position as 0..1 across the free space of the game window.</summary>
    public event Action<double, double>? Moved;
    /// <summary>The text of a game after it was typed in (place id, text).</summary>
    public event Action<long, string>? Edited;

    public DimNotesWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => FlushSave();
        Deactivated += (_, _) => FlushSave();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        OverlayNative.MakeToolWindow(hwnd);
        OverlayNative.RoundCorners(hwnd);
    }

    /// <summary>Shows the note of another game. The text of the game before is saved first.</summary>
    public void SetNote(long place, string text)
    {
        FlushSave();
        _loading = true;
        Place = place;
        Box.IsEnabled = place > 0;
        Box.Text = place > 0 ? text : "";
        Placeholder.Text = place > 0 ? "Write a note for this game..." : "Notes work once you are in a game.";
        Placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "Saved on this PC";
        _loading = false;
    }

    public void SetTitle(string? gameName)
    {
        var title = string.IsNullOrWhiteSpace(gameName) ? "Notes" : "Notes - " + gameName;
        if (TitleText.Text != title) TitleText.Text = title;
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_loading || Place <= 0) return;

        _dirty = true;
        StatusText.Text = "Saving...";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Hands a typed text to MainWindow right now (also called when the window loses the keyboard or hides).</summary>
    public void FlushSave()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        if (Place <= 0) return;
        Edited?.Invoke(Place, Box.Text);
        StatusText.Text = "Saved on this PC";
    }

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

    /// <summary>Places the notes over the game window at the saved spot. Returns true when it just appeared.</summary>
    public bool Present(IntPtr game, double fx, double fy)
    {
        if (_drag) return false;
        if (!OverlayNative.GameRect(game, out _gx, out _gy, out _gw, out _gh)) return false;

        // Real pixels, so display scaling cannot change the size; never bigger than the game window.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _pw = Math.Min(_gw, (int)Math.Round(360 * dpi));
        _ph = Math.Min(_gh, (int)Math.Round(340 * dpi));
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

    public void HideNotes()
    {
        FlushSave();
        if (!IsVisible) return;
        if (_drag) { _drag = false; Header.ReleaseMouseCapture(); }
        Hide();
        _px = int.MinValue;
    }
}
