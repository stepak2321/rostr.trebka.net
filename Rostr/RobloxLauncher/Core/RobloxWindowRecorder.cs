using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RobloxLauncher.Core;

/// <summary>Records a Roblox window with Windows Graphics Capture and that process's audio.</summary>
public sealed class RobloxWindowRecorder
{
    private Process? _videoProcess;
    private ProcessAudioCapture? _audioCapture;
    private WindowVideoCapture? _windowCapture;
    private StringBuilder? _ffmpegError;
    private string? _ffmpegPath;
    private string? _outputPath;
    private string? _videoTempPath;
    private string? _audioTempPath;
    private bool _segments;

    public bool IsRecording => _videoProcess is { HasExited: false };

    public bool Start(string ffmpeg, IntPtr hwnd, string output, RecordOptions options, out string error)
    {
        error = "";
        if (_videoProcess is not null)
        {
            error = "A recording is already in progress.";
            return false;
        }

        try
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                throw new ArgumentException("The Roblox window is no longer available.", nameof(hwnd));
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
                throw new InvalidOperationException("Could not identify the Roblox window process.");
            using (var robloxProcess = Process.GetProcessById(checked((int)processId)))
            {
                if (!string.Equals(robloxProcess.ProcessName, "RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected window does not belong to Roblox Player.");
            }

            _outputPath = output;
            _ffmpegPath = ffmpeg;
            _segments = options.Segments;
            _windowCapture = new WindowVideoCapture(hwnd, options.Fps);
            if (options.Audio)
            {
                if (_segments)
                    _audioCapture = new ProcessAudioCapture();
                else
                {
                    _audioTempPath = Path.Combine(Path.GetTempPath(), $"rostr-audio-{Guid.NewGuid():N}.wav");
                    _audioCapture = new ProcessAudioCapture(_audioTempPath);
                }
                _audioCapture.Start(checked((int)processId));
            }

            var videoOutput = output;
            if (!_segments)
            {
                _videoTempPath = Path.Combine(Path.GetTempPath(), $"rostr-video-{Guid.NewGuid():N}.mkv");
                videoOutput = _videoTempPath;
            }

            var startInfo = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "rawvideo", "-pixel_format", "bgra",
                "-video_size", $"{_windowCapture.Width}x{_windowCapture.Height}",
                "-framerate", options.Fps.ToString(CultureInfo.InvariantCulture),
                "-i", "pipe:0"
            })
                startInfo.ArgumentList.Add(argument);

