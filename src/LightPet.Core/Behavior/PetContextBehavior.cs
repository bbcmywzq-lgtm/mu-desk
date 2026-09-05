using System.Globalization;

namespace LightPet.Core.Behavior;

public enum PetDayType
{
    Workday,
    RestDay,
}

public enum PetTimeBlock
{
    Morning,
    WorkMorning,
    Midday,
    Afternoon,
    Evening,
    LateNight,
}

public readonly record struct PetContextSnapshot(
    DateTimeOffset LocalTime,
    PetDayType DayType,
    PetTimeBlock TimeBlock)
{
    public string SignatureKey =>
        $"{LocalTime:yyyy-MM-dd}:{DayType}:{TimeBlock}";
}

public readonly record struct ActivityDelayRange(int MinimumSeconds, int MaximumSecondsExclusive);

public static class PetContextResolver
{
    public static PetContextSnapshot Resolve(
        DateTimeOffset localTime,
        string? dayTypeOverride = null)
    {
        var dayType = dayTypeOverride?.Trim().ToLowerInvariant() switch
        {
            "workday" or "work" or "weekday" => PetDayType.Workday,
            "restday" or "rest" or "weekend" => PetDayType.RestDay,
            _ => localTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? PetDayType.RestDay
                : PetDayType.Workday,
        };

        var hour = localTime.Hour;
        var timeBlock = hour switch
        {
            >= 6 and < 9 => PetTimeBlock.Morning,
            >= 9 and < 12 => PetTimeBlock.WorkMorning,
            >= 12 and < 14 => PetTimeBlock.Midday,
            >= 14 and < 18 => PetTimeBlock.Afternoon,
            >= 18 and < 23 => PetTimeBlock.Evening,
            _ => PetTimeBlock.LateNight,
        };

        return new PetContextSnapshot(localTime, dayType, timeBlock);
    }

    public static DateTimeOffset ResolveQaTime(
        DateTimeOffset realLocalTime,
        string? qaTime)
    {
        if (string.IsNullOrWhiteSpace(qaTime))
        {
            return realLocalTime;
        }

        if (TimeOnly.TryParse(
                qaTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var timeOnly))
        {
            return new DateTimeOffset(
                realLocalTime.Year,
                realLocalTime.Month,
                realLocalTime.Day,
                timeOnly.Hour,
                timeOnly.Minute,
                timeOnly.Second,
                realLocalTime.Offset);
        }

        if (DateTimeOffset.TryParse(
                qaTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var fullTime))
        {
            return fullTime;
        }

        return realLocalTime;
    }
}

public sealed class ContextBehaviorPlanner
{
    private const int RecentActionLimit = 3;
    private readonly Queue<string> _recentActions = new();
    private readonly HashSet<string> _completedSignatures = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> RecentActions => _recentActions.ToArray();

    public bool TryChooseSignatureAction(
        PetContextSnapshot context,
        IReadOnlySet<string> availableActions,
        double randomValue,
        out string actionId)
    {
        actionId = string.Empty;
        if (_completedSignatures.Contains(context.SignatureKey))
        {
            return false;
        }

        var selected = ChooseAction(
            GetSignatureCandidates(context),
            availableActions,
            randomValue);
        if (selected is null)
        {
            return false;
        }

        _completedSignatures.Add(context.SignatureKey);
        actionId = selected;
        RecordAction(selected);
        return true;
    }

    public string? ChooseAmbientAction(
        PetContextSnapshot context,
        IReadOnlySet<string> availableActions,
        double randomValue)
    {
        var selected = ChooseAction(
            GetAmbientCandidates(context),
            availableActions,
            randomValue);
        if (selected is not null)
        {
            RecordAction(selected);
        }

        return selected;
    }

    public bool ShouldPatrol(PetContextSnapshot context, double randomValue)
    {
        var threshold = context.TimeBlock switch
        {
            PetTimeBlock.LateNight => 0.06,
            PetTimeBlock.Midday => 0.12,
            PetTimeBlock.WorkMorning or PetTimeBlock.Afternoon
                when context.DayType == PetDayType.Workday => 0.18,
            PetTimeBlock.Morning => 0.20,
            _ when context.DayType == PetDayType.RestDay => 0.30,
            _ => 0.24,
        };

        if (_recentActions.Contains("patrol", StringComparer.OrdinalIgnoreCase))
        {
            threshold *= 0.35;
        }

        return ClampUnit(randomValue) < threshold;
    }

