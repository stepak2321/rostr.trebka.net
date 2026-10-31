using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RobloxLauncher.Core;

/// <summary>
/// Remembers which Roblox client (process) was started for which account, and keeps that list on
/// disk. With Multi Roblox on, launching again for the same account closes every client tagged with
/// that account, even one the launcher lost track of (a client that was re-spawned under a new
/// PID, or one left over from before the launcher was restarted). That stops clients piling up.
/// A tag is only trusted while the PID still has the same start time, so a reused PID is never killed.
/// </summary>
public static class ClientTags
{
    private sealed class Tag
    {
        public int Pid { get; set; }
        public long StartTicks { get; set; }   // process start time, UTC ticks
        public string Account { get; set; } = "";
    }

    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rostr", "client-tags.json");

    private static List<Tag> _tags = Load();

    private static List<Tag> Load()
    {
        var list = SafeFile.ReadJson<List<Tag>>(FilePath) ?? new List<Tag>();
        return list.Where(IsSameProcess).ToList();
    }

    private static void Save()
    {
        try { SafeFile.WriteAtomic(FilePath, JsonSerializer.Serialize(_tags)); }
        catch (Exception ex) { Log.Warn("Could not save client tags: " + ex.Message); }
    }

    private static long StartTicksOf(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime().Ticks;
        }
        catch { return 0; }
    }

    private static bool IsSameProcess(Tag t)
    {
        var ticks = StartTicksOf(t.Pid);
        return ticks != 0 && ticks == t.StartTicks;
    }

    /// <summary>Tags a running client with the account that started it.</summary>
    public static void Register(int pid, string account)
    {
        if (pid <= 0 || string.IsNullOrEmpty(account)) return;
        var ticks = StartTicksOf(pid);
        if (ticks == 0) return;

        lock (Gate)
        {
            _tags.RemoveAll(t => t.Pid == pid || !IsSameProcess(t));
            _tags.Add(new Tag { Pid = pid, StartTicks = ticks, Account = account });
            Save();
        }
    }

    private static List<int> PidsFor(string account)
    {
        lock (Gate)
        {
            _tags.RemoveAll(t => !IsSameProcess(t));
            return _tags
                .Where(t => t.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Pid)
                .ToList();
        }
    }

    private static bool IsTaggedByOther(int pid, string account)
    {
        lock (Gate)
            return _tags.Any(t => t.Pid == pid &&
                                  !t.Account.Equals(account, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// After a launch: tags every client that appeared since the launch with the account. Roblox can
    /// start its real client a few seconds after the process we started, so this keeps watching a while.
    /// </summary>
    public static async Task TagNewAsync(string account, ICollection<int> before, DateTime launchedUtc)
    {
        if (string.IsNullOrEmpty(account)) return;

        var end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < end)
        {
            foreach (var pid in MultiInstance.RunningPids())
            {
                if (before.Contains(pid) || IsTaggedByOther(pid, account)) continue;
                var ticks = StartTicksOf(pid);
                if (ticks == 0 || new DateTime(ticks, DateTimeKind.Utc) < launchedUtc.AddSeconds(-2)) continue;
                Register(pid, account);
            }
            await Task.Delay(1000);
        }
    }

    /// <summary>Closes every client tagged with the account and waits until they are gone.</summary>
    public static async Task<int> KillAccountAsync(string account, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(account)) return 0;
        var pids = PidsFor(account);
        if (pids.Count == 0) return 0;

        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(true);
            }
            catch { }   // already closed
        }

        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end && pids.Any(IsAlive)) await Task.Delay(150);

        lock (Gate)
        {
            _tags.RemoveAll(t => !IsSameProcess(t));
            Save();
        }
        Log.Info($"Multi Roblox: closed {pids.Count} old client(s) of {account}.");
        return pids.Count;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }
}
