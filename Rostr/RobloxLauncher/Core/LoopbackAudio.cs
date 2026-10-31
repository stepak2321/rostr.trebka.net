using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using NAudio.Wave;

namespace RobloxLauncher.Core;

/// <summary>
/// Records what the PC plays through the default speakers or headphones (WASAPI loopback) and feeds it to ffmpeg
/// through a named pipe as raw samples. Windows only delivers loopback data while something is playing, so the quiet
/// stretches are filled with silence by the clock; that keeps the sound in step with the video.
/// This is the sound of the whole PC, not only of Roblox.
/// </summary>
public sealed class LoopbackAudio : IDisposable
{
    private readonly string _name = "rostr-audio-" + Guid.NewGuid().ToString("N");
    private readonly ConcurrentQueue<byte[]> _queue = new();
    private WasapiLoopbackCapture? _cap;
    private NamedPipeServerStream? _pipe;
    private Thread? _thread;
    private volatile bool _stop;
    private bool _disposed;
    private int _blockAlign = 8, _bytesPerSec = 384000;

    public string PipePath => @"\\.\pipe\" + _name;
    public string Format { get; private set; } = "f32le";
    public int SampleRate { get; private set; } = 48000;
    public int Channels { get; private set; } = 2;

    /// <summary>Null (with the reason in error) when there is no sound device to record from.</summary>
    public static LoopbackAudio? TryCreate(out string error)
    {
        error = "";
        var a = new LoopbackAudio();
        try
        {
            a.Prepare();
            return a;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            a.Dispose();
            return null;
        }
    }

    private void Prepare()
    {
        _cap = new WasapiLoopbackCapture();   // the default output device
        var wf = _cap.WaveFormat;
        SampleRate = wf.SampleRate;
        Channels = wf.Channels;
        _blockAlign = Math.Max(1, wf.BlockAlign);
        _bytesPerSec = Math.Max(1, wf.AverageBytesPerSecond);
        // The shared-mode mix format of Windows is 32-bit float in practice; 16 and 24 bit are handled too.
        Format = wf.BitsPerSample switch { 16 => "s16le", 24 => "s24le", _ => "f32le" };
        _pipe = new NamedPipeServerStream(_name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                                          PipeOptions.Asynchronous, 0, 1 << 20);
    }

    /// <summary>Call right after ffmpeg started: waits for ffmpeg to open the pipe, then starts feeding it.</summary>
    public void Begin()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Rostr game sound" };
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            var pipe = _pipe;
            var cap = _cap;
            if (pipe == null || cap == null) return;

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                pipe.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
            if (_stop) return;

            cap.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;
                var copy = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
                _queue.Enqueue(copy);
            };
            cap.StartRecording();

            var clock = Stopwatch.StartNew();
            long written = 0;
            while (!_stop)
            {
                Thread.Sleep(15);
                while (_queue.TryDequeue(out var chunk))
                {
                    pipe.Write(chunk, 0, chunk.Length);
                    written += chunk.Length;
                }

                // More than 100 ms behind the clock: nothing is playing. Write silence so the track keeps its length.
                var expected = (long)(clock.Elapsed.TotalSeconds * _bytesPerSec) / _blockAlign * _blockAlign;
                var gap = expected - written;
                if (gap > _bytesPerSec / 10)
                {
                    gap = gap / _blockAlign * _blockAlign;
                    while (gap > 0 && !_stop)
                    {
                        var n = (int)Math.Min(gap, _bytesPerSec);
                        pipe.Write(new byte[n], 0, n);
                        written += n;
                        gap -= n;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // ffmpeg closed the pipe (it was stopped) or never opened it.
            if (!_stop) Log.Info("Game sound feed ended: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop = true;
        try { _cap?.StopRecording(); } catch { }
        try { _pipe?.Dispose(); } catch { }   // also wakes a feed that is waiting on ffmpeg
        try { _thread?.Join(1500); } catch { }
        try { _cap?.Dispose(); } catch { }
    }
}
