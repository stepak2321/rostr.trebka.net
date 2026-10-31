using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

/// <summary>Finds the Roblox window and takes a picture of it.</summary>
public static class MediaCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    // Per-monitor v2: the window sizes below are real pixels, even on a scaled (125%, 150%) display.
    private static readonly IntPtr PerMonitorV2 = new(-4);

    /// <summary>The window of a running Roblox client. The one in front wins when several run.</summary>
    public static IntPtr FindRobloxWindow()
    {
        var best = IntPtr.Zero;
        var front = GetForegroundWindow();
        foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                if (!MultiInstance.InThisSession(p)) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (h == front) return h;
                if (best == IntPtr.Zero) best = h;
            }
            catch { }
            finally { p.Dispose(); }
        }
        return best;
    }

    /// <summary>Where the game picture sits on the screen (screen pixels). False when minimized.</summary>
    public static bool TryGetClientRect(IntPtr hwnd, out int x, out int y, out int w, out int h)
    {
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try { return ClientRect(hwnd, out x, out y, out w, out h); }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }

    private static bool ClientRect(IntPtr hwnd, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (IsIconic(hwnd) || !GetClientRect(hwnd, out var r)) return false;
        var pt = new POINT();
        if (!ClientToScreen(hwnd, ref pt)) return false;
        x = pt.X; y = pt.Y; w = r.Right - r.Left; h = r.Bottom - r.Top;
        return w > 16 && h > 16;
    }

    /// <summary>
    /// A picture of the game area. First asks Windows for the window's own picture (works even when something
    /// is in front of it). Some games answer with black; then, if allowed, the pixels are copied from the screen,
    /// which needs the game to be visible.
    /// </summary>
    public static System.Drawing.Bitmap? Capture(IntPtr hwnd, bool allowScreenCopy)
    {
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            if (!ClientRect(hwnd, out var x, out var y, out var w, out var h)) return null;

            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            bool ok;
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                try { ok = PrintWindow(hwnd, hdc, 0x3); }   // PW_CLIENTONLY | PW_RENDERFULLCONTENT
                finally { g.ReleaseHdc(hdc); }
            }
            if (ok && !LooksBlank(bmp)) return bmp;

            if (!allowScreenCopy) { bmp.Dispose(); return null; }
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            return bmp;
        }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }

    // True when a grid of sample points is all (nearly) black.
    private static bool LooksBlank(System.Drawing.Bitmap bmp)
    {
        for (int i = 1; i <= 8; i++)
            for (int j = 1; j <= 8; j++)
            {
                var c = bmp.GetPixel(bmp.Width * i / 9, bmp.Height * j / 9);
                if (c.R + c.G + c.B > 24) return false;
            }
        return true;
    }
}
