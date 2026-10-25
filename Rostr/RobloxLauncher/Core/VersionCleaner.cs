using System.IO;

namespace RobloxLauncher.Core;

public static class VersionCleaner
{
    public static (int Removed, long Freed, bool Skipped) Clean(
        string versionsRoot, string keepGuid, int keepPrevious = 1)
    {
        if (!Directory.Exists(versionsRoot)) return (0, 0, false);

        // Never delete anything while a Roblox client is running
        var running = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta");
        var isRunning = running.Length > 0;
        foreach (var p in running) p.Dispose();
        if (isRunning) return (0, 0, true);

        var old = new DirectoryInfo(versionsRoot).GetDirectories("version-*")
            .Where(d => !d.Name.Equals(keepGuid, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.CreationTimeUtc)
            .Skip(keepPrevious)
            .ToList();

        int removed = 0;
        long freed = 0;
        foreach (var d in old)
        {
            try
            {
                var size = DirSize(d);
                d.Delete(true);
                removed++;
                freed += size;
            }
            catch { }
        }
        return (removed, freed, false);
    }

    public static (int Count, long Bytes) Measure(string versionsRoot)
    {
        if (!Directory.Exists(versionsRoot)) return (0, 0);
        var dirs = new DirectoryInfo(versionsRoot).GetDirectories("version-*");
        return (dirs.Length, dirs.Sum(DirSize));
    }

    private static long DirSize(DirectoryInfo d)
    {
        try
        {
            return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}