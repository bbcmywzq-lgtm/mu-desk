using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LightPet.Core.Packs;
using WpfImage = System.Windows.Controls.Image;

namespace LightPet.App.Services;

internal sealed class AnimationPlayer : IDisposable
{
    private readonly LoadedPetPack _pack;
    private readonly WpfImage _target;
    private ImageFrameCache _cache;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    private int _actionTransitions;
    private int _operationId;
    private readonly string? _tracePath = Environment.GetEnvironmentVariable("LIGHTPET_QA_TRACE");
    private readonly long _traceOrigin = Stopwatch.GetTimestamp();
    private readonly List<FrameTraceEntry> _trace = [];

    public AnimationPlayer(LoadedPetPack pack, WpfImage target, int decodePixelWidth)
    {
        _pack = pack;
        _target = target;
        // The longest hot loop currently contains eight frames. Keeping the
        // full loop avoids an LRU miss on every frame of idle/walk playback;
        // decoded frames are still released whenever the action changes.
        _cache = new ImageFrameCache(capacity: 8, decodePixelWidth);
    }

    public event Action<Exception>? PlaybackFailed;

    public string? CurrentAction { get; private set; }

    public BitmapSource? CurrentFrame => _target.Source as BitmapSource;

    public void SetDecodePixelWidth(int decodePixelWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cache.Clear();
        _cache = new ImageFrameCache(capacity: 8, decodePixelWidth);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        CurrentAction = null;
        _cache.Clear();
    }

    public void StartLoop(string actionId)
    {
        var operation = ReplaceOperation(actionId);
        _ = GuardAsync(
            () => RunLoopAsync(_pack.GetAction(actionId), operation),
            operation.Token);
    }

    public Task PlayOnceAsync(string actionId)
    {
        var operation = ReplaceOperation(actionId);
        return GuardAsync(
            () => RunOnceAsync(_pack.GetAction(actionId), operation),
            operation.Token);
    }

    public Task PlayStagedForAsync(string actionId, TimeSpan loopDuration)
    {
        var operation = ReplaceOperation(actionId);
        return GuardAsync(
            () => RunStagedForAsync(_pack.GetAction(actionId), loopDuration, operation),
            operation.Token);
    }

    public Task PlayStagedCyclesAsync(string actionId, int loopCycles)
    {
        if (loopCycles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(loopCycles));
        }

