using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RobloxLauncher;

/// <summary>
/// The clickable close part of the dim, in the right top corner of the Roblox window: the key hint and a close button. It never takes the focus from the game.
/// </summary>
public partial class DimControlsWindow : Window
{
    private int _x = int.MinValue, _y, _w, _h;

    public event Action? CloseRequested;

    public DimControlsWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayNative.MakeNoActivate(new WindowInteropHelper(this).Handle);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    /// <summary>Puts the controls in the right top corner of the game window. Returns true when it just appeared.</summary>
    public bool Present(IntPtr game, string hint)
    {
        if (!OverlayNative.GameRect(game, out var gx, out var gy, out var gw, out _)) return false;

        if (HintText.Text != hint) HintText.Text = hint;
        // Size and place in real pixels, so display scaling cannot move it.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var pw = (int)Math.Ceiling(Pill.DesiredSize.Width * dpi);
        var ph = (int)Math.Ceiling(Pill.DesiredSize.Height * dpi);
        var margin = (int)Math.Round(24 * dpi);
        var x = gx + gw - pw - margin;
        var y = gy + margin;

        var appeared = !IsVisible;
        if (appeared) Show();

        if (appeared || x != _x || y != _y || pw != _w || ph != _h)
        {
            _x = x; _y = y; _w = pw; _h = ph;
            OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle,
                appeared ? OverlayNative.HWND_TOPMOST : IntPtr.Zero, x, y, pw, ph,
                OverlayNative.SWP_NOACTIVATE | (appeared ? 0u : OverlayNative.SWP_NOZORDER));
        }

        return appeared;
    }

    /// <summary>Moves this window back above the other topmost windows.</summary>
    public void BringToTop()
    {
        if (!IsVisible) return;
        OverlayNative.SetWindowPos(new WindowInteropHelper(this).Handle, OverlayNative.HWND_TOPMOST, 0, 0, 0, 0,
            OverlayNative.SWP_NOSIZE | OverlayNative.SWP_NOMOVE | OverlayNative.SWP_NOACTIVATE);
    }

    public void HideControls()
    {
        if (!IsVisible) return;
        Hide();
        _x = int.MinValue;
    }
}
