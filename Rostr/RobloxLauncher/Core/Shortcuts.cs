using System.IO;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

/// <summary>
/// "Install": copies Rostr.exe into the Rostr data folder (%LocalAppData%\Rostr\app) and puts a shortcut
/// to that copy in the Start menu (so Windows search finds it) and on the desktop. Because everything
/// points at the copy, deleting or moving the downloaded Rostr.exe breaks nothing. The copy updates
/// itself in place like any other Rostr.exe. Needs no admin rights.
/// </summary>
public static class Shortcuts
{
    private const string LinkName = "Rostr.lnk";

    private static string StartMenuLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), LinkName);

    private static string DesktopLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinkName);

    /// <summary>Where the installed copy of Rostr.exe lives, next to settings and accounts. No "bin" in the path on purpose: self-update refuses those.</summary>
    public static string InstalledExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "app", "Rostr.exe");

    public static bool HasInstalledCopy => File.Exists(InstalledExe);

    /// <summary>The Rostr.exe that is running now. Null for "dotnet run" and other development starts.</summary>
    public static string? RunningExe()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return exe;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public static bool RunningFromInstalledCopy()
    {
        var running = RunningExe();
        return running != null && SamePath(running, InstalledExe);
    }

    /// <summary>
    /// The exe that shortcuts, the startup entry and the roblox-player: handler should open: the installed
    /// copy when there is one, otherwise the running exe. Null for development starts.
    /// </summary>
    public static string? TargetExe()
    {
        var running = RunningExe();
        if (running == null) return null;
        return HasInstalledCopy ? InstalledExe : running;
    }

    private static Version? FileVer(string path)
    {
        try { return Updater.VersionOfFile(path); }
        catch { return null; }
    }

    /// <summary>Copies the running Rostr.exe to the data folder. Keeps a newer copy that is already there.</summary>
    private static void EnsureCopy()
    {
        var running = RunningExe() ?? throw new InvalidOperationException(
            "Installing needs the published Rostr.exe. Run build.bat and start it from dist.");
        if (SamePath(running, InstalledExe)) return;

        if (HasInstalledCopy)
        {
            var have = FileVer(InstalledExe);
            var mine = FileVer(running);
            if (have != null && mine != null && have > mine)
            {
                Log.Info("Kept the installed copy, it is newer than this exe");
                return;
            }
        }

        var tmp = InstalledExe + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InstalledExe)!);
            File.Copy(running, tmp, true);
            File.Move(tmp, InstalledExe, true);
            Log.Info("Copied Rostr.exe to " + InstalledExe);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            if (HasInstalledCopy)
                Log.Warn("Could not refresh the installed copy, keeping the old one: " + ex.Message);
            else
                throw new InvalidOperationException("Could not copy Rostr.exe to " + InstalledExe + ": " + ex.Message);
        }
    }

    public static bool HasStartMenu => File.Exists(StartMenuLink);
    public static bool HasDesktop => File.Exists(DesktopLink);

    /// <summary>True when every shortcut that exists points at this exe.</summary>
    public static bool PointsHere()
    {
        var exe = TargetExe();
        if (exe == null) return false;
        foreach (var link in new[] { StartMenuLink, DesktopLink })
        {
            if (!File.Exists(link)) continue;
            var target = ReadTarget(link);
            if (!string.Equals(target, exe, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>The version of the installed copy, read from the file itself. Null when there is no copy.</summary>
    public static Version? InstalledVersion() => HasInstalledCopy ? FileVer(InstalledExe) : null;

    /// <summary>Saves the version of the installed copy in the settings (empty when there is none).</summary>
    public static void RecordInstalledVersion()
    {
        try
        {
            var v = InstalledVersion();
            var text = v == null ? "" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
            var s = RobloxLauncher.Settings.AppSettings.Current;
            if (s.InstalledVersion == text) return;
            s.InstalledVersion = text;
            s.Save();
            Log.Info("Installed version is now " + (text.Length == 0 ? "none" : text));
        }
        catch (Exception ex) { Log.Warn("Could not save the installed version: " + ex.Message); }
    }

    /// <summary>After an update of a Rostr.exe that is NOT the installed copy: bring the installed copy up to date too.</summary>
    public static void SyncInstalledCopy()
    {
        if (!HasInstalledCopy || RunningFromInstalledCopy()) return;
        try { EnsureCopy(); }
        catch (Exception ex) { Log.Warn("Could not update the installed copy: " + ex.Message); }
        RecordInstalledVersion();
    }

    /// <summary>Starts the installed copy (it waits for this Rostr to close). False when there is none or it will not start.</summary>
    public static bool LaunchInstalledCopy()
    {
        try
        {
            if (!HasInstalledCopy) return false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(InstalledExe, "--updated")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(InstalledExe)!
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not start the installed copy: " + ex.Message);
            return false;
        }
    }

    public static void Install()
    {
        EnsureCopy();
        RecordInstalledVersion();   // saves which version the installed copy is
        var exe = TargetExe()!;     // the installed copy now
        Create(StartMenuLink, exe);
        Create(DesktopLink, exe);
        Log.Info("Installed Start menu and desktop shortcuts for " + exe);
    }

    public static void Uninstall()
    {
        foreach (var link in new[] { StartMenuLink, DesktopLink })
        {
            try { if (File.Exists(link)) File.Delete(link); }
            catch (Exception ex) { Log.Warn("Could not remove " + link + ": " + ex.Message); }
        }
        Log.Info("Removed Start menu and desktop shortcuts");

        // Delete the installed copy too, unless it is the one running right now (Windows locks it).
        if (HasInstalledCopy && !RunningFromInstalledCopy())
        {
            try { File.Delete(InstalledExe); Log.Info("Removed the installed copy"); }
            catch (Exception ex) { Log.Warn("Could not remove the installed copy: " + ex.Message); }
        }
        RecordInstalledVersion();
    }

    // Windows has no .NET API for .lnk files. WScript.Shell is part of Windows, so no extra package is needed.
    private static void Create(string linkPath, string exe)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
                   ?? throw new InvalidOperationException("Windows could not create a shortcut (WScript.Shell is missing).");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic? link = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            link = shell.CreateShortcut(linkPath);
            link.TargetPath = exe;
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.IconLocation = exe + ",0";
            link.Description = "Rostr: a Roblox launcher that shows you the server first";
            link.Save();
        }
        finally
        {
            if (link != null) Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string? ReadTarget(string linkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(linkPath);
            string? target = link.TargetPath;
            Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
            return target;
        }
        catch
        {
            return null;
        }
    }
}