        var operation = ReplaceOperation(actionId);
        return GuardAsync(
            () => RunStagedCyclesAsync(_pack.GetAction(actionId), loopCycles, operation),
            operation.Token);
    }

    public TimeSpan GetStagedDuration(string actionId, int loopCycles)
    {
        if (loopCycles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(loopCycles));
        }

        var action = _pack.GetAction(actionId);
        var milliseconds = PhaseDuration(action.GetPhase(AnimationPhase.Start)) +
            PhaseDuration(action.GetPhase(AnimationPhase.Loop) ?? action.GetPhase(AnimationPhase.Single)) * loopCycles +
            PhaseDuration(action.GetPhase(AnimationPhase.End));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public TimeSpan GetStagedTravelDuration(string actionId, int loopCycles)
    {
        if (loopCycles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(loopCycles));
        }

        var action = _pack.GetAction(actionId);
        var milliseconds = PhaseDuration(action.GetPhase(AnimationPhase.Start)) +
            PhaseDuration(action.GetPhase(AnimationPhase.Loop) ?? action.GetPhase(AnimationPhase.Single)) * loopCycles;
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cache.Clear();
        WriteTrace();
    }

    private PlaybackOperation ReplaceOperation(string actionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        if (!string.Equals(CurrentAction, actionId, StringComparison.OrdinalIgnoreCase))
        {
            _cache.Clear();
            _actionTransitions++;
            if (_actionTransitions % 8 == 0)
            {
                _ = _target.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() =>
                        GC.Collect(
                            GC.MaxGeneration,
                            GCCollectionMode.Optimized,
                            blocking: false,
                            compacting: false)));
            }
        }

        _cancellation = new CancellationTokenSource();
        _operationId++;
        CurrentAction = actionId;
        return new PlaybackOperation(_operationId, actionId, _cancellation.Token);
    }

    private async Task RunLoopAsync(LoadedAction action, PlaybackOperation operation)
    {
        if (action.GetPhase(AnimationPhase.Single) is { } single)
        {
            while (true)
            {
                await PlayPhaseAsync(single, operation);
            }
        }

        if (action.GetPhase(AnimationPhase.Start) is { } start)
        {
            await PlayPhaseAsync(start, operation);
        }

        var loop = action.GetPhase(AnimationPhase.Loop)
            ?? throw new InvalidOperationException($"Action '{action.Id}' has no loop phase.");
        while (true)
        {
            await PlayPhaseAsync(loop, operation);
        }
    }

    private async Task RunOnceAsync(LoadedAction action, PlaybackOperation operation)
    {
        if (action.GetPhase(AnimationPhase.Single) is { } single)
        {
            await PlayPhaseAsync(single, operation);
            return;
        }

        if (action.GetPhase(AnimationPhase.Start) is { } start)
        {
            await PlayPhaseAsync(start, operation);
        }

        if (action.GetPhase(AnimationPhase.Loop) is { } loop)
        {
            await PlayPhaseAsync(loop, operation);
        }

        if (action.GetPhase(AnimationPhase.End) is { } end)
        {
            await PlayPhaseAsync(end, operation);
        }
    }

    private async Task RunStagedForAsync(
        LoadedAction action,
        TimeSpan loopDuration,
        PlaybackOperation operation)
    {
        if (action.GetPhase(AnimationPhase.Start) is { } start)
        {
            await PlayPhaseAsync(start, operation);
        }

        var loop = action.GetPhase(AnimationPhase.Loop)
            ?? action.GetPhase(AnimationPhase.Single)
            ?? throw new InvalidOperationException($"Action '{action.Id}' has no playable loop.");
        var deadline = operation.DeadlineMilliseconds + loopDuration.TotalMilliseconds;
        do
        {
            await PlayPhaseAsync(loop, operation);
        }
        while (operation.DeadlineMilliseconds < deadline);

        if (action.GetPhase(AnimationPhase.End) is { } end)
        {
            await PlayPhaseAsync(end, operation);
        }
    }

    private async Task RunStagedCyclesAsync(
        LoadedAction action,
        int loopCycles,
        PlaybackOperation operation)
    {
        if (action.GetPhase(AnimationPhase.Start) is { } start)
        {
            await PlayPhaseAsync(start, operation);
        }

        var loop = action.GetPhase(AnimationPhase.Loop)
            ?? action.GetPhase(AnimationPhase.Single)
            ?? throw new InvalidOperationException($"Action '{action.Id}' has no playable loop.");
        for (var cycle = 0; cycle < loopCycles; cycle++)
        {
            await PlayPhaseAsync(loop, operation);
        }

        if (action.GetPhase(AnimationPhase.End) is { } end)
        {
            await PlayPhaseAsync(end, operation);
        }
    }

    private static int PhaseDuration(LoadedPhase? phase) =>
        phase?.Frames.Sum(frame => frame.DurationMilliseconds) ?? 0;

    private async Task PlayPhaseAsync(LoadedPhase phase, PlaybackOperation operation)
    {
        foreach (var frame in phase.Frames)
        {
            operation.Token.ThrowIfCancellationRequested();
            _target.Source = _cache.Get(frame.Path);
            if (!string.IsNullOrWhiteSpace(_tracePath))
            {
                _trace.Add(new FrameTraceEntry(
                    operation.Id,
                    operation.Action,
                    phase.Kind.ToString(),
                    Path.GetFileName(frame.Path),
                    frame.DurationMilliseconds,
                    Math.Round(Stopwatch.GetElapsedTime(_traceOrigin).TotalMilliseconds, 3)));
            }
            operation.DeadlineMilliseconds += frame.DurationMilliseconds;
            var remaining = operation.DeadlineMilliseconds - operation.Clock.Elapsed.TotalMilliseconds;
            if (remaining > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(remaining), operation.Token);
            }
            else
            {
                operation.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }

    private void WriteTrace()
    {
        if (string.IsNullOrWhiteSpace(_tracePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_tracePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            _tracePath,
            JsonSerializer.Serialize(
                new
                {
                    stopwatchFrequency = Stopwatch.Frequency,
                    entries = _trace,
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task GuardAsync(Func<Task> operation, CancellationToken token)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PlaybackFailed?.Invoke(exception);
        }
    }

    private sealed record FrameTraceEntry(
        int OperationId,
        string Action,
        string Phase,
        string Frame,
        int ExpectedDurationMs,
        double PresentedAtMs);

    private sealed class PlaybackOperation(
        int id,
        string action,
        CancellationToken token)
    {
        public int Id { get; } = id;

        public string Action { get; } = action;

        public CancellationToken Token { get; } = token;

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        public double DeadlineMilliseconds { get; set; }
    }
}
