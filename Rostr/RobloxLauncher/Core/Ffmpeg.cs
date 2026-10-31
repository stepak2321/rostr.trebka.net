using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Net.Http;
using Microsoft.Win32;

namespace RobloxLauncher.Core;

/// <summary>
/// Video recording uses ffmpeg. Rostr looks for it in its own data folder, next to Rostr.exe and on PATH,
/// and can download it once (free, from gyan.dev) when the user first presses Record.
/// </summary>
public static class Ffmpeg
{
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rostr", "ffmpeg");

    public static string? Find()
    {
        var own = Path.Combine(DataDir, "ffmpeg.exe");
        if (File.Exists(own)) return own;

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir))
        {
            var beside = Path.Combine(exeDir, "ffmpeg.exe");
            if (File.Exists(beside) && IsUsable(beside)) return beside;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var f = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(f) && IsUsable(f)) return f;
            }
            catch { }
        }
        return null;
    }

    // An ffmpeg found on the PATH can be very old (no -hide_banner, no screen capture) or a stripped build.
    // Those are skipped, so Rostr offers its own download instead of failing to record.
    private static readonly Dictionary<string, bool> UsableCache = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsUsable(string exe)
    {
        lock (UsableCache)
            if (UsableCache.TryGetValue(exe, out var known)) return known;

        var ok = Probe(exe, "-formats", "gdigrab") && Probe(exe, "-encoders", "libx264");
        if (!ok) Log.Warn($"Ignoring {exe}: it is too old or has no screen capture / H.264 encoder.");
        lock (UsableCache) UsableCache[exe] = ok;
        return ok;
    }

    internal static bool Probe(string exe, string args, string needle)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(true); } catch { }
                return false;
            }
            return output.Wait(3000) && output.Result.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static async Task DownloadAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(DataDir);
        var zip = Path.Combine(DataDir, "ffmpeg.zip.part");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Rostr");
            using var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(zip))
            {
                var buf = new byte[81920];
                long done = 0;
                int last = -1, n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    var pct = total > 0 ? (int)(done * 100 / total) : (int)(done / 1_000_000);
                    if (pct != last)
                    {
                        last = pct;
                        progress?.Report(total > 0 ? $"Downloading ffmpeg... {pct}%" : $"Downloading ffmpeg... {pct} MB");
                    }
                }
            }

            progress?.Report("Unpacking ffmpeg...");
            using (var za = ZipFile.OpenRead(zip))
            {
                var entry = za.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new InvalidDataException("ffmpeg.exe was not in the download.");
                entry.ExtractToFile(Path.Combine(DataDir, "ffmpeg.exe"), true);
            }
            Log.Info("ffmpeg downloaded");
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
        }
    }

    /// <summary>One still picture from a video, used as its tile in the Media page.</summary>
    public static async Task MakeThumbAsync(string ffmpeg, string video, string thumb)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            foreach (var seek in new[] { "0.5", "0" })
            {
                var psi = new ProcessStartInfo(ffmpeg)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    Arguments = $"-y -loglevel error -ss {seek} -i \"{video}\" -frames:v 1 -vf scale=480:-2 -q:v 4 \"{thumb}\""
                };
                using var p = Process.Start(psi);
                if (p == null) return;
                _ = p.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await p.WaitForExitAsync(cts.Token); }
                catch { try { p.Kill(true); } catch { } }
                if (File.Exists(thumb) && new FileInfo(thumb).Length > 0) return;
            }
        }
        catch (Exception ex) { Log.Warn("Video thumbnail: " + ex.Message); }
    }

    /// <summary>
    /// Joins the finished instant-replay segments (short .ts files in workDir) into one mp4 without re-encoding.
    /// </summary>
    public static bool Concat(string ffmpeg, string workDir, IReadOnlyList<string> segmentNames, string output)
    {
        var list = Path.Combine(workDir, "concat-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            // File names are relative to workDir and made by Rostr itself, so they hold nothing that needs escaping.
            File.WriteAllLines(list, segmentNames.Select(n => "file '" + n + "'"), new UTF8Encoding(false));
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = workDir,
                Arguments = $"-y -loglevel error -f concat -safe 0 -i \"{Path.GetFileName(list)}\" -c copy -movflags +faststart \"{output}\"",
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60000))
            {
                try { p.Kill(true); } catch { }
                Log.Warn("Instant replay: joining the segments took too long.");
                return false;
            }
            if (p.ExitCode != 0)
            {
                Log.Warn("Instant replay: ffmpeg " + (err.Wait(1000) ? err.Result.Trim() : "failed"));
                return false;
            }
            return File.Exists(output) && new FileInfo(output).Length > 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Instant replay: " + ex.Message);
            return false;
        }
        finally
        {
            try { File.Delete(list); } catch { }
        }
    }
}

