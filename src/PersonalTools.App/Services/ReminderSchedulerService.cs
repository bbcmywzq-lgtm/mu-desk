using System.Windows.Threading;
using PersonalTools.Core;

namespace PersonalTools.App.Services;

/// <summary>
/// Polls the provider for due reminders. A reminder is durably marked as
/// triggered before the UI callback runs, preventing repeated alerts from timer
/// ticks and subsequent application starts.
/// </summary>
public sealed class ReminderSchedulerService : IDisposable
{
    private readonly IEntryProvider _provider;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _checking;
    private bool _disposed;

    public ReminderSchedulerService(
        IEntryProvider provider,
        TimeSpan? pollingInterval = null)
    {
        _provider = provider;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = pollingInterval ?? TimeSpan.FromSeconds(20),
        };
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler<ReminderTriggeredEventArgs>? ReminderTriggered;

    public event EventHandler<ReminderSchedulerFailedEventArgs>? PollingFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        // The immediate check restores reminders that became due while the tool
        // was not running; callers need not wait for the first timer interval.
        _ = CheckNowAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task CheckNowAsync()
    {
        if (_disposed || _checking)
        {
            return;
        }

        _checking = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var reminder = (await _provider.GetMissedAsync(now, _disposeCancellation.Token))
                .OrderBy(item => item.DueAt)
                .FirstOrDefault();
            if (reminder is not null)
            {
                // Present at most one item per tick so simultaneous reminders do
                // not overwrite the same speech bubble. The provider conditionally
                // marks the exact snapshot revision, closing complete/snooze/delete
                // races between the query and this write.
                var triggered = await _provider.TryMarkTriggeredIfDueAsync(
                    reminder.Id,
                    reminder.Revision,
                    now,
                    _disposeCancellation.Token);
                if (triggered is not null)
                {
                    ReminderTriggered?.Invoke(
                        this,
                        new ReminderTriggeredEventArgs(triggered));
                }
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // Expected during application shutdown.
        }
        catch (Exception exception)
        {
            PollingFailed?.Invoke(this, new ReminderSchedulerFailedEventArgs(exception));
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs e) => _ = CheckNowAsync();
}

public sealed class ReminderTriggeredEventArgs(EntryItem reminder) : EventArgs
{
    public EntryItem Reminder { get; } = reminder;
}

public sealed class ReminderSchedulerFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
