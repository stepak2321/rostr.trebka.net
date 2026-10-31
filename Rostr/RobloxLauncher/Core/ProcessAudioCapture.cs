using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

internal sealed class ProcessAudioCapture : IDisposable
{
    private const uint LoopbackStreamFlag = 0x00020000;
    private const uint SilentBufferFlag = 0x00000002;
    private readonly string _path;
    private IAudioClientHandle? _audioClient;
    private IAudioCaptureClientHandle? _captureClient;
    private FileStream? _file;
    private NamedPipeServerStream? _pipe;
    private readonly string? _pipeName;
    private Thread? _thread;
    private volatile bool _stopping;
    private bool _stopped;
    private Exception? _captureError;
    private WaveFormatEx _format;
    private byte[]? _formatExtra;
    private long _dataBytes;

    public ProcessAudioCapture(string path) => _path = path;
    public ProcessAudioCapture()
    {
        _path = "";
        _pipeName = "rostr-process-audio-" + Guid.NewGuid().ToString("N");
    }

    public string PipePath => _pipeName is null ? "" : @"\\.\pipe\" + _pipeName;
    public string Format { get; private set; } = "";
    public int SampleRate => checked((int)_format.SamplesPerSecond);
    public int Channels => _format.Channels;

    public void Start(int processId)
    {
        if (Environment.OSVersion.Version.Build < 19041)
            throw new PlatformNotSupportedException("Per-application audio capture requires Windows 10 version 2004 (build 19041) or later.");

        var formatSource = CreateDefaultAudioFormatSource();
        IAudioClientHandle processClient;
        try
        {
            processClient = CreateProcessAudioClient(processId);
        }
        catch
        {
            formatSource.Dispose();
            throw;
        }

        Start(processClient, formatSource);
    }