/// <summary>How a recording (or the instant-replay buffer) is encoded. Comes from Settings > Gallery.</summary>
public sealed record RecordOptions(int Fps, int Crf, bool Audio, bool Segments)
{
    /// <summary>0 = low, 1 = medium, 2 = high.</summary>
    public static int CrfFor(int quality) => quality switch { 0 => 28, 2 => 18, _ => 23 };
}

/// <summary>
/// Makes ffmpeg children die with Rostr. Without it, an ffmpeg that is recording in the background (the instant
/// replay buffer) would keep running after a crash or a killed Rostr.
/// </summary>
internal static class ChildJob
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private static IntPtr _job;
    private static bool _failed;

    public static void Add(Process p)
    {
        try
        {
            if (_failed) return;
            if (_job == IntPtr.Zero)
            {
                var job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) { _failed = true; return; }
                var info = new ExtendedLimit();
                info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                var size = Marshal.SizeOf<ExtendedLimit>();
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(job, 9, ptr, (uint)size)) { _failed = true; return; }   // 9 = extended limits
                }
                finally { Marshal.FreeHGlobal(ptr); }
                _job = job;
            }
            AssignProcessToJobObject(_job, p.Handle);
        }
        catch (Exception ex) { Log.Warn("ffmpeg job: " + ex.Message); }
    }
}

/// <summary>Legacy desktop capture implementation. Gallery recordings and replay use RobloxWindowRecorder.</summary>
public sealed class ScreenRecorder
{
    private Process? _p;
    private LoopbackAudio? _audio;

    public string? OutputFile { get; private set; }
    public bool IsRecording => _p is { HasExited: false };

    /// <summary>True when this recording carries the game sound.</summary>
    public bool HasAudio => _audio != null;

    /// <summary>Why the sound was left out, when it was asked for but could not be recorded.</summary>
    public string AudioError { get; private set; } = "";

