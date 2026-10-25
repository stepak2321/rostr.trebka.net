using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

/// <summary>
/// Lets several Roblox clients run at once. Roblox clients hold an event called
/// ROBLOX_singletonEvent; a new client quits if it already exists. This watches every
/// RobloxPlayerBeta process and closes that handle inside it, so the next client can start.
/// </summary>
public sealed class MultiInstance : IDisposable
{
    private const string TargetEvent = "ROBLOX_singletonEvent";
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<int, byte> _handled = new();

    // Handle values already inspected per process. Describing a handle (duplicate + two queries)
    // is the expensive part of a scan, so each handle is only examined once. The cache is wiped
    // every few scans in case Windows reuses a handle value for something new.
    private readonly Dictionary<int, HashSet<IntPtr>> _inspected = new();
    private readonly Dictionary<int, int> _scanCount = new();
    private const int FullRescanEvery = 12;
    private CancellationTokenSource? _cts;

    public event Action<string>? Status;

    public bool Running => _cts != null;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => LoopAsync(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    public static List<int> RunningPids()
    {
        var list = new List<int>();
        foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            list.Add(p.Id);
            p.Dispose();
        }
        return list;
    }

    /// <summary>
    /// Closes every running Roblox client (and its crash handler), including one that is only
    /// left in the tray, and waits until they are gone.
    /// </summary>
    public static async Task<bool> KillAllAsync(TimeSpan timeout)
    {
        foreach (var name in new[] { "RobloxPlayerBeta", "RobloxCrashHandler" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(true); } catch { }
                p.Dispose();
            }
        }

        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (RunningPids().Count == 0) return true;
            await Task.Delay(150);
        }
        return RunningPids().Count == 0;
    }

    /// <summary>Waits until every running client has had its singleton event closed.</summary>
    public async Task<bool> WaitUntilClearAsync(TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (RunningPids().All(p => _handled.ContainsKey(p))) return true;
            await Task.Delay(200);
        }
        return false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var firstSeen = new Dictionary<int, DateTime>();
        string? lastError = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pids = RunningPids();

                foreach (var dead in firstSeen.Keys.Where(p => !pids.Contains(p)).ToList())
                {
                    firstSeen.Remove(dead);
                    _handled.TryRemove(dead, out _);
                    _inspected.Remove(dead);
                    _scanCount.Remove(dead);
                }
                // A handled process that is gone is dropped above only if we tracked firstSeen for it.
                foreach (var gone in _handled.Keys.Where(p => !pids.Contains(p)).ToList())
                    _handled.TryRemove(gone, out _);

                var todo = new List<int>();
                foreach (var pid in pids)
                {
                    if (_handled.ContainsKey(pid)) continue;
                    if (!firstSeen.TryGetValue(pid, out var t0)) firstSeen[pid] = t0 = DateTime.UtcNow;
                    if (DateTime.UtcNow - t0 > GiveUpAfter)
                    {
                        _handled[pid] = 1;
                        Log.Warn($"Multi Roblox: PID {pid}: could not find the singleton event, giving up.");
                        Status?.Invoke($"PID {pid}: could not find the singleton event, giving up.");
                        continue;
                    }
                    todo.Add(pid);
                }

                if (todo.Count > 0)
                {
                    var table = NativeHandles.QueryHandles(todo);
                    foreach (var pid in todo)
                    {
                        if (!table.TryGetValue(pid, out var handles)) continue;
                        if (TryCloseEvent(pid, handles)) _handled[pid] = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                // Only report a given error once, not every 500 ms.
                if (ex.Message != lastError)
                {
                    Log.Error("Multi Roblox scan failed", ex);
                    Status?.Invoke("Multi Roblox: " + ex.Message);
                }
                lastError = ex.Message;
            }

            // Fast while a new client still needs its event closed, relaxed when there is nothing to do.
            var busy = RunningPids().Any(p => !_handled.ContainsKey(p));
            try { await Task.Delay(busy ? 250 : 1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool TryCloseEvent(int pid, List<IntPtr> handles)
    {
        var source = NativeHandles.OpenForDuplicate(pid);
        if (source == IntPtr.Zero) return false;

        if (!_inspected.TryGetValue(pid, out var seen))
            _inspected[pid] = seen = new HashSet<IntPtr>();
        var scans = _scanCount.TryGetValue(pid, out var c) ? c + 1 : 1;
        _scanCount[pid] = scans;
        if (scans % FullRescanEvery == 0) seen.Clear();

        try
        {
            foreach (var h in handles)
            {
                if (!seen.Add(h)) continue;   // already looked at this one

                var (type, name) = NativeHandles.Describe(source, h, "Event");
                if (!type.Equals("Event", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(name)) continue;

                var leaf = name.Substring(name.LastIndexOf('\\') + 1);
                if (!leaf.Equals(TargetEvent, StringComparison.OrdinalIgnoreCase)) continue;

                if (NativeHandles.CloseRemote(source, h))
                {
                    Log.Info($"Multi Roblox: closed the singleton event in PID {pid}");
                    Status?.Invoke($"PID {pid}: multi-instance ready.");
                    _inspected.Remove(pid);
                    _scanCount.Remove(pid);
                    return true;
                }
                seen.Remove(h);   // could not close it yet, try again next scan
            }
        }
        finally
        {
            NativeHandles.Close(source);
        }
        return false;
    }
}

internal static class NativeHandles
{
    private const int SystemExtendedHandleInformation = 64;
    private const int ObjectNameInformation = 1;
    private const int ObjectTypeInformation = 2;

    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint DUPLICATE_CLOSE_SOURCE = 0x1;
    private const uint DUPLICATE_SAME_ACCESS = 0x2;

    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const uint STATUS_BUFFER_OVERFLOW = 0x80000005;
    private const uint STATUS_BUFFER_TOO_SMALL = 0xC0000023;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr buffer, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint access, bool inherit, uint options);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static void Close(IntPtr handle) => CloseHandle(handle);

    public static IntPtr OpenForDuplicate(int pid) =>
        OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

    /// <summary>All handle values owned by the given processes (one system-wide query).</summary>
    public static Dictionary<int, List<IntPtr>> QueryHandles(ICollection<int> pids)
    {
        var wanted = new HashSet<long>(pids.Select(p => (long)p));
        var result = new Dictionary<int, List<IntPtr>>();

        int size = 0x100000;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            while (true)
            {
                int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out int needed);
                if ((uint)status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    size = Math.Max(size * 2, needed + 0x10000);
                    if (size > 256 * 1024 * 1024)
                        throw new InvalidOperationException("Windows reported an unexpectedly large handle table.");
                    buffer = Marshal.ReAllocHGlobal(buffer, (IntPtr)size);
                    continue;
                }
                if (status < 0)
                    throw new InvalidOperationException($"NtQuerySystemInformation failed (0x{(uint)status:X8}).");
                break;
            }

            int ptr = IntPtr.Size;
            int header = ptr * 2;               // NumberOfHandles + Reserved
            int entrySize = ptr * 3 + 16;       // Object, Pid, Handle, then 16 bytes of fields
            long count = Marshal.ReadIntPtr(buffer).ToInt64();
            long max = (size - header) / entrySize;
            if (count > max) count = max;

            for (long i = 0; i < count; i++)
            {
                var entry = IntPtr.Add(buffer, (int)(header + i * entrySize));
                long pid = Marshal.ReadIntPtr(entry, ptr).ToInt64();
                if (!wanted.Contains(pid)) continue;

                var handle = Marshal.ReadIntPtr(entry, ptr * 2);
                if (!result.TryGetValue((int)pid, out var list))
                    result[(int)pid] = list = new List<IntPtr>();
                list.Add(handle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    /// <summary>
    /// Type name of a handle in another process, plus its object name when the type matches
    /// nameOnlyForType (name queries can block on unrelated handle types).
    /// </summary>
    public static (string Type, string Name) Describe(IntPtr sourceProcess, IntPtr handle, string nameOnlyForType)
    {
        if (!DuplicateHandle(sourceProcess, handle, GetCurrentProcess(), out var dup,
                0, false, DUPLICATE_SAME_ACCESS))
            return ("", "");

        try
        {
            var type = QueryUnicode(dup, ObjectTypeInformation);
            if (!type.Equals(nameOnlyForType, StringComparison.OrdinalIgnoreCase)) return (type, "");
            return (type, QueryUnicode(dup, ObjectNameInformation));
        }
        finally
        {
            CloseHandle(dup);
        }
    }

    /// <summary>Closes a handle inside the other process.</summary>
    public static bool CloseRemote(IntPtr sourceProcess, IntPtr handle)
    {
        if (!DuplicateHandle(sourceProcess, handle, GetCurrentProcess(), out var dup,
                0, false, DUPLICATE_SAME_ACCESS | DUPLICATE_CLOSE_SOURCE))
            return false;
        CloseHandle(dup);
        return true;
    }

    private static string QueryUnicode(IntPtr handle, int infoClass)
    {
        int size = 0x400;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryObject(handle, infoClass, buffer, size, out int needed);
                if (status >= 0)
                {
                    // Both results start with a UNICODE_STRING: Length, MaximumLength, then the Buffer pointer.
                    int length = (ushort)Marshal.ReadInt16(buffer, 0);
                    var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    if (text == IntPtr.Zero || length == 0) return "";
                    return Marshal.PtrToStringUni(text, length / 2) ?? "";
                }

                var u = (uint)status;
                if (u == STATUS_INFO_LENGTH_MISMATCH || u == STATUS_BUFFER_OVERFLOW || u == STATUS_BUFFER_TOO_SMALL)
                {
                    size = Math.Max(size * 2, needed + 0x100);
                    continue;
                }
                return "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return "";
    }
}
