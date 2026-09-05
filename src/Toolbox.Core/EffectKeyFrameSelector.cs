namespace Toolbox.Core;

public sealed record EffectFrameMetric(
    int Index,
    long TimestampMilliseconds,
    double MotionScore,
    double EdgeChangeScore = 0);

public static class EffectKeyFrameSelector
{
    public static IReadOnlyList<EffectFrameMetric> Select(
        IReadOnlyList<EffectFrameMetric> frames,
        int requestedCount = 12)
    {
        if (frames.Count == 0)
        {
            return [];
        }

        var ordered = frames
            .OrderBy(frame => frame.TimestampMilliseconds)
            .ThenBy(frame => frame.Index)
            .ToArray();
        var target = Math.Min(ordered.Length, Math.Clamp(requestedCount, 8, 16));
        if (ordered.Length <= target)
        {
            return ordered;
        }

        var chosen = new HashSet<int> { 0, ordered.Length - 1 };

        // Preserve broad temporal coverage before ranking local motion peaks.
        var coverageSlots = Math.Max(0, Math.Min(target / 3, target - chosen.Count));
        for (var slot = 1; slot <= coverageSlots; slot++)
        {
            var position = (double)slot / (coverageSlots + 1);
            chosen.Add((int)Math.Round(position * (ordered.Length - 1)));
        }

        var ranked = Enumerable.Range(1, ordered.Length - 2)
            .Select(position => new
            {
                Position = position,
                Score = PeakScore(ordered, position),
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => ordered[candidate.Position].TimestampMilliseconds)
            .ThenBy(candidate => ordered[candidate.Position].Index)
            .ToArray();

        var totalDuration = Math.Max(1, ordered[^1].TimestampMilliseconds - ordered[0].TimestampMilliseconds);
        var minimumSpacing = Math.Max(12, totalDuration / Math.Max(24, target * 3));
        foreach (var candidate in ranked)
        {
            if (chosen.Count >= target)
            {
                break;
            }

            var timestamp = ordered[candidate.Position].TimestampMilliseconds;
            if (chosen.All(position =>
                    Math.Abs(ordered[position].TimestampMilliseconds - timestamp) >= minimumSpacing))
            {
                chosen.Add(candidate.Position);
            }
        }

        // Dense or duplicate timestamps can defeat spacing. Fill deterministically.
        for (var position = 1; chosen.Count < target && position < ordered.Length - 1; position++)
        {
            chosen.Add(position);
        }

        return chosen
            .OrderBy(position => ordered[position].TimestampMilliseconds)
            .ThenBy(position => ordered[position].Index)
            .Select(position => ordered[position])
            .ToArray();
    }

    private static double PeakScore(IReadOnlyList<EffectFrameMetric> frames, int position)
    {
        var current = Combined(frames[position]);
        var previous = Combined(frames[position - 1]);
        var next = Combined(frames[position + 1]);
        var localPeak = Math.Max(0, current - ((previous + next) / 2));
        var turn = Math.Abs((current - previous) - (next - current));
        return current + (localPeak * 1.8) + (turn * 0.45);
    }

    private static double Combined(EffectFrameMetric frame) =>
        Math.Max(0, frame.MotionScore) + (Math.Max(0, frame.EdgeChangeScore) * 0.35);
}