    /// <summary>
    /// output is the mp4 file, or (opt.Segments) a segment pattern such as C:\...\s1_%05d.ts.
    /// </summary>
    public bool Start(string ffmpeg, IntPtr hwnd, int x, int y, int w, int h, string output, RecordOptions opt, out string error)
    {
        error = "";
        AudioError = "";
        LoopbackAudio? audio = null;
        try
        {
            w &= ~1; h &= ~1;   // the encoder needs even sizes
            EnsureDpiAware(ffmpeg);

            // Newer ffmpeg can record one window (its game area) instead of a part of the desktop, so nothing
            // else on the screen can end up in the video. Older builds fall back to the screen rectangle.
            var byWindow = hwnd != IntPtr.Zero && Ffmpeg.Probe(ffmpeg, "-h demuxer=gdigrab", "hwnd");
            var source = byWindow
                ? $"-i hwnd=0x{hwnd.ToInt64():X} "
                : $"-offset_x {x} -offset_y {y} -video_size {w}x{h} -i desktop ";
            var filter = byWindow ? "-vf crop=trunc(iw/2)*2:trunc(ih/2)*2 " : "";
            Log.Info($"{(opt.Segments ? "Instant replay" : "Recording")} {(byWindow ? "the Roblox window" : "a screen rectangle")}: " +
                     $"hwnd 0x{hwnd.ToInt64():X}, game area {w}x{h} at {x},{y}, {opt.Fps} FPS, crf {opt.Crf}, sound {(opt.Audio ? "on" : "off")}, ffmpeg {ffmpeg}");

            // The game sound arrives through a named pipe, as a second input. (stdin is kept for the "q" that stops ffmpeg.)
            if (opt.Audio)
            {
                audio = LoopbackAudio.TryCreate(out var audioError);
                if (audio == null)
                {
                    AudioError = audioError;
                    Log.Warn("Game sound is not recorded: " + audioError);
                }
            }

            var sb = new StringBuilder("-y -loglevel error ");
            sb.Append($"-f gdigrab -framerate {opt.Fps} -draw_mouse 0 ").Append(source);
            if (audio != null)
                sb.Append($"-thread_queue_size 1024 -f {audio.Format} -ar {audio.SampleRate} -ac {audio.Channels} -i {audio.PipePath} ");
            sb.Append(filter);
            if (audio != null) sb.Append("-map 0:v:0 -map 1:a:0 ");
            sb.Append($"-c:v libx264 -preset {(opt.Segments ? "superfast" : "veryfast")} -crf {opt.Crf} -pix_fmt yuv420p ");
            sb.Append(audio != null ? "-c:a aac -b:a 160k -ac 2 " : "-an ");
            if (opt.Segments)
            {
                // A keyframe every second, so every segment can start a clip by itself.
                sb.Append("-force_key_frames expr:gte(t,n_forced*1) -sc_threshold 0 ");
                sb.Append($"-f segment -segment_time 1 -segment_format mpegts \"{output}\"");
            }
            else
            {
                sb.Append($"-movflags +faststart \"{output}\"");
            }

            var psi = new ProcessStartInfo(ffmpeg, sb.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };
            var p = Process.Start(psi);
            if (p == null) { error = "ffmpeg did not start."; audio?.Dispose(); return false; }
            ChildJob.Add(p);
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Warn("ffmpeg: " + e.Data); };
            p.BeginErrorReadLine();
            audio?.Begin();

            // If ffmpeg dies right away the arguments were refused (bad region, no encoder...).
            if (p.WaitForExit(700))
            {
                error = "ffmpeg stopped right after starting. See the log file.";
                p.Dispose();
                audio?.Dispose();
                return false;
            }

            _p = p;
            _audio = audio;
            OutputFile = output;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Start recording", ex);
            audio?.Dispose();
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Asks ffmpeg to finish the file, then waits for it. Returns the file, or null when nothing was saved.</summary>
    public string? Stop(int timeoutMs = 10000)
    {
        var p = _p;
        var audio = _audio;
        _p = null;
        _audio = null;
        if (p == null) { audio?.Dispose(); return null; }
        try
        {
            if (!p.HasExited)
            {
                p.StandardInput.Write("q");
                p.StandardInput.Flush();
            }
        }
        catch { }

        try
        {
            if (!p.WaitForExit(timeoutMs)) p.Kill(true);
        }
        catch { }
        p.Dispose();
        audio?.Dispose();   // after ffmpeg is gone, so the sound pipe is never closed under a running ffmpeg

        var file = OutputFile;
        return file != null && File.Exists(file) && new FileInfo(file).Length > 0 ? file : null;
    }

    public Task<string?> StopAsync() => Task.Run(() => Stop());

    // ffmpeg.exe is not DPI aware, so on a scaled display its screen coordinates would not match ours.
    // Windows has a per-program compatibility flag for that; it is set for this one exe, for the current user.
    private static void EnsureDpiAware(string exe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
            var cur = key?.GetValue(exe) as string ?? "";
            if (cur.Contains("HIGHDPIAWARE", StringComparison.OrdinalIgnoreCase)) return;
            key?.SetValue(exe, cur.Length == 0 ? "~ HIGHDPIAWARE" : cur + " HIGHDPIAWARE", RegistryValueKind.String);
        }
        catch (Exception ex) { Log.Warn("DPI flag for ffmpeg: " + ex.Message); }
    }
}
