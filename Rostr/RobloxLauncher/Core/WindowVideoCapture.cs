using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using WinRT;

namespace RobloxLauncher.Core;

internal sealed class WindowVideoCapture : IDisposable
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid DxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private const uint D3D11SdkVersion = 7;

    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private readonly Channel<Direct3D11CaptureFrame> _frames = Channel.CreateUnbounded<Direct3D11CaptureFrame>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Stream? _output;
    private Task? _frameWorker;
    private Exception? _captureError;
    private long _lastFrameTimestamp;
    private readonly TimeSpan _frameInterval;
    private int _queuedFrames;
    private bool _started;
    private bool _stopped;

    public int Width { get; }
    public int Height { get; }

    public WindowVideoCapture(IntPtr window, int framesPerSecond = 30)
    {
        if (framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        _frameInterval = TimeSpan.FromSeconds(1.0 / framesPerSecond);
        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows Graphics Capture requires Windows 10 version 1903 or later.");
        if (window == IntPtr.Zero || !IsWindow(window))
            throw new ArgumentException("The selected window is no longer available.", nameof(window));

        IDirect3DDevice? winrtDevice = null;
        GraphicsCaptureItem? item = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        IntPtr d3dDevice = IntPtr.Zero;
        IntPtr d3dContext = IntPtr.Zero;
        try
        {
            CheckResult(D3D11CreateDevice(
                IntPtr.Zero,
                1,
                IntPtr.Zero,
                0x20,
                IntPtr.Zero,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out d3dContext), "Could not create a Direct3D capture device.");
            winrtDevice = CreateDirect3DDevice(d3dDevice);
            item = CreateItemForWindow(window);
            Width = item.Size.Width;
            Height = item.Size.Height;
            if (Width < 2 || Height < 2)
                throw new InvalidOperationException("Windows returned an empty selected-window capture surface.");

            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = true;
            _winrtDevice = winrtDevice;
            _item = item;
            _framePool = framePool;
            _session = session;
            winrtDevice = null;
            item = null;
            framePool = null;
            session = null;
        }
        catch
        {
            session?.Dispose();
            framePool?.Dispose();
            winrtDevice?.Dispose();
            throw;
        }
        finally
        {
            if (d3dContext != IntPtr.Zero)
                Marshal.Release(d3dContext);
            if (d3dDevice != IntPtr.Zero)
                Marshal.Release(d3dDevice);
        }
    }

    public void Start(Stream output)
    {
        ObjectDisposedException.ThrowIf(_stopped, this);
        if (_started)
            throw new InvalidOperationException("Window video capture has already started.");

        _output = output;
        _framePool.FrameArrived += OnFrameArrived;
        _frameWorker = ProcessFramesAsync();
        _started = true;
        _session.StartCapture();
    }

    public async Task StopAsync()
    {
        if (_stopped)
            return;
        _stopped = true;

        if (_started)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _framePool.Dispose();
            _frames.Writer.TryComplete();
            if (_frameWorker is not null)
                await _frameWorker.ConfigureAwait(false);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_output is not null)
                    await _output.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        else
        {
            _session.Dispose();
            _framePool.Dispose();
        }

        _winrtDevice.Dispose();

        if (_captureError is not null)
            throw new InvalidOperationException("Could not capture the selected window.", _captureError);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = null;
        try
        {
            frame = sender.TryGetNextFrame();
            if (frame is null)
                return;
            if (Interlocked.Increment(ref _queuedFrames) > 2 || !_frames.Writer.TryWrite(frame))
            {
                Interlocked.Decrement(ref _queuedFrames);
                frame.Dispose();
                return;
            }
            frame = null;
        }
        catch (Exception exception)
        {
            _captureError = exception;
            _frames.Writer.TryComplete(exception);
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private async Task ProcessFramesAsync()
    {
        try
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using (frame)
                {
                    Interlocked.Decrement(ref _queuedFrames);
                    var now = Stopwatch.GetTimestamp();
                    if (_lastFrameTimestamp != 0 && Stopwatch.GetElapsedTime(_lastFrameTimestamp, now) < _frameInterval)
                        continue;
                    _lastFrameTimestamp = now;

                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                    using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    if (converted.PixelWidth != Width || converted.PixelHeight != Height)
                        throw new InvalidOperationException("The selected window changed size during recording. Stop and restart the recording after resizing the window.");

                    var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(Width * Height * 4)));
                    converted.CopyToBuffer(buffer);
                    CryptographicBuffer.CopyToByteArray(buffer, out var pixels);

                    await _writeLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_output is not null)
                            await _output.WriteAsync(pixels).ConfigureAwait(false);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _captureError = exception;
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice(IntPtr d3dDevice)
    {
        IntPtr dxgiDevice = IntPtr.Zero;
        IntPtr graphicsDevice = IntPtr.Zero;
        try
        {
            CheckResult(Marshal.QueryInterface(d3dDevice, in DxgiDeviceGuid, out dxgiDevice), "Could not access the Direct3D DXGI device.");
            CheckResult(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out graphicsDevice), "Could not create the Windows Graphics Capture device.");
            var device = MarshalInspectable<IDirect3DDevice>.FromAbi(graphicsDevice);
            graphicsDevice = IntPtr.Zero;
            return device;
        }
        finally
        {
            if (graphicsDevice != IntPtr.Zero)
                Marshal.Release(graphicsDevice);
            if (dxgiDevice != IntPtr.Zero)
                Marshal.Release(dxgiDevice);
        }
    }

    private static GraphicsCaptureItem CreateItemForWindow(IntPtr window)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        IntPtr interopPointer = IntPtr.Zero;
        IntPtr itemPointer = IntPtr.Zero;
        try
        {
            CheckResult(Marshal.QueryInterface(factory.ThisPtr, in GraphicsCaptureItemInteropGuid, out interopPointer), "Could not access window-capture support.");
            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(interopPointer);
            CheckResult(interop.CreateForWindow(window, in GraphicsCaptureItemGuid, out itemPointer), "Windows could not create a capture item for this window.");
            var item = MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPointer);
            itemPointer = IntPtr.Zero;
            return item;
        }
        finally
        {
            if (itemPointer != IntPtr.Zero)
                Marshal.Release(itemPointer);
            if (interopPointer != IntPtr.Zero)
                Marshal.Release(interopPointer);
        }
    }

    private static void CheckResult(int result, string message)
    {
        if (result < 0)
            throw new InvalidOperationException($"{message} (HRESULT 0x{result:X8})", Marshal.GetExceptionForHR(result));
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _writeLock.Dispose();
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out uint featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr window, in Guid interfaceId, out IntPtr result);
        [PreserveSig] int CreateForMonitor(IntPtr monitor, in Guid interfaceId, out IntPtr result);
    }
}
