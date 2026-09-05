using System.Text.Json.Serialization;

namespace Toolbox.Core;

public enum EffectSendStatus
{
    NotSent,
    Connecting,
    Sending,
    Accepted,
    Completed,
    Failed,
    Cancelled,
}

public sealed record EffectFrameMetadata(
    int Index,
    string RelativePath,
    long TimestampMilliseconds,
    double MotionScore);

public sealed class EffectCaptureManifest
{
    public int SchemaVersion { get; set; } = 1;

    public required string PackageId { get; set; }

    public long DurationMilliseconds { get; set; }

    public int LogicalWidth { get; set; }

    public int LogicalHeight { get; set; }

    public int EncodedWidth { get; set; }

    public int EncodedHeight { get; set; }

    public string SourceVideo { get; set; } = "source.mp4";

    public string ContactSheet { get; set; } = "contact-sheet.png";

    public List<EffectFrameMetadata> Frames { get; set; } = [];

    [JsonIgnore]
    public bool HasSafeRelativePaths =>
        IsSafeRelativePath(SourceVideo) &&
        IsSafeRelativePath(ContactSheet) &&
        Frames.All(frame => IsSafeRelativePath(frame.RelativePath));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(PackageId))
        {
            errors.Add("素材包编号不能为空。");
        }

        if (DurationMilliseconds <= 0)
        {
            errors.Add("录制时长必须大于零。");
        }

        if (LogicalWidth <= 0 || LogicalHeight <= 0)
        {
            errors.Add("逻辑选区尺寸无效。");
        }

        if (EncodedWidth < LogicalWidth || EncodedHeight < LogicalHeight ||
            EncodedWidth % 2 != 0 || EncodedHeight % 2 != 0)
        {
            errors.Add("编码尺寸必须是覆盖逻辑选区的偶数尺寸。");
        }

        if (Frames.Count == 0)
        {
            errors.Add("素材包至少需要一个关键帧。");
        }

        if (!HasSafeRelativePaths)
        {
            errors.Add("素材包路径必须是安全的相对路径。");
        }

        var previousTimestamp = -1L;
        var indexes = new HashSet<int>();
        foreach (var frame in Frames)
        {
            if (!indexes.Add(frame.Index))
            {
                errors.Add("关键帧编号不能重复。");
                break;
            }

            if (frame.TimestampMilliseconds < previousTimestamp ||
                frame.TimestampMilliseconds < 0 ||
                frame.TimestampMilliseconds > DurationMilliseconds)
            {
                errors.Add("关键帧时间戳必须按录制时间升序排列。");
                break;
            }

            previousTimestamp = frame.TimestampMilliseconds;
        }

        return errors;
    }

    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        return !normalized.Split('/').Any(segment => segment is ".." or "." or "");
    }
}

public sealed class EffectSendState
{
    public int SchemaVersion { get; set; } = 1;

    public EffectSendStatus Status { get; set; }

    public string? ThreadId { get; set; }

    public string? TurnId { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<string> SubmittedFramePaths { get; set; } = [];

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    [JsonIgnore]
    public bool HasKnownDestination => !string.IsNullOrWhiteSpace(ThreadId);
}
