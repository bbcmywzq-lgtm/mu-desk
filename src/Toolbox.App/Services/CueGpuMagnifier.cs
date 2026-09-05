using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PersonalToolbox.Native;
using Toolbox.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Forms = System.Windows.Forms;

namespace PersonalToolbox.Services;

/// <summary>WGC -> persistent D3D11 texture -> shader -> composition swap chain.
/// No pixel readback, GDI bitmap, WPF image or UI dispatcher on the frame path.</summary>
public sealed class CueGpuMagnifier : IDisposable
{
    private readonly CueSettings _settings;
    private readonly Thread _thread;
    private readonly ManualResetEvent _stop = new(false);
    private readonly object _frameGate = new();
    private Direct3D11CaptureFrame? _latest;
    private Direct3D11CaptureFramePool? _currentPool;
    private volatile bool _disposed;
    private long _captureCount;
    private long _presentCount;
    private readonly List<double> _intervals = [];
    private readonly List<double> _frameAge = [];
    private readonly List<double> _drawTime = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string? _failure;
    private bool _fastCapture;
    private bool _clickThrough;
    internal string? DiagnosticSnapshotPath { get; set; }
    internal bool DiagnosticFullScreen { get; set; }
    private bool _snapshotTaken;
    private readonly CueCameraMotion _probeCamera = new();
    public event Action<Exception>? Error;
    public string? Failure => _failure;
    public CueGpuMagnifier(CueSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "Cue GPU presentation" };
        _thread.SetApartmentState(ApartmentState.STA);
    }
    public void Start() => _thread.Start();
    public object Statistics => new
    {
        Backend = "WGC / D3D11 / DirectComposition",
        CaptureFrames = Interlocked.Read(ref _captureCount),
        PresentedFrames = Interlocked.Read(ref _presentCount),
        Seconds = _clock.Elapsed.TotalSeconds,
        PresentInterval = Summary(_intervals),
        FrameAgeAtSubmit = Summary(_frameAge),
        DrawCpuMilliseconds = Summary(_drawTime),
        Failure = _failure,
        FastCaptureIntervalSupported = _fastCapture,
        ClickThrough = _clickThrough,
    };
    private static object Summary(List<double> samples)
    {
        double[] values;
        lock (samples) values = samples.Order().ToArray();
        return new { Count = values.Length, Median = values.Length == 0 ? 0 : values[values.Length / 2],
            P95 = values.Length == 0 ? 0 : values[(int)((values.Length - 1) * .95)],
            Max = values.Length == 0 ? 0 : values[^1] };
    }
    private static void Record(List<double> samples, double value)
    {
        lock (samples) { if (samples.Count == 4096) samples.RemoveAt(0); samples.Add(value); }
    }

    private void Run()
    {
        try
        {
            // All window, immediate-context and presentation operations have one owner.
            using var window = new LensWindow();
            Vortice.Direct3D11.D3D11.D3D11CreateDevice(null, DriverType.Hardware,
                DeviceCreationFlags.BgraSupport, Array.Empty<FeatureLevel>(), out ID3D11Device device,
                out ID3D11DeviceContext context).CheckError();
            using (device)
            using (context)
            using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            using (var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<IDXGIFactory2>())
            using (var composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice))
            using (var target = CreateTarget(composition, window.Handle))
            using (var visual = composition.CreateVisual())
            using (var winrtDevice = WrapDevice(dxgiDevice.NativePointer))
            {
                target.SetRoot(visual).CheckError();
                var vertexCode = Compiler.Compile(Shader, "VS", "CueLens", "vs_5_0");
                var pixelCode = Compiler.Compile(Shader, "PS", "CueLens", "ps_5_0");
                using var vertex = device.CreateVertexShader(vertexCode.Span);
                using var pixel = device.CreatePixelShader(pixelCode.Span);
                using var constants = device.CreateBuffer(64, BindFlags.ConstantBuffer);
                using var sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                context.VSSetShader(vertex);
                context.PSSetShader(pixel);
                context.PSSetConstantBuffer(0, constants);
                context.PSSetSampler(0, sampler);

                while (!_stop.WaitOne(0))
                {
                    var cursor = Forms.Cursor.Position;
                    var monitor = CaptureInterop.MonitorFromPhysicalPoint(cursor.X, cursor.Y);
                    var bounds = Forms.Screen.FromPoint(cursor).Bounds;
                    var item = CaptureInterop.CreateItemForMonitor(monitor);
                    var width = item.Size.Width;
                    var height = item.Size.Height;
                    var desc = new SwapChainDescription1
                    {
                        Width = (uint)width, Height = (uint)height,
                        Format = Format.B8G8R8A8_UNorm, BufferCount = 2,
                        BufferUsage = Usage.RenderTargetOutput,
                        SampleDescription = new SampleDescription(1, 0),
                        SwapEffect = SwapEffect.FlipSequential,
                        Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Premultiplied,
                        Flags = SwapChainFlags.FrameLatencyWaitableObject,
                    };
                    using var swap = factory.CreateSwapChainForComposition(device, desc);
                    using var swap2 = swap.QueryInterface<IDXGISwapChain2>();
                    swap2.MaximumFrameLatency = 1;
                    using var ready = new EventWaitHandle(false, EventResetMode.AutoReset);
                    ready.SafeWaitHandle = new SafeWaitHandle(swap2.FrameLatencyWaitableObject, false);
                    using var back = swap.GetBuffer<ID3D11Texture2D>(0);
                    using var renderTarget = device.CreateRenderTargetView(back);
                    using var desktop = device.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
                    });
                    using var desktopView = device.CreateShaderResourceView(desktop);
                    context.OMSetRenderTargets(renderTarget);
                    context.RSSetViewport(0, 0, width, height);
                    visual.SetContent(swap).CheckError();
                    composition.Commit().CheckError();
                    window.Place(bounds);
                    using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                    using var session = pool.CreateCaptureSession(item);
                    session.IsCursorCaptureEnabled = false;
                    _fastCapture = ConfigureFastCapture(session);
                    // Border suppression requires a separate OS capability. Keep the
                    // standard capture indication unless that capability was granted.
                    pool.FrameArrived += OnFrame;
                    lock (_frameGate) _currentPool = pool;
                    session.StartCapture();
                    var hasImage = false;
                    var lastPresent = 0d;
                    try
                    {
                        while (!_stop.WaitOne(0))
                        {
                            window.Pump();
                            if (WaitHandle.WaitAny([_stop, ready], 100) == 0) break;
                            cursor = Forms.Cursor.Position;
                            if (monitor != CaptureInterop.MonitorFromPhysicalPoint(cursor.X, cursor.Y)) break;
                            var start = Stopwatch.GetTimestamp();
                            Direct3D11CaptureFrame? frame;
                            lock (_frameGate) { frame = _latest; _latest = null; }
                            if (frame is not null)
                            {
                                using (frame)
                                {
                                    if (frame.ContentSize.Width != width || frame.ContentSize.Height != height) break;
                                    using var source = GetTexture(frame.Surface);
                                    context.PSSetShaderResource(0, null!);
                                    context.CopyResource(desktop, source);
                                    hasImage = true;
                                    Record(_frameAge, Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency - frame.SystemRelativeTime.TotalMilliseconds);
                                }
                            }
                            if (!hasImage) { _stop.WaitOne(2); continue; }
                            var radius = (float)_settings.MagnifierRadius * window.DpiScale;
                            var rectangular = _settings.MagnifierShape == "RoundedRectangle";
                            var data = new Parameters
                            {
                                Lens = new Vector4(cursor.X - bounds.Left, cursor.Y - bounds.Top,
                                    radius * (rectangular ? 1.35f : 1), radius * (rectangular ? .72f : 1)),
                                Screen = new Vector4(width, height, (float)_settings.MagnifierZoom, rectangular ? 1 : 0),
                                Extra = new Vector4(20 * window.DpiScale, radius * .22f, DiagnosticFullScreen ? 1 : 0, 0),
                            };
                            if (DiagnosticFullScreen)
                            {
                                if (_clock.Elapsed.TotalSeconds < 3)
                                    _probeCamera.Aim(2, -(cursor.X - bounds.Left), -(cursor.Y - bounds.Top));
                                else _probeCamera.Aim(1, 0, 0);
                                _probeCamera.Step(lastPresent == 0 ? .006 : (_clock.Elapsed.TotalMilliseconds - lastPresent) / 1000, 220);
                            }
                            data.Camera = new Vector4((float)_probeCamera.Zoom, (float)_probeCamera.X, (float)_probeCamera.Y, 0);
                            // Flip-model Present unbinds the backbuffer from the
                            // pipeline. Bind again on EVERY frame, not only at setup.
                            context.OMSetRenderTargets(renderTarget);
                            context.UpdateSubresource(in data, constants);
                            context.PSSetShaderResource(0, desktopView);
                            context.Draw(3, 0);
                            Record(_drawTime, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                            if (!_snapshotTaken && DiagnosticSnapshotPath is not null && _clock.Elapsed.TotalSeconds > 2)
                            {
                                SaveDiagnosticFrame(device, context, back, DiagnosticSnapshotPath);
                                _snapshotTaken = true;
                            }
                            swap.Present(0, PresentFlags.None).CheckError();
                            window.Show();
                            _clickThrough = window.IsClickThrough(cursor);
                            var now = _clock.Elapsed.TotalMilliseconds;
                            if (lastPresent > 0) Record(_intervals, now - lastPresent);
                            lastPresent = now;
                            Interlocked.Increment(ref _presentCount);
                        }
                    }
                    finally
                    {
                        pool.FrameArrived -= OnFrame;
                        lock (_frameGate) { _currentPool = null; _latest?.Dispose(); _latest = null; }
                        session.Dispose();
                        window.Hide();
                        context.PSSetShaderResource(0, null!);
                        context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>());
                        visual.SetContent(null).CheckError();
                        composition.Commit().CheckError();
                    }
                }
                context.ClearState();
            }
        }
        catch (Exception exception)
        {
            _failure = exception.ToString();
            Error?.Invoke(exception);
        }
    }

    private void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            lock (_frameGate)
            {
                if (_disposed || !ReferenceEquals(sender, _currentPool)) return;
                var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                _latest?.Dispose();
                _latest = frame;
                Interlocked.Increment(ref _captureCount);
            }
        }
        catch (Exception exception)
        {
            _failure = exception.ToString();
            _stop.Set();
            Error?.Invoke(exception);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join();
        lock (_frameGate) { _latest?.Dispose(); _latest = null; }
        _clock.Stop();
        _stop.Dispose();
    }

    private static IDirect3DDevice WrapDevice(nint dxgi)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var pointer));
        try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }
    private static IDCompositionTarget CreateTarget(IDCompositionDevice device, nint hwnd)
    {
        device.CreateTargetForHwnd(hwnd, true, out var target).CheckError();
        return target;
    }
    private static bool ConfigureFastCapture(GraphicsCaptureSession session)
    {
        using var marshaler = WinRT.MarshalInspectable<GraphicsCaptureSession>.CreateMarshaler(session);
        var iid = typeof(ICaptureSession5).GUID;
        if (Marshal.QueryInterface(marshaler.ThisPtr, in iid, out var pointer) < 0) return false;
        var api = (ICaptureSession5)Marshal.GetObjectForIUnknown(pointer);
        try { Marshal.ThrowExceptionForHR(api.SetMinUpdateInterval(TimeSpan.FromMilliseconds(2).Ticks)); return true; }
        finally { Marshal.ReleaseComObject(api); Marshal.Release(pointer); }
    }
    // Older Windows retains the default capture interval. QueryInterface keeps
    // Windows 10 compatibility without loading newer WinRT contracts at startup.
    [ComImport, Guid("67c0ea62-1f85-5061-925a-239be0ac09cb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureSession5
    {
        [PreserveSig] int GetIids(out uint count, out nint iids);
        [PreserveSig] int GetRuntimeClassName(out nint name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int GetMinUpdateInterval(out long ticks);
        [PreserveSig] int SetMinUpdateInterval(long ticks);
    }
    private static void SaveDiagnosticFrame(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D texture, string path)
    {
        // Explicit probe only. Production rendering never reads pixels back.
        var desc = texture.Description;
        desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read; desc.MiscFlags = ResourceOptionFlags.None;
        using var staging = device.CreateTexture2D(desc);
        context.CopyResource(staging, texture);
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var stride = (int)desc.Width * 4;
            var pixels = new byte[stride * desc.Height];
            for (var y = 0; y < desc.Height; y++)
                Marshal.Copy(mapped.DataPointer + (int)(y * mapped.RowPitch), pixels, y * stride, stride);
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create((int)desc.Width, (int)desc.Height,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, pixels, stride);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(path);
            encoder.Save(file);
        }
        finally { context.Unmap(staging, 0); }
    }
    private static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        using var marshaler = WinRT.MarshalInterface<IDirect3DSurface>.CreateMarshaler(surface);
        var accessId = typeof(IDirect3DDxgiInterfaceAccess).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(marshaler.ThisPtr, in accessId, out var pointer));
        var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            var textureId = typeof(ID3D11Texture2D).GUID;
            Marshal.ThrowExceptionForHR(access.GetInterface(in textureId, out var texture));
            return new ID3D11Texture2D(texture);
        }
        finally { Marshal.ReleaseComObject(access); Marshal.Release(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters { public Vector4 Lens, Screen, Extra, Camera; }
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(in Guid iid, out nint result);
    }
    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);

    private sealed class LensWindow : Forms.NativeWindow, IDisposable
    {
        private bool _shown;
        public float DpiScale => GetDpiForWindow(Handle) / 96f;
        public LensWindow()
        {
            CreateHandle(new Forms.CreateParams
            {
                Caption = "Mujun Cue GPU lens", Style = unchecked((int)0x80000000),
                ExStyle = 0x00200000 | 0x00080000 | 0x00000020 | 0x08000000 | 0x80 | 0x8,
                Width = 1, Height = 1,
            });
            SetLayeredWindowAttributes(Handle, 0, 255, 2);
            if (!CaptureInterop.ExcludeWindowFromCapture(Handle))
            {
                DestroyHandle();
                throw new InvalidOperationException("无法排除放大镜自身，已停止以避免画面递归。");
            }
        }
        public void Place(System.Drawing.Rectangle bounds) =>
            SetWindowPos(Handle, new nint(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x10);
        public void Show() { if (!_shown) { ShowWindow(Handle, 8); _shown = true; } }
        public void Hide() { ShowWindow(Handle, 0); _shown = false; }
        public bool IsClickThrough(System.Drawing.Point point) => WindowFromPoint(point) != Handle;
        public void Pump()
        {
            while (PeekMessage(out var message, 0, 0, 0, 1))
            { TranslateMessage(in message); DispatchMessage(in message); }
        }
        protected override void WndProc(ref Forms.Message m)
        {
            if (m.Msg == 0x84) { m.Result = -1; return; }
            if (m.Msg == 0x21) { m.Result = 3; return; }
            base.WndProc(ref m);
        }
        public void Dispose() => DestroyHandle();
        [StructLayout(LayoutKind.Sequential)] private struct NativeMessage
        { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
        [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, nint hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(in NativeMessage message);
        [DllImport("user32.dll")] private static extern nint DispatchMessage(in NativeMessage message);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
        [DllImport("user32.dll")] private static extern nint WindowFromPoint(System.Drawing.Point point);
    }

    private const string Shader = """
        cbuffer Constants : register(b0) { float4 lens; float4 screen; float4 extra; float4 camera; }
        Texture2D desktop : register(t0); SamplerState linearSampler : register(s0);
        float4 VS(uint id : SV_VertexID) : SV_Position {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4(uv * float2(2,-2) + float2(-1,1), 0, 1);
        }
        float4 PS(float4 p : SV_Position) : SV_Target {
            if (extra.z > .5) return float4(desktop.Sample(linearSampler, (p.xy - camera.yz) / camera.x / screen.xy).rgb, 1);
            float2 d = p.xy - lens.xy;
            float distance;
            if(screen.w < .5) distance = (length(d / lens.zw) - 1) * min(lens.z,lens.w);
            else {
                float2 q = abs(d) - lens.zw + extra.x;
                distance = length(max(q,0)) + min(max(q.x,q.y),0) - extra.x;
            }
            // Wide feather, zero first/second derivatives at both ends. Keep
            // the inner image sharp and blend out without a colored outline.
            float t = saturate(-distance / max(extra.y, 1));
            float coverage = t * t * t * (t * (t * 6 - 15) + 10);
            if (coverage == 0) return 0;
            float2 uv = (lens.xy + d / screen.z) / screen.xy;
            float4 image = desktop.Sample(linearSampler, uv);
            return float4(image.rgb * coverage, coverage);
        }
        """;
}
