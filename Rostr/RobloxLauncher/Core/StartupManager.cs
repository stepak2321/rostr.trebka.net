using Microsoft.Win32;

namespace RobloxLauncher.Core;

/// <summary>
/// Starts Rostr when Windows starts, through the per-user Run key. Needs no admin rights.
/// The entry points at the Rostr.exe that is running, with --startup so it opens in the tray
/// instead of showing the window.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Rostr";
    public const string Arg = "--startup";

    private static string? Expected()
    {
        var exe = Shortcuts.TargetExe();
        return exe == null ? null : "\"" + exe + "\" " + Arg;
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    public static void Enable()
    {
        var cmd = Expected() ?? throw new InvalidOperationException(
            "Starting with Windows needs the published Rostr.exe. Run build.bat and start it from dist.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, cmd, RegistryValueKind.String);
        Log.Info("Start with Windows is on: " + cmd);
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("Start with Windows is off");
        }
        catch (Exception ex) { Log.Warn("Could not remove the startup entry: " + ex.Message); }
    }

    /// <summary>If the entry exists but points at another copy (Rostr.exe was moved), point it here.</summary>
    public static void Refresh()
    {
        try
        {
            var expected = Expected();
            if (expected == null || !IsEnabled) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current &&
                !string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, expected, RegistryValueKind.String);
        }
        catch (Exception ex) { Log.Warn("Startup entry refresh failed: " + ex.Message); }
    }
}
