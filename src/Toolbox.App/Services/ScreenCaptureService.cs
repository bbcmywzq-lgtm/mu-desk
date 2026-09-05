using System.Diagnostics;
using System.IO;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using PersonalToolbox.Native;
using Toolbox.Core;

namespace PersonalToolbox.Services;

public sealed record EffectCaptureRegion(
    IntPtr MonitorHandle,
    int MonitorLeft,
    int MonitorTop,
    int X,
    int Y,
    int Width,
    int Height);

public sealed record RawCaptureFrame(
    int Index,
    long TimestampMilliseconds,
    long FileOffset,
    double MotionScore,
    double EdgeChangeScore);

public sealed class RawCaptureResult
{
    public required string RawPath { get; init; }

    public required int LogicalWidth { get; init; }

    public required int LogicalHeight { get; init; }

    public required int EncodedWidth { get; init; }

    public required int EncodedHeight { get; init; }

    public required IReadOnlyList<RawCaptureFrame> Frames { get; init; }

    public required long DurationMilliseconds { get; init; }

    public double AverageFramesPerSecond =>
        DurationMilliseconds <= 0 ? 0 : Frames.Count * 1000d / DurationMilliseconds;
}

public sealed class ScreenCaptureService
{
    public async Task<RawCaptureResult> CaptureAsync(
        EffectCaptureRegion region,
        TimeSpan duration,
        string rawPath,
        IProgress<TimeSpan>? progress,
        CancellationToken cancellationToken,
        CancellationToken stopToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(region.Width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(region.Height, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        var encodedWidth = region.Width + (region.Width & 1);
        var encodedHeight = region.Height + (region.Height & 1);
        var frameBytes = checked(encodedWidth * encodedHeight * 4);
        var frames = new List<RawCaptureFrame>();
        var writeGate = new SemaphoreSlim(1, 1);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFrameReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? firstFrameElapsed = null;
        byte[]? previousSignature = null;
        var accepting = true;
        Exception? failure = null;

        await using var rawStream = new FileStream(
            rawPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var device = CaptureInterop.CreateDirect3DDevice();
        var item = CaptureInterop.CreateItemForMonitor(region.MonitorHandle);
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = true;
        framePool.FrameArrived += OnFrameArrived;

        using var cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        using var stopRegistration = stopToken.Register(() => completion.TrySetResult());
        session.StartCapture();
        try
        {
            var timerTask = RunTimerAsync();
            await completion.Task;
            await timerTask;
        }
        finally
        {
            accepting = false;
            framePool.FrameArrived -= OnFrameArrived;
            await writeGate.WaitAsync(CancellationToken.None);
            writeGate.Release();
            await rawStream.FlushAsync(CancellationToken.None);
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("屏幕帧处理失败。", failure);
        }

        if (frames.Count == 0)
        {
            throw new InvalidOperationException("录制期间没有取得可用的屏幕帧。");
        }

        var measuredDuration = stopToken.IsCancellationRequested
            ? Math.Max(1, frames[^1].TimestampMilliseconds)
            : Math.Max(frames[^1].TimestampMilliseconds, (long)duration.TotalMilliseconds);
        return new RawCaptureResult
        {
            RawPath = rawPath,
            LogicalWidth = region.Width,
            LogicalHeight = region.Height,
            EncodedWidth = encodedWidth,
            EncodedHeight = encodedHeight,
            Frames = frames.ToArray(),
            DurationMilliseconds = measuredDuration,
        };

        async Task RunTimerAsync()
        {
            try
            {
                var firstOrCompleted = await Task.WhenAny(firstFrameReady.Task, completion.Task);
                if (firstOrCompleted == completion.Task)
                {
                    return;
                }

                await firstFrameReady.Task;
                while (stopwatch.Elapsed - firstFrameElapsed!.Value < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var captured = stopwatch.Elapsed - firstFrameElapsed.Value;
                    progress?.Report(captured);
                    var remaining = duration - captured;
                    var delay = remaining < TimeSpan.FromMilliseconds(50)
                        ? remaining
                        : TimeSpan.FromMilliseconds(50);
                    await Task.Delay(
                        delay < TimeSpan.FromMilliseconds(5) ? TimeSpan.FromMilliseconds(5) : delay,
                        cancellationToken);
                }

                progress?.Report(duration);
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
        }

        async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (!accepting || !await writeGate.WaitAsync(0))
            {
                using var dropped = sender.TryGetNextFrame();
                return;
            }

            try
            {
                using var frame = sender.TryGetNextFrame();
                var arrivalElapsed = stopwatch.Elapsed;
                if (frame is null ||
                    firstFrameElapsed is not null && arrivalElapsed - firstFrameElapsed.Value > duration + TimeSpan.FromMilliseconds(100))
                {
                    return;
                }

                if (firstFrameElapsed is null)
                {
                    firstFrameElapsed = arrivalElapsed;
                    firstFrameReady.TrySetResult();
                }

                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                var monitorPixels = CaptureInterop.CopyBitmapPixels(bitmap);
                var relativeX = region.X - region.MonitorLeft;
                var relativeY = region.Y - region.MonitorTop;
                if (relativeX < 0 || relativeY < 0 ||
                    relativeX + region.Width > bitmap.PixelWidth ||
                    relativeY + region.Height > bitmap.PixelHeight)
                {
                    throw new InvalidOperationException("选区已经超出当前显示器的可捕获范围。");
                }

                var cropped = new byte[frameBytes];
                var sourceStride = bitmap.PixelWidth * 4;
                var targetStride = encodedWidth * 4;
                var logicalStride = region.Width * 4;
                for (var row = 0; row < region.Height; row++)
                {
                    Buffer.BlockCopy(
                        monitorPixels,
                        ((relativeY + row) * sourceStride) + (relativeX * 4),
                        cropped,
                        row * targetStride,
                        logicalStride);
                    if (encodedWidth != region.Width)
                    {
                        var target = (row * targetStride) + logicalStride;
                        Buffer.BlockCopy(cropped, target - 4, cropped, target, 4);
                    }
                }

                if (encodedHeight != region.Height)
                {
                    Buffer.BlockCopy(
                        cropped,
                        (region.Height - 1) * targetStride,
                        cropped,
                        region.Height * targetStride,
                        targetStride);
                }

                var signature = BuildSignature(cropped, encodedWidth, encodedHeight);
                var (motion, edge) = CompareSignatures(previousSignature, signature);
                previousSignature = signature;
                var fileOffset = rawStream.Position;
                await rawStream.WriteAsync(cropped, cancellationToken);
                frames.Add(new RawCaptureFrame(
                    frames.Count,
                    Math.Max(0, (long)(arrivalElapsed - firstFrameElapsed.Value).TotalMilliseconds),
                    fileOffset,
                    motion,
                    edge));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
                completion.TrySetResult();
            }
            finally
            {
                writeGate.Release();
            }
        }
    }

    private static byte[] BuildSignature(byte[] pixels, int width, int height)
    {
        const int sampleWidth = 32;
        const int sampleHeight = 18;
        var signature = new byte[sampleWidth * sampleHeight];
        var stride = width * 4;
        for (var sy = 0; sy < sampleHeight; sy++)
        {
            var y = Math.Min(height - 1, sy * height / sampleHeight);
            for (var sx = 0; sx < sampleWidth; sx++)
            {
                var x = Math.Min(width - 1, sx * width / sampleWidth);
                var offset = (y * stride) + (x * 4);
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                signature[(sy * sampleWidth) + sx] = (byte)((red * 54 + green * 183 + blue * 19) >> 8);
            }
        }

        return signature;
    }

    private static (double Motion, double Edge) CompareSignatures(byte[]? previous, byte[] current)
    {
        if (previous is null || previous.Length != current.Length)
        {
            return (0, 0);
        }

        long motion = 0;
        long edge = 0;
        const int width = 32;
        for (var index = 0; index < current.Length; index++)
        {
            motion += Math.Abs(current[index] - previous[index]);
            if (index % width != 0)
            {
                var currentEdge = Math.Abs(current[index] - current[index - 1]);
                var previousEdge = Math.Abs(previous[index] - previous[index - 1]);
                edge += Math.Abs(currentEdge - previousEdge);
            }
        }

        return (
            motion / (double)current.Length,
            edge / (double)(current.Length - (current.Length / width)));
    }
}
