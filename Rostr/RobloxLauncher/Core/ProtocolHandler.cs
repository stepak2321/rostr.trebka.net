using System.IO;
using Microsoft.Win32;

namespace RobloxLauncher.Core;

public static class ProtocolHandler
{
    private const string KeyPath = @"Software\Classes\roblox-player";
    private const string CommandPath = KeyPath + @"\shell\open\command";

    private static string BackupFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "original-handler.txt");

    /// <summary>The command Windows should run for roblox-player: links: this exe, no cmd or batch file.</summary>
    public static string? BuildCommand()
    {
        // The installed copy when Install was used (so deleting the downloaded exe breaks nothing), else this exe.
        // Null for "dotnet run" and other development starts: not a stable target.
        var exe = Shortcuts.TargetExe();
        if (exe == null) return null;
        return $"\"{exe}\" \"%1\"";
    }

    private static bool IsOurs(string command) =>
        command.Contains("Rostr", StringComparison.OrdinalIgnoreCase) ||
        command.Contains("RobloxLauncher", StringComparison.OrdinalIgnoreCase) ||
        command.Contains("start.bat", StringComparison.OrdinalIgnoreCase);

    public static void Register(string command)
    {
        // Save the current (official) command once, so we can restore it. Never save one of ours.
        if (!File.Exists(BackupFile))
        {
            using var existing = Registry.CurrentUser.OpenSubKey(CommandPath);
            var old = existing?.GetValue("") as string ?? "";
            if (!IsOurs(old))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
                File.WriteAllText(BackupFile, old);
            }
        }

        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            key.SetValue("", "URL: Roblox Protocol");
            key.SetValue("URL Protocol", "");
        }
        using var cmd = Registry.CurrentUser.CreateSubKey(CommandPath);
        cmd.SetValue("", command);
        Log.Info("Registered roblox-player handler: " + command);
    }

    /// <summary>
    /// Registers (or re-points) the handler on its own, so nobody has to press Register.
    /// Does nothing in a dev build, or after the user pressed Unregister (AutoRegister = false).
    /// Safe to call on every start: it only writes to the registry when something is out of date,
    /// which also keeps the handler correct after the exe is moved or updated.
    /// </summary>
    public static void EnsureRegistered()
    {
        try
        {
            if (!RobloxLauncher.Settings.AppSettings.Current.AutoRegister) return;
            var cmd = BuildCommand();
            if (cmd == null || IsRegistered()) return;
            Register(cmd);
        }
        catch (Exception ex)
        {
            Log.Warn("Automatic register failed: " + ex.Message);
        }
    }

    public static void Unregister()
    {
        var old = File.Exists(BackupFile) ? File.ReadAllText(BackupFile) : "";

        if (string.IsNullOrWhiteSpace(old) || IsOurs(old))
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        else
        {
            using var cmd = Registry.CurrentUser.CreateSubKey(CommandPath);
            cmd.SetValue("", old);
        }
        if (File.Exists(BackupFile)) File.Delete(BackupFile);
        Log.Info("Restored the original roblox-player handler");
    }

    /// <summary>True only when the registered command points at THIS exe.</summary>
    public static bool IsRegistered()
    {
        var want = BuildCommand();
        if (want == null) return false;
        using var cmd = Registry.CurrentUser.OpenSubKey(CommandPath);
        return string.Equals(cmd?.GetValue("") as string, want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when something of ours (an older start.bat setup or another copy) is registered.</summary>
    public static bool IsRegisteredElsewhere()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey(CommandPath);
        var current = cmd?.GetValue("") as string ?? "";
        return IsOurs(current) && !IsRegistered();
    }
}
