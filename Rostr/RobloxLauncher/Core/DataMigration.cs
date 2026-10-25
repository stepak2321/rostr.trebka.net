using System.IO;

namespace RobloxLauncher.Core;

/// <summary>
/// Earlier versions kept their data in %LocalAppData%\MyRobloxLauncher. The folder is now called Rostr.
/// This moves the old folder over once, so accounts, settings, FastFlags and downloaded clients survive.
/// </summary>
public static class DataMigration
{
    public static void Run()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var oldDir = Path.Combine(root, "MyRobloxLauncher");
            var newDir = Path.Combine(root, "Rostr");
            if (!Directory.Exists(oldDir)) return;

            if (!Directory.Exists(newDir))
            {
                try
                {
                    Directory.Move(oldDir, newDir);
                    return;
                }
                catch
                {
                    // Something holds a file open: fall through and copy instead.
                }
            }

            CopyMissing(oldDir, newDir);
        }
        catch
        {
            // Migration is best effort. The launcher still starts with a fresh folder.
        }
    }

    private static void CopyMissing(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (var dir in Directory.GetDirectories(from))
            CopyMissing(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
