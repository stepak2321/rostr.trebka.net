using System.Runtime.InteropServices;

namespace RobloxLauncher;

/// <summary>Win32 helpers shared by the windows that sit on top of the Roblox window (dim controls, friends panel).</summary>
internal static class OverlayNative
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Clickable, but it never takes the keyboard (or the "front window" status) away from the game.</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>Hidden from Alt+Tab, but it can take the keyboard (the wiki needs that to type in its search box).</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
    }

    /// <summary>Round corners for a window without transparency (Windows 11; other versions simply ignore it).</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        try { var round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); } catch { }
    }

    /// <summary>The client area of the game window in screen pixels.</summary>
    public static bool GameRect(IntPtr game, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (!GetClientRect(game, out var rc) || rc.Right <= 0 || rc.Bottom <= 0) return false;
        var origin = new POINT();
        ClientToScreen(game, ref origin);
        x = origin.X; y = origin.Y; w = rc.Right; h = rc.Bottom;
        return true;
    }
}
