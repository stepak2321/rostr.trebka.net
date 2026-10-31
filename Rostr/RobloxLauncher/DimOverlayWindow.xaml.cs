using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RobloxLauncher;

/// <summary>
/// A big click-through window that darkens (and, when Windows allows it, blurs) the whole Roblox window.
/// It shows the local time and how long you have been playing (the close key, the friends button and the friends
/// panel are separate small windows on top of it, because this one is click-through). Mouse and keyboard
/// still reach the game underneath. MainWindow decides when it is shown (only while Roblox is in front).
/// </summary>
public partial class DimOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private int _x = int.MinValue, _y, _w, _h;

    public DimOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Click-through and never takes the keyboard from the game.
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // Blur what is behind the window (the game). Windows decides how strong the blur is, so the dark tint is
        // what can be changed here. If Windows refuses the blur, a slightly darker tint is used instead.
        var blur = TryEnableBlur(hwnd);
        Root.Background = new SolidColorBrush(blur ? Color.FromArgb(0x46, 0, 0, 0) : Color.FromArgb(0x5C, 0, 0, 0));
    }

    // Undocumented but long-standing Windows call (ACCENT_ENABLE_BLURBEHIND). Any failure is harmless.
    private static bool TryEnableBlur(IntPtr hwnd)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new AccentPolicy { AccentState = 3 }, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Covers the client area of the game window and updates the texts.
    /// Returns true when the dim just appeared (so other overlays can move back on top of it).
    /// </summary>
    public bool Present(IntPtr game, string clock, string playing)
    {
        if (!GetClientRect(game, out var rc) || rc.Right <= 0 || rc.Bottom <= 0) return false;
        var origin = new POINT();
        ClientToScreen(game, ref origin);

        ClockText.Text = clock;
        PlayText.Text = playing;

        var appeared = !IsVisible;
        if (appeared) Show();

        // Pixel positions on purpose, so display scaling cannot offset it.
        if (appeared || origin.X != _x || origin.Y != _y || rc.Right != _w || rc.Bottom != _h)
        {
            _x = origin.X; _y = origin.Y; _w = rc.Right; _h = rc.Bottom;
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, appeared ? HWND_TOPMOST : IntPtr.Zero, _x, _y, _w, _h,
                         SWP_NOACTIVATE | (appeared ? 0u : SWP_NOZORDER));
        }

        return appeared;
    }

    public void HideDim()
    {
        if (!IsVisible) return;
        Hide();
        _x = int.MinValue;
    }
}