    private void Start(IAudioClientHandle client, IAudioClientHandle? formatSource = null)
    {
        _audioClient = client;
        formatSource ??= client;
        try
        {
            CheckResult(formatSource.GetMixFormat(out var formatPointer), "Could not read the audio format.");
            try
            {
                _format = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);
                if (_format.Channels == 0 || _format.BlockAlign == 0)
                    throw new InvalidOperationException("Windows returned an unsupported audio format.");
                if (_format.ExtraSize > 0)
                {
                    _formatExtra = new byte[_format.ExtraSize];
                    Marshal.Copy(IntPtr.Add(formatPointer, Marshal.SizeOf<WaveFormatEx>()), _formatExtra, 0, _formatExtra.Length);
                }
                if (_pipeName is not null)
                    Format = GetRawFormat();

                CheckResult(client.Initialize(0, LoopbackStreamFlag, 10_000_000, 0, formatPointer, IntPtr.Zero), "Could not initialize audio capture.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(formatPointer);
            }

            var captureClientId = typeof(IAudioCaptureClientNative).GUID;
            CheckResult(client.GetService(ref captureClientId, out var captureClient), "Could not start the audio capture interface.");
            _captureClient = captureClient;
            if (_pipeName is not null)
                _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 1 << 20);
            else
            {
                _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                WriteWaveHeader();
            }
            CheckResult(client.Start(), "Could not start audio capture.");
            _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Audio capture" };
            _thread.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (!ReferenceEquals(formatSource, client))
                formatSource.Dispose();
        }
    }

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        _stopping = true;
        _pipe?.Dispose();
        _thread?.Join();

        Exception? stopError = null;
        if (_audioClient is not null)
        {
            try { CheckResult(_audioClient.Stop(), "Could not stop audio capture."); }
            catch (Exception exception) { stopError = exception; }
        }

        try
        {
            if (_file is not null)
                FinalizeWaveHeader();
        }
        finally
        {
            _file?.Dispose();
            _file = null;
        }

        if (_captureError is not null)
            stopError = stopError is null
                ? new InvalidOperationException("Audio capture failed.", _captureError)
                : new AggregateException(stopError, _captureError);
        if (stopError is not null)
            throw stopError;
    }

    private void CaptureLoop()
    {
        try
        {
            if (_pipe is not null)
                _pipe.WaitForConnection();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!_stopping)
            {
                CheckResult(_captureClient!.GetNextPacketSize(out var packetFrames), "Could not read the audio packet.");
                if (packetFrames == 0)
                {
                    Thread.Sleep(8);
                }

                while (packetFrames > 0)
                {
                    CheckResult(_captureClient.GetBuffer(out var data, out var frames, out var flags, out _, out _), "Could not retrieve captured audio.");
                    try
                    {
                        var length = checked((int)(frames * _format.BlockAlign));
                        if ((flags & SilentBufferFlag) != 0)
                        {
                            WriteSilence(length);
                        }
                        else
                        {
                            var buffer = new byte[length];
                            Marshal.Copy(data, buffer, 0, length);
                            WriteAudio(buffer);
                            _dataBytes += length;
                        }
                    }
                    finally
                    {
                        CheckResult(_captureClient.ReleaseBuffer(frames), "Could not release the audio packet.");
                    }

                    CheckResult(_captureClient.GetNextPacketSize(out packetFrames), "Could not read the next audio packet.");
                }

                if (_pipe is not null && _format.AverageBytesPerSecond > 0)
                {
                    var expected = (long)(clock.Elapsed.TotalSeconds * _format.AverageBytesPerSecond) / _format.BlockAlign * _format.BlockAlign;
                    var gap = expected - _dataBytes;
                    if (gap > _format.AverageBytesPerSecond / 10)
                    {
                        gap = gap / _format.BlockAlign * _format.BlockAlign;
                        while (gap > 0 && !_stopping)
                        {
                            var length = (int)Math.Min(gap, _format.AverageBytesPerSecond);
                            WriteSilence(length);
                            gap -= length;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (!_stopping)
                _captureError = exception;
            _stopping = true;
        }
    }

    private void WriteAudio(byte[] buffer)
    {
        if (_pipe is not null)
            _pipe.Write(buffer);
        else
            _file!.Write(buffer);
    }

    private void WriteSilence(int length)
    {
        var silence = new byte[Math.Min(length, 16_384)];
        while (length > 0)
        {
            var count = Math.Min(length, silence.Length);
            if (_pipe is not null)
                _pipe.Write(silence, 0, count);
            else
                _file!.Write(silence, 0, count);
            _dataBytes += count;
            length -= count;
        }
    }

    private string GetRawFormat()
    {
        var formatTag = _format.FormatTag;
        if (formatTag == 0xFFFE)
        {
            if (_formatExtra is not { Length: >= 22 })
                throw new InvalidOperationException("Windows returned an incomplete extensible audio format.");

            var subFormat = new Guid(_formatExtra.AsSpan(6, 16));
            if (subFormat == new Guid("00000003-0000-0010-8000-00AA00389B71"))
                formatTag = 3;
            else if (subFormat == new Guid("00000001-0000-0010-8000-00AA00389B71"))
                formatTag = 1;
            else
                throw new InvalidOperationException($"Windows returned an unsupported extensible audio subtype ({subFormat}).");
        }

        if (formatTag == 3)
            return _format.BitsPerSample switch
            {
                32 => "f32le",
                64 => "f64le",
                _ => throw new InvalidOperationException($"Windows returned an unsupported floating-point audio depth ({_format.BitsPerSample}).")
            };
        if (formatTag != 1)
            throw new InvalidOperationException($"Windows returned an unsupported process audio format (tag {formatTag}).");

        return _format.BitsPerSample switch
        {
            8 => "u8",
            16 => "s16le",
            24 => "s24le",
            32 => "s32le",
            _ => throw new InvalidOperationException($"Windows returned an unsupported PCM bit depth ({_format.BitsPerSample}).")
        };
    }

    private void WriteWaveHeader()
    {
        var writer = new BinaryWriter(_file!, System.Text.Encoding.ASCII, leaveOpen: true);
        var formatChunkSize = _format.ExtraSize == 0 ? 16 : 18 + _format.ExtraSize;
        writer.Write("RIFF"u8);
        writer.Write(0u);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write((uint)formatChunkSize);
        writer.Write(_format.FormatTag);
        writer.Write(_format.Channels);
        writer.Write(_format.SamplesPerSecond);
        writer.Write(_format.AverageBytesPerSecond);
        writer.Write(_format.BlockAlign);
        writer.Write(_format.BitsPerSample);
        if (_format.ExtraSize > 0)
        {
            writer.Write(_format.ExtraSize);
            writer.Write(_formatExtra!);
        }
        writer.Write("data"u8);
        writer.Write(0u);
    }

    private void FinalizeWaveHeader()
    {
        _file!.Flush();
        var writer = new BinaryWriter(_file, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Seek(4, SeekOrigin.Begin);
        writer.Write(checked((uint)(_file.Length - 8)));
        writer.Seek(40 + (_format.ExtraSize == 0 ? 0 : _format.ExtraSize + 2), SeekOrigin.Begin);
        writer.Write(checked((uint)_dataBytes));
        _file.Flush();
    }

    private static IAudioClientHandle CreateDefaultAudioFormatSource()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            CheckResult(enumerator.GetDefaultAudioEndpoint(0, 0, out var device), "Could not find the default audio output device.");
            var audioClientId = typeof(IAudioClientNative).GUID;
            CheckResult(device.Activate(ref audioClientId, 23, IntPtr.Zero, out var audioObject), "Could not read the default output audio format.");
            return new ComAudioClient((IAudioClientNative)audioObject);
        }
        finally
        {
            ReleaseComObject(enumerator);
        }
    }

    private static IAudioClientHandle CreateProcessAudioClient(int processId)
    {
        var parameters = new AudioClientActivationParams
        {
            ActivationType = 1,
            ProcessLoopback = new ProcessLoopbackParams
            {
                TargetProcessId = checked((uint)processId),
                ProcessLoopbackMode = 0
            }
        };
        var parameterMemory = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
        var variantMemory = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(parameters, parameterMemory, false);
            Marshal.StructureToPtr(new PropVariant
            {
                Type = 65,
                BlobSize = (uint)Marshal.SizeOf<AudioClientActivationParams>(),
                BlobData = parameterMemory
            }, variantMemory, false);

            var completion = new ActivationCompletionHandler();
            var interfaceId = typeof(IAudioClientNative).GUID;
            CheckResult(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref interfaceId, variantMemory, completion, out var operation), "Could not activate application audio capture.");
            completion.Wait();
            CheckResult(operation.GetActivateResult(out var activationResult, out var activatedPointer), "Audio activation did not complete.");
            CheckResult(activationResult, "Could not capture audio from the selected application.");
            if (activatedPointer == IntPtr.Zero)
                throw new InvalidOperationException("Windows returned an empty application audio interface.");
            return new RawAudioClient(activatedPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(variantMemory);
            Marshal.FreeHGlobal(parameterMemory);
        }
    }

    private static void CheckResult(int result, string message)
    {
        if (result < 0)
        {
            var nativeError = Marshal.GetExceptionForHR(result);
            throw new InvalidOperationException(
                $"{message} (HRESULT 0x{result:X8}{(nativeError is null ? string.Empty : $": {nativeError.Message}")})",
                nativeError);
        }
    }

    private static void ReleaseComObject(object instance)
    {
        if (Marshal.IsComObject(instance))
            Marshal.ReleaseComObject(instance);
    }

    public void Dispose()
    {
        try
        {
            if (_thread is not null && !_stopped)
                Stop();
        }
        finally
        {
            if (_file is not null)
            {
                _file.Dispose();
                _file = null;
            }
            _captureClient?.Dispose();
            _audioClient?.Dispose();
            _captureClient = null;
            _audioClient = null;
        }
    }

    private interface IAudioClientHandle : IDisposable
    {
        int GetMixFormat(out IntPtr format);
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        int Start();
        int Stop();
        int GetService(ref Guid interfaceId, out IAudioCaptureClientHandle service);
    }

    private interface IAudioCaptureClientHandle : IDisposable
    {
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        int ReleaseBuffer(uint frames);
        int GetNextPacketSize(out uint frames);
    }

    private sealed class ComAudioClient(IAudioClientNative client) : IAudioClientHandle
    {
        public int GetMixFormat(out IntPtr format) => client.GetMixFormat(out format);
        public int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid)
            => client.Initialize(shareMode, streamFlags, bufferDuration, periodicity, format, audioSessionGuid);
        public int Start() => client.Start();
        public int Stop() => client.Stop();

        public int GetService(ref Guid interfaceId, out IAudioCaptureClientHandle service)
        {
            var result = client.GetService(ref interfaceId, out var nativeService);
            service = result >= 0 ? new ComAudioCaptureClient((IAudioCaptureClientNative)nativeService) : null!;
            return result;
        }

        public void Dispose() => ReleaseComObject(client);
    }

    private sealed class ComAudioCaptureClient(IAudioCaptureClientNative client) : IAudioCaptureClientHandle
    {
        public int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition)
            => client.GetBuffer(out data, out frames, out flags, out devicePosition, out qpcPosition);
        public int ReleaseBuffer(uint frames) => client.ReleaseBuffer(frames);
        public int GetNextPacketSize(out uint frames) => client.GetNextPacketSize(out frames);
        public void Dispose() => ReleaseComObject(client);
    }

    private sealed class RawAudioClient : IAudioClientHandle
    {
        private IntPtr _pointer;

        public RawAudioClient(IntPtr pointer) => _pointer = pointer;

        public int GetMixFormat(out IntPtr format)
            => Marshal.GetDelegateForFunctionPointer<GetMixFormatDelegate>(GetVtableSlot(_pointer, 8))(_pointer, out format);
        public int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid)
            => Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(GetVtableSlot(_pointer, 3))(_pointer, shareMode, streamFlags, bufferDuration, periodicity, format, audioSessionGuid);
        public int Start() => Marshal.GetDelegateForFunctionPointer<NoArgsDelegate>(GetVtableSlot(_pointer, 10))(_pointer);
        public int Stop() => Marshal.GetDelegateForFunctionPointer<NoArgsDelegate>(GetVtableSlot(_pointer, 11))(_pointer);

        public int GetService(ref Guid interfaceId, out IAudioCaptureClientHandle service)
        {
            var result = Marshal.GetDelegateForFunctionPointer<GetServiceDelegate>(GetVtableSlot(_pointer, 14))(_pointer, ref interfaceId, out var capturePointer);
            service = result >= 0 ? new RawAudioCaptureClient(capturePointer) : null!;
            return result;
        }

        public void Dispose()
        {
            if (_pointer != IntPtr.Zero)
            {
                Marshal.Release(_pointer);
                _pointer = IntPtr.Zero;
            }
        }
    }

    private sealed class RawAudioCaptureClient(IntPtr pointer) : IAudioCaptureClientHandle
    {
        private IntPtr _pointer = pointer;

        public int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition)
            => Marshal.GetDelegateForFunctionPointer<GetCaptureBufferDelegate>(GetVtableSlot(_pointer, 3))(_pointer, out data, out frames, out flags, out devicePosition, out qpcPosition);
        public int ReleaseBuffer(uint frames)
            => Marshal.GetDelegateForFunctionPointer<ReleaseCaptureBufferDelegate>(GetVtableSlot(_pointer, 4))(_pointer, frames);
        public int GetNextPacketSize(out uint frames)
            => Marshal.GetDelegateForFunctionPointer<GetNextPacketSizeDelegate>(GetVtableSlot(_pointer, 5))(_pointer, out frames);

        public void Dispose()
        {
            if (_pointer != IntPtr.Zero)
            {
                Marshal.Release(_pointer);
                _pointer = IntPtr.Zero;
            }
        }
    }

    private static IntPtr GetVtableSlot(IntPtr instance, int index)
        => Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetMixFormatDelegate(IntPtr self, out IntPtr format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeDelegate(IntPtr self, int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NoArgsDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetServiceDelegate(IntPtr self, ref Guid interfaceId, out IntPtr service);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCaptureBufferDelegate(IntPtr self, out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseCaptureBufferDelegate(IntPtr self, uint frames);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetNextPacketSizeDelegate(IntPtr self, out uint frames);

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid interfaceId,
        IntPtr activationParams,
        [MarshalAs(UnmanagedType.Interface)] IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessLoopbackParams
    {
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public int ActivationType;
        public ProcessLoopbackParams ProcessLoopback;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint BlobSize;
        [FieldOffset(16)] public IntPtr BlobData;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), ClassInterface(ClassInterfaceType.None)]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClientNative
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferSize);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClientNative
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activationResult, out IntPtr activatedInterface);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEvent _completed = new(false);
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            _completed.Set();
            return 0;
        }
        public void Wait()
        {
            if (!_completed.WaitOne(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Timed out while starting per-application audio capture.");
            _completed.Dispose();
        }
    }
}
