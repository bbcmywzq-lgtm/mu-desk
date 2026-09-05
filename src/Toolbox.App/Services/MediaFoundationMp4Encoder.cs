using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Toolbox.Core;

namespace PersonalToolbox.Services;

public sealed class MediaFoundationMp4Encoder
{
    public async Task EncodeAsync(
        RawCaptureResult capture,
        string outputPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (capture.Frames.Count == 0)
        {
            throw new ArgumentException("没有可编码的录制帧。", nameof(capture));
        }

        var frameBytes = checked(capture.EncodedWidth * capture.EncodedHeight * 4);
        using var rawStream = new FileStream(
            capture.RawPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        var properties = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8,
            (uint)capture.EncodedWidth,
            (uint)capture.EncodedHeight);
        properties.FrameRate.Numerator = 60;
        properties.FrameRate.Denominator = 1;
        var descriptor = new VideoStreamDescriptor(properties);
        var source = new MediaStreamSource(descriptor) { BufferTime = TimeSpan.Zero };
        var nextFrame = 0;
        var streamGate = new object();

        source.Starting += (_, args) =>
        {
            lock (streamGate)
            {
                rawStream.Position = 0;
                nextFrame = 0;
            }

            args.Request.SetActualStartPosition(TimeSpan.Zero);
        };
        source.SampleRequested += (_, args) =>
        {
            lock (streamGate)
            {
                if (cancellationToken.IsCancellationRequested || nextFrame >= capture.Frames.Count)
                {
                    args.Request.Sample = null;
                    return;
                }

                var frame = capture.Frames[nextFrame];
                rawStream.Position = frame.FileOffset;
                var pixels = new byte[frameBytes];
                rawStream.ReadExactly(pixels);
                var timestamp = TimeSpan.FromMilliseconds(frame.TimestampMilliseconds);
                var duration = nextFrame + 1 < capture.Frames.Count
                    ? TimeSpan.FromMilliseconds(Math.Max(
                        1,
                        capture.Frames[nextFrame + 1].TimestampMilliseconds - frame.TimestampMilliseconds))
                    : TimeSpan.FromMilliseconds(Math.Max(
                        1,
                        capture.DurationMilliseconds - frame.TimestampMilliseconds));
                var sample = MediaStreamSample.CreateFromBuffer(pixels.AsBuffer(), timestamp);
                sample.Duration = duration;
                args.Request.Sample = sample;
                nextFrame++;
                progress?.Report(nextFrame / (double)capture.Frames.Count);
            }
        };

        var profile = new MediaEncodingProfile();
        profile.Container.Subtype = MediaEncodingSubtypes.Mpeg4;
        profile.Video.Subtype = MediaEncodingSubtypes.H264;
        profile.Video.Width = (uint)capture.EncodedWidth;
        profile.Video.Height = (uint)capture.EncodedHeight;
        profile.Video.Bitrate = CalculateBitrate(capture.EncodedWidth, capture.EncodedHeight);
        profile.Video.FrameRate.Numerator = 60;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
        var file = await folder.CreateFileAsync(
            Path.GetFileName(outputPath),
            CreationCollisionOption.ReplaceExisting);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, profile);
        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException($"Windows 无法准备 H.264 编码：{prepared.FailureReason}");
        }

        await prepared.TranscodeAsync().AsTask(cancellationToken);
        progress?.Report(1);
    }

    private static uint CalculateBitrate(int width, int height)
    {
        var pixels = (long)width * height;
        var bitrate = pixels * 60 * 12 / 100;
        return (uint)Math.Clamp(bitrate, 1_000_000, 18_000_000);
    }
}
