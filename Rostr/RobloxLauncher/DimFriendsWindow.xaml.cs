using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RobloxLauncher.Core;

namespace RobloxLauncher;

/// <summary>
/// The friends panel that floats in the middle of the dim: the friends who are playing right now, each with a Join
/// button. Drag the header to move it. It never takes the focus from the game, so the dim stays on while you use it.
/// </summary>
public partial class DimFriendsWindow : Window
{
    private const int PanelW = 340, PanelH = 420;   // device independent pixels, same as the XAML

    // The game window (screen pixels) and where the panel sits now, from the last Present.
    private int _gx, _gy, _gw, _gh;
    private int _px = int.MinValue, _py, _pw, _ph;

    private bool _drag;
    private OverlayNative.POINT _dragStart;
    private int _dragWinX, _dragWinY;

    public event Action<FriendRow>? JoinRequested;
    public event Action? CloseRequested;
    /// <summary>Raised after a drag, with the new position as 0..1 across the free space of the game window.</summary>
    public event Action<double, double>? Moved;

    public DimFriendsWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayNative.MakeNoActivate(new WindowInteropHelper(this).Handle);
    }

    /// <summary>The friends to list (already filtered to the ones in a game), or a message when there are none.</summary>
    public void SetRows(List<FriendRow> rows, string emptyText)
    {
        FriendsItems.ItemsSource = rows;
        CountText.Text = rows.Count.ToString();
        EmptyText.Text = emptyText;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Join_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var id = Convert.ToInt64(b.Tag);
        if (FriendsItems.ItemsSource is IEnumerable<FriendRow> rows && rows.FirstOrDefault(r => r.UserId == id) is { } row)
            JoinRequested?.Invoke(row);
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
        var (x, y) = Clamp(_dragWinX + now.X - _dragStart.X, _dragWinY + now.Y - _dragStart.Y);
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

        // Saved as a fraction of the free space, so the spot also fits when the game window changes size.
        var freeW = Math.Max(1, _gw - _pw);
        var freeH = Math.Max(1, _gh - _ph);
        var fx = Math.Clamp((_px - _gx) / (double)freeW, 0, 1);
        var fy = Math.Clamp((_py - _gy) / (double)freeH, 0, 1);
        Moved?.Invoke(fx, fy);
    }

    // Keeps the panel inside the game window.
    private (int x, int y) Clamp(int x, int y) =>
        (Math.Clamp(x, _gx, Math.Max(_gx, _gx + _gw - _pw)), Math.Clamp(y, _gy, Math.Max(_gy, _gy + _gh - _ph)));

    // ---------- Showing ----------

    /// <summary>Places the panel over the game window at the saved spot. Returns true when it just appeared.</summary>
    public bool Present(IntPtr game, double fx, double fy)
    {
        if (_drag) return false;
        if (!OverlayNative.GameRect(game, out _gx, out _gy, out _gw, out _gh)) return false;

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _pw = Math.Min((int)Math.Round(PanelW * dpi), _gw);
        _ph = Math.Min((int)Math.Round(PanelH * dpi), _gh);

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

    private int _lastW, _lastH;

    /// <summary>Moves this window back above the other topmost windows.</summary>
    public void BringToTop()
    {
        if (!IsVisible) return;
        OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle, OverlayNative.HWND_TOPMOST, 0, 0, 0, 0,
            OverlayNative.SWP_NOSIZE | OverlayNative.SWP_NOMOVE | OverlayNative.SWP_NOACTIVATE);
    }

    public void HidePanel()
    {
        if (!IsVisible) return;
        if (_drag) { _drag = false; Header.ReleaseMouseCapture(); }
        Hide();
        _px = int.MinValue;
    }
}
