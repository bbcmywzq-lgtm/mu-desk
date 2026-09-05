using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Core;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPen = System.Drawing.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace PersonalToolbox.Services;

public sealed class EffectCapturePackageResult
{
    public required string RootPath { get; init; }

    public required EffectCaptureManifest Manifest { get; init; }

    public required IReadOnlyList<string> FramePaths { get; init; }

    public string SourceVideoPath => Path.Combine(RootPath, Manifest.SourceVideo);

    public string ContactSheetPath => Path.Combine(RootPath, Manifest.ContactSheet);

    public string EffectPromptPath => Path.Combine(RootPath, "effect.md");

    public string SendStatePath => Path.Combine(RootPath, "send-state.json");
}

public sealed class CapturePackageService
{
    private readonly ScreenCaptureService _captureService;
    private readonly MediaFoundationMp4Encoder _encoder;

    public CapturePackageService(
        ScreenCaptureService? captureService = null,
        MediaFoundationMp4Encoder? encoder = null)
    {
        _captureService = captureService ?? new ScreenCaptureService();
        _encoder = encoder ?? new MediaFoundationMp4Encoder();
    }

    public async Task<EffectCapturePackageResult> CreateAsync(
        EffectCaptureRegion region,
        TimeSpan duration,
        string outputRoot,
        IProgress<(string Stage, double Progress)>? progress,
        CancellationToken cancellationToken,
        CancellationToken stopToken = default)
    {
        var now = DateTimeOffset.Now;
        var packageId = now.ToString("yyyyMMdd-HHmmss-fff");
        var dayFolder = Path.Combine(Path.GetFullPath(outputRoot), now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dayFolder);
        var finalPath = UniqueDirectoryPath(dayFolder, packageId);
        var temporaryPath = finalPath + ".mu-effect-partial";
        Directory.CreateDirectory(temporaryPath);
        try
        {
            var rawPath = Path.Combine(temporaryPath, "capture.bgra");
            progress?.Report(("正在录制", 0));
            var capture = await _captureService.CaptureAsync(
                region,
                duration,
                rawPath,
                new Progress<TimeSpan>(elapsed =>
                    progress?.Report(("正在录制", Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)))),
                cancellationToken,
                stopToken);

            progress?.Report(("正在编码 MP4", 0));
            await _encoder.EncodeAsync(
                capture,
                Path.Combine(temporaryPath, "source.mp4"),
                new Progress<double>(value => progress?.Report(("正在编码 MP4", value))),
                cancellationToken);

            var requestedFrames = duration <= TimeSpan.FromSeconds(2.5) ? 12 : 16;
            var selected = EffectKeyFrameSelector.Select(
                capture.Frames.Select(frame => new EffectFrameMetric(
                    frame.Index,
                    frame.TimestampMilliseconds,
                    frame.MotionScore,
                    frame.EdgeChangeScore)).ToArray(),
                requestedFrames);
            var selectedLookup = capture.Frames.ToDictionary(frame => frame.Index);
            var frameDirectory = Path.Combine(temporaryPath, "frames");
            Directory.CreateDirectory(frameDirectory);
            var framePaths = new List<string>();
            var frameMetadata = new List<EffectFrameMetadata>();
            var frameImages = new List<(byte[] Pixels, long Timestamp)>();
            var frameBytes = checked(capture.EncodedWidth * capture.EncodedHeight * 4);
            await using (var rawStream = new FileStream(
                             capture.RawPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                for (var index = 0; index < selected.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var metric = selected[index];
                    var rawFrame = selectedLookup[metric.Index];
                    rawStream.Position = rawFrame.FileOffset;
                    var encodedPixels = new byte[frameBytes];
                    await rawStream.ReadExactlyAsync(encodedPixels, cancellationToken);
                    var logicalPixels = CropLogicalPixels(
                        encodedPixels,
                        capture.EncodedWidth,
                        capture.LogicalWidth,
                        capture.LogicalHeight);
                    var fileName = $"frame-{index:000}-{metric.TimestampMilliseconds:00000}ms.png";
                    var absolutePath = Path.Combine(frameDirectory, fileName);
                    WritePng(absolutePath, logicalPixels, capture.LogicalWidth, capture.LogicalHeight);
                    var relativePath = Path.Combine("frames", fileName).Replace('\\', '/');
                    framePaths.Add(absolutePath);
                    frameMetadata.Add(new EffectFrameMetadata(
                        metric.Index,
                        relativePath,
                        metric.TimestampMilliseconds,
                        metric.MotionScore));
                    frameImages.Add((logicalPixels, metric.TimestampMilliseconds));
                    progress?.Report(("正在整理关键帧", (index + 1d) / selected.Count));
                }
            }

            WriteContactSheet(
                Path.Combine(temporaryPath, "contact-sheet.png"),
                frameImages,
                capture.LogicalWidth,
                capture.LogicalHeight);
            var manifest = new EffectCaptureManifest
            {
                PackageId = Path.GetFileName(finalPath),
                DurationMilliseconds = capture.DurationMilliseconds,
                LogicalWidth = capture.LogicalWidth,
                LogicalHeight = capture.LogicalHeight,
                EncodedWidth = capture.EncodedWidth,
                EncodedHeight = capture.EncodedHeight,
                Frames = frameMetadata,
            };
            var validationErrors = manifest.Validate();
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", validationErrors));
            }

            await File.WriteAllTextAsync(
                Path.Combine(temporaryPath, "capture.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryPath, "send-state.json"),
                JsonSerializer.Serialize(new EffectSendState
                {
                    Status = EffectSendStatus.NotSent,
                    UpdatedAt = DateTimeOffset.Now,
                }, JsonOptions),
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryPath, "effect.md"),
                BuildEffectPrompt(manifest),
                Encoding.UTF8,
                cancellationToken);
            File.Delete(rawPath);

            ValidateRequiredFiles(temporaryPath, manifest);
            Directory.Move(temporaryPath, finalPath);
            return new EffectCapturePackageResult
            {
                RootPath = finalPath,
                Manifest = manifest,
                FramePaths = frameMetadata
                    .Select(frame => Path.Combine(finalPath, frame.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
                    .ToArray(),
            };
        }
        catch
        {
            TryDeletePartial(temporaryPath);
            throw;
        }
    }

    public static void CleanupStalePartialDirectories(string outputRoot, DateTimeOffset cutoff)
    {
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     outputRoot,
                     "*.mu-effect-partial",
                     SearchOption.AllDirectories))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) <= cutoff.UtcDateTime &&
                    (File.Exists(Path.Combine(directory, "capture.bgra")) ||
                     !File.Exists(Path.Combine(directory, "capture.json"))))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string UniqueDirectoryPath(string parent, string packageId)
    {
        var candidate = Path.Combine(parent, packageId);
        for (var suffix = 1; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(parent, $"{packageId}-{suffix}");
        }

        return candidate;
    }

    private static byte[] CropLogicalPixels(
        byte[] encoded,
        int encodedWidth,
        int logicalWidth,
        int logicalHeight)
    {
        var result = new byte[checked(logicalWidth * logicalHeight * 4)];
        var sourceStride = encodedWidth * 4;
        var targetStride = logicalWidth * 4;
        for (var row = 0; row < logicalHeight; row++)
        {
            Buffer.BlockCopy(encoded, row * sourceStride, result, row * targetStride, targetStride);
        }

        return result;
    }

    private static void WritePng(string path, byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void WriteContactSheet(
        string path,
        IReadOnlyList<(byte[] Pixels, long Timestamp)> frames,
        int sourceWidth,
        int sourceHeight)
    {
        const int columns = 4;
        const int cardWidth = 250;
        const int imageWidth = 230;
        const int labelHeight = 28;
        const int gap = 12;
        var imageHeight = Math.Clamp((int)Math.Round(imageWidth * sourceHeight / (double)sourceWidth), 80, 180);
        var cardHeight = imageHeight + labelHeight + 16;
        var rows = (int)Math.Ceiling(frames.Count / (double)columns);
        using var sheet = new DrawingBitmap(
            (columns * cardWidth) + ((columns + 1) * gap),
            (rows * cardHeight) + ((rows + 1) * gap),
            PixelFormat.Format32bppArgb);
        using var graphics = DrawingGraphics.FromImage(sheet);
        graphics.Clear(DrawingColor.FromArgb(238, 240, 245));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        using var outline = new DrawingPen(DrawingColor.FromArgb(37, 37, 42), 1);
        using var labelFont = new DrawingFont("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
        for (var index = 0; index < frames.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = gap + (column * (cardWidth + gap));
            var y = gap + (row * (cardHeight + gap));
            graphics.FillRectangle(DrawingBrushes.White, x, y, cardWidth, cardHeight);
            graphics.DrawRectangle(outline, x, y, cardWidth, cardHeight);
            using var frameBitmap = CreateDrawingBitmap(frames[index].Pixels, sourceWidth, sourceHeight);
            graphics.DrawImage(frameBitmap, x + 10, y + 8, imageWidth, imageHeight);
            graphics.DrawString(
                $"{index + 1:00}  ·  {frames[index].Timestamp} ms",
                labelFont,
                DrawingBrushes.Black,
                x + 10,
                y + imageHeight + 12);
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static DrawingBitmap CreateDrawingBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new DrawingBitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    pixels,
                    row * width * 4,
                    IntPtr.Add(data.Scan0, row * data.Stride),
                    width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static string BuildEffectPrompt(EffectCaptureManifest manifest)
    {
        var timestamps = string.Join(" / ", manifest.Frames.Select(frame => $"{frame.TimestampMilliseconds}ms"));
        return $"""
            # 动态效果分析素材

            录制长度：{manifest.DurationMilliseconds} ms  
            选区尺寸：{manifest.LogicalWidth} × {manifest.LogicalHeight}  
            关键帧数量：{manifest.Frames.Count}  
            关键帧时间：{timestamps}

            请按关键帧顺序分析这段界面动态效果，重点说明：

            1. 起始、加速、关键转折、过冲/回弹和结束状态；
            2. 位移、缩放、透明度、模糊、颜色与层级变化；
            3. 最可能的 easing、时长、延迟和触发方式；
            4. 如何用 HTML/CSS/JavaScript 复现相近观感；
            5. 哪些细节仅凭画面无法确定，请明确标注推测。

            原始 MP4 保存在当前素材包的 `source.mp4`，请优先依据已附加的原尺寸关键帧理解效果。
            """;
    }

    private static void ValidateRequiredFiles(string root, EffectCaptureManifest manifest)
    {
        var required = new[]
        {
            manifest.SourceVideo,
            manifest.ContactSheet,
            "effect.md",
            "capture.json",
            "send-state.json",
        }.Concat(manifest.Frames.Select(frame => frame.RelativePath));
        foreach (var relativePath in required)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                throw new InvalidOperationException($"素材包缺少有效文件：{relativePath}");
            }
        }
    }

    private static void TryDeletePartial(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