            if (_segments && _audioCapture is { } audio)
            {
                foreach (var argument in new[]
                {
                    "-thread_queue_size", "1024", "-f", audio.Format,
                    "-ar", audio.SampleRate.ToString(CultureInfo.InvariantCulture),
                    "-ac", audio.Channels.ToString(CultureInfo.InvariantCulture),
                    "-i", audio.PipePath
                })
                    startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:v:0");
            if (_segments && _audioCapture is not null)
            {
                startInfo.ArgumentList.Add("-map");
                startInfo.ArgumentList.Add("1:a:0");
            }
            AddArguments(startInfo,
                "-c:v", "libx264",
                "-preset", options.Segments ? "superfast" : "ultrafast",
                "-crf", options.Crf.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p");
            if (_segments && _audioCapture is not null)
                AddArguments(startInfo, "-c:a", "aac", "-b:a", "160k", "-ac", "2");
            else
                startInfo.ArgumentList.Add("-an");

            if (options.Segments)
            {
                AddArguments(startInfo,
                    "-force_key_frames", "expr:gte(t,n_forced*1)",
                    "-sc_threshold", "0",
                    "-f", "segment", "-segment_time", "1",
                    "-segment_format", "mpegts", videoOutput);
            }
            else
            {
                AddArguments(startInfo, "-f", "matroska", videoOutput);
            }

            var errorText = new StringBuilder();
            _ffmpegError = errorText;
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    lock (errorText)
                        errorText.AppendLine(eventArgs.Data);
                }
            };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("FFmpeg did not start.");
            }
            _videoProcess = process;
            ChildJob.Add(process);
            process.BeginErrorReadLine();
            _windowCapture.Start(process.StandardInput.BaseStream);
            if (process.WaitForExit(700))
            {
                process.WaitForExit();
                throw new InvalidOperationException("FFmpeg exited immediately. " + GetFfmpegError());
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            try { Abort(); }
            catch (Exception cleanupException) { error += Environment.NewLine + "Cleanup error: " + cleanupException.Message; }
            try { CleanupTemporaryFiles(); }
            catch (Exception cleanupException) { error += Environment.NewLine + "Temporary-file cleanup error: " + cleanupException.Message; }
            if (!_segments && _outputPath is not null && File.Exists(_outputPath))
            {
                try { File.Delete(_outputPath); }
                catch (Exception cleanupException) { error += Environment.NewLine + "Output cleanup error: " + cleanupException.Message; }
            }
            Reset();
            return false;
        }
    }

    public string? Stop(int timeoutMs = 10000) => StopAsync(timeoutMs).GetAwaiter().GetResult();

    public Task<string?> StopAsync(int timeoutMs = 10000) => Task.Run(() => StopCore(timeoutMs));

    private string? StopCore(int timeoutMs)
    {
        Exception? recordingError = null;
        try
        {
            if (_windowCapture is not null)
            {
                try { _windowCapture.StopAsync().GetAwaiter().GetResult(); }
                catch (Exception exception) { recordingError = exception; }
                finally
                {
                    _windowCapture.Dispose();
                    _windowCapture = null;
                }
            }

            try { _audioCapture?.Stop(); }
            catch (Exception exception)
            {
                recordingError = recordingError is null ? exception : new AggregateException(recordingError, exception);
            }
            finally
            {
                try { _audioCapture?.Dispose(); }
                catch (Exception exception)
                {
                    recordingError = recordingError is null ? exception : new AggregateException(recordingError, exception);
                }
                _audioCapture = null;
            }

            var process = _videoProcess;
            _videoProcess = null;
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.StandardInput.Close();
                        if (!process.WaitForExit(timeoutMs))
                        {
                            process.Kill(true);
                            process.WaitForExit();
                        }
                    }
                    if (process.ExitCode != 0)
                    {
                        var ffmpegFailure = new InvalidOperationException(
                            "FFmpeg could not capture the Roblox Player window. " + GetFfmpegError());
                        recordingError = recordingError is null ? ffmpegFailure : new AggregateException(recordingError, ffmpegFailure);
                    }
                }
                catch (Exception exception)
                {
                    recordingError = recordingError is null ? exception : new AggregateException(recordingError, exception);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(true);
                            process.WaitForExit();
                        }
                        catch (Exception exception)
                        {
                            recordingError = recordingError is null ? exception : new AggregateException(recordingError, exception);
                        }
                    }
                    process.Dispose();
                }
            }

            if (recordingError is not null)
            {
                CleanupTemporaryFiles();
                if (!_segments && _outputPath is not null && File.Exists(_outputPath))
                    File.Delete(_outputPath);
                throw recordingError;
            }

            var output = _outputPath;
            if (!_segments)
                output = FinalizeRecording();
            return output;
        }
        finally
        {
            Reset();
        }
    }

    private string GetFfmpegError()
    {
        var text = _ffmpegError;
        if (text is null) return "";
        lock (text)
            return text.ToString().Trim();
    }

    private string? FinalizeRecording()
    {
        if (_outputPath is null || _videoTempPath is null ||
            !File.Exists(_videoTempPath) || new FileInfo(_videoTempPath).Length == 0)
        {
            CleanupTemporaryFiles();
            return null;
        }

        var startInfo = new ProcessStartInfo(_ffmpegPath
            ?? throw new InvalidOperationException("FFmpeg path was not saved for finalizing the recording."))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        AddArguments(startInfo, "-hide_banner", "-loglevel", "error", "-y", "-i", _videoTempPath);
        if (_audioTempPath is not null)
            AddArguments(startInfo, "-i", _audioTempPath, "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest");
        else
            AddArguments(startInfo, "-c:v", "copy", "-an");
        AddArguments(startInfo, "-movflags", "+faststart", _outputPath);

        using var muxProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not start the final mux operation.");
        var errorTask = muxProcess.StandardError.ReadToEndAsync();
        if (!muxProcess.WaitForExit(60000))
        {
            muxProcess.Kill(true);
            muxProcess.WaitForExit();
            throw new TimeoutException("FFmpeg took too long to finalize the recording.");
        }
        var error = errorTask.GetAwaiter().GetResult();
        if (muxProcess.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg could not combine the recording's video and audio. " + error.Trim());

        var savedPath = _outputPath;
        CleanupTemporaryFiles();
        return File.Exists(savedPath) && new FileInfo(savedPath).Length > 0 ? savedPath : null;
    }

    private void CleanupTemporaryFiles()
    {
        foreach (var path in new[] { _videoTempPath, _audioTempPath })
        {
            if (path is not null && File.Exists(path))
                File.Delete(path);
        }
        _videoTempPath = null;
        _audioTempPath = null;
    }

    private void Abort()
    {
        if (_windowCapture is not null)
        {
            try { _windowCapture.Dispose(); }
            finally { _windowCapture = null; }
        }
        if (_audioCapture is not null)
        {
            try { _audioCapture.Dispose(); }
            finally { _audioCapture = null; }
        }
        if (_videoProcess is not null)
        {
            if (!_videoProcess.HasExited)
            {
                _videoProcess.Kill(true);
                _videoProcess.WaitForExit();
            }
            _videoProcess.Dispose();
            _videoProcess = null;
        }
    }

    private void Reset()
    {
        _outputPath = null;
        _videoTempPath = null;
        _audioTempPath = null;
        _ffmpegPath = null;
        _ffmpegError = null;
        _segments = false;
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
