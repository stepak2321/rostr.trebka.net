using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace RobloxLauncher.Core;

/// <summary>Follows the log file of ONE Roblox client, so several clients can run side by side.</summary>
public class LogWatcher : IDisposable
{
    public event Action<string, long, string>? GameJoined;     // job id, place id, ip
    public event Action<string, string?>? ServerAddress;       // relay ip, real server ip (when public)
    public event Action? GameClosed;
    public event Action<int>? PidAdopted;                      // the client we really follow, when it differs from the one we started

    private static readonly Regex JoinRx = new(
        @"Joining game '([0-9a-fA-F\-]{36})' place (\d+) at ([0-9\.]+)", RegexOptions.Compiled);
    private static readonly Regex UdmuxRx = new(
        @"UDMUX Address = ([0-9\.]+), Port = \d+(?:\s*\|\s*RCC Server Address = ([0-9\.]+))?", RegexOptions.Compiled);
    private static readonly Regex ConnectRx = new(
        @"Connecting to UDMUX server ([0-9\.]+):\d+(?:, and RCC server ([0-9\.]+):\d+)?", RegexOptions.Compiled);

    // Log files already followed by another watcher.
    private static readonly HashSet<string> Claimed = new(StringComparer.OrdinalIgnoreCase);

    // Client processes already followed by some watcher, so two sessions never share one client.
    private static readonly HashSet<int> ClaimedPids = new();

    private CancellationTokenSource? _cts;

    public void Start(DateTime launchedUtc, int pid)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(launchedUtc, pid, token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    private static bool IsAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the client that belongs to this launch: a RobloxPlayerBeta that started just after we
    /// did and is not followed by another watcher. Returns 0 when there is no safe match.
    /// </summary>
    private static int FindOwnClient(DateTime launchedUtc)
    {
        foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                if (p.StartTime.ToUniversalTime() < launchedUtc.AddSeconds(-2)) continue;
                lock (ClaimedPids)
                {
                    if (ClaimedPids.Contains(p.Id)) continue;
                    ClaimedPids.Add(p.Id);
                    return p.Id;
                }
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }
        return 0;
    }

    private async Task RunAsync(DateTime launchedUtc, int pid, CancellationToken ct)
    {
        string? file = null;
        int claimedPid = 0;
        try
        {
            if (pid > 0)
                lock (ClaimedPids) ClaimedPids.Add(claimedPid = pid);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "logs");

            for (int i = 0; i < 120 && file == null && !ct.IsCancellationRequested; i++)
            {
                if (Directory.Exists(dir))
                {
                    lock (Claimed)
                    {
                        file = new DirectoryInfo(dir).GetFiles("*.log")
                            .Where(f => f.Name.Contains("Player") &&
                                        f.CreationTimeUtc >= launchedUtc.AddSeconds(-1) &&
                                        !Claimed.Contains(f.FullName))
                            .OrderBy(f => f.CreationTimeUtc)
                            .FirstOrDefault()?.FullName;
                        if (file != null) Claimed.Add(file);
                    }
                }
                if (file == null) await Task.Delay(500, ct);
            }
            if (file == null)
            {
                Log.Warn("LogWatcher: no Roblox log file appeared for this launch.");
                return;
            }

            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);

            bool seenProcess = false;
            int missingLoops = 0;

            while (!ct.IsCancellationRequested)
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                    Parse(line);

                if (IsAlive(pid))
                {
                    seenProcess = true;
                    missingLoops = 0;
                }
                else if (seenProcess)
                {
                    GameClosed?.Invoke();
                    return;
                }
                else
                {
                    // The process we started is gone and we never saw it running. Look for the client
                    // that really belongs to this launch, but only one nobody else is following.
                    var found = FindOwnClient(launchedUtc);
                    if (found > 0)
                    {
                        if (claimedPid > 0) lock (ClaimedPids) ClaimedPids.Remove(claimedPid);
                        claimedPid = pid = found;
                        seenProcess = true;
                        Log.Info($"LogWatcher: following client PID {found}");
                        PidAdopted?.Invoke(found);
                    }
                    else if (++missingLoops > 60)
                    {
                        Log.Warn("LogWatcher: could not find the client for this launch; giving up.");
                        return;
                    }
                }

                await Task.Delay(700, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("LogWatcher stopped unexpectedly", ex);
        }
        finally
        {
            if (file != null)
            {
                lock (Claimed) Claimed.Remove(file);
            }
            if (claimedPid > 0)
            {
                lock (ClaimedPids) ClaimedPids.Remove(claimedPid);
            }
        }
    }

    private static bool IsPublic(string? ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var a)) return false;
        var b = a.GetAddressBytes();
        if (b.Length != 4) return false;
        return !(b[0] == 10 || b[0] == 127 || (b[0] == 192 && b[1] == 168) ||
                 (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 169 && b[1] == 254));
    }

    private void Parse(string line)
    {
        var m = JoinRx.Match(line);
        if (m.Success)
        {
            GameJoined?.Invoke(m.Groups[1].Value, long.Parse(m.Groups[2].Value), m.Groups[3].Value);
            return;
        }

        m = UdmuxRx.Match(line);
        if (!m.Success) m = ConnectRx.Match(line);
        if (m.Success)
        {
            var rcc = m.Groups[2].Success && IsPublic(m.Groups[2].Value) ? m.Groups[2].Value : null;
            ServerAddress?.Invoke(m.Groups[1].Value, rcc);
        }
    }
}