    public void RecordPatrol() => RecordAction("patrol");

    public static int GetLoopCycles(string actionId) =>
        actionId is "sit-rest" or "sleep" ? 2 : 1;

    public static ActivityDelayRange GetActivityDelay(
        PetContextSnapshot context,
        bool restingOnSideOrTop,
        bool qaStress)
    {
        if (qaStress)
        {
            return new ActivityDelayRange(2, 5);
        }

        if (restingOnSideOrTop)
        {
            return context.TimeBlock == PetTimeBlock.LateNight
                ? new ActivityDelayRange(240, 481)
                : new ActivityDelayRange(120, 301);
        }

        return context.TimeBlock == PetTimeBlock.LateNight
            ? new ActivityDelayRange(90, 181)
            : new ActivityDelayRange(45, 91);
    }

    private string? ChooseAction(
        IReadOnlyList<string> weightedCandidates,
        IReadOnlySet<string> availableActions,
        double randomValue)
    {
        var candidates = weightedCandidates
            .Where(availableActions.Contains)
            .Where(candidate => !_recentActions.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var index = Math.Min(
            candidates.Length - 1,
            (int)Math.Floor(ClampUnit(randomValue) * candidates.Length));
        return candidates[index];
    }

    private void RecordAction(string actionId)
    {
        _recentActions.Enqueue(actionId);
        while (_recentActions.Count > RecentActionLimit)
        {
            _recentActions.Dequeue();
        }
    }

    private static IReadOnlyList<string> GetSignatureCandidates(PetContextSnapshot context) =>
        (context.DayType, context.TimeBlock) switch
        {
            (PetDayType.RestDay, PetTimeBlock.Morning) =>
                ["yawn", "sit-rest", "stretch"],
            (PetDayType.RestDay, PetTimeBlock.WorkMorning) =>
                ["sit-rest", "happy", "wave"],
            (_, PetTimeBlock.Morning) =>
                ["stretch", "yawn", "hair-fix"],
            (_, PetTimeBlock.WorkMorning) =>
                ["think", "hair-fix", "stretch"],
            (_, PetTimeBlock.Midday) =>
                ["sit-rest", "yawn", "hair-fix"],
            (PetDayType.RestDay, PetTimeBlock.Afternoon) =>
                ["happy", "sit-rest", "wave"],
            (_, PetTimeBlock.Afternoon) =>
                ["think", "hair-fix", "stretch"],
            (_, PetTimeBlock.Evening) =>
                ["sit-rest", "happy", "wave"],
            _ => ["yawn", "sleep", "sit-rest"],
        };

    private static IReadOnlyList<string> GetAmbientCandidates(PetContextSnapshot context) =>
        (context.DayType, context.TimeBlock) switch
        {
            (PetDayType.RestDay, PetTimeBlock.Morning) =>
                ["idle-random", "idle-random", "yawn", "stretch", "sit-rest", "happy"],
            (PetDayType.RestDay, PetTimeBlock.WorkMorning) =>
                ["idle-random", "idle-random", "sit-rest", "happy", "happy", "wave"],
            (PetDayType.RestDay, PetTimeBlock.Afternoon) =>
                ["idle-random", "happy", "happy", "wave", "sit-rest", "stretch"],
            (_, PetTimeBlock.Morning) =>
                ["idle-random", "idle-random", "stretch", "hair-fix", "yawn"],
            (_, PetTimeBlock.WorkMorning) =>
                ["idle-random", "idle-random", "think", "think", "hair-fix", "wave"],
            (_, PetTimeBlock.Midday) =>
                ["idle-random", "idle-random", "sit-rest", "sit-rest", "yawn", "hair-fix"],
            (_, PetTimeBlock.Afternoon) =>
                ["idle-random", "idle-random", "think", "think", "hair-fix", "stretch"],
            (_, PetTimeBlock.Evening) =>
                ["idle-random", "idle-random", "happy", "wave", "sit-rest", "hair-fix"],
            _ => ["idle-random", "yawn", "yawn", "sleep", "sleep", "sit-rest"],
        };

    private static double ClampUnit(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 0, 0, Math.BitDecrement(1d));
}
