namespace DesktopOrganizer.Services;

public sealed class SingleInstanceActivationService : IDisposable
{
    private const string ActivationEventName = "Local\\DesktopOrganizer.Activate";
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action _activate;
    private readonly Task _listener;

    public SingleInstanceActivationService(Action activate)
    {
        _activate = activate;
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName,
            out _);
        _listener = Task.Run(Listen);
    }

    public static bool SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                return activationEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
        }

        return false;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _activationEvent.Set();
        try
        {
            _listener.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Shutdown must continue even if the listener is already unwinding.
        }

        _activationEvent.Dispose();
        _cancellation.Dispose();
    }

    private void Listen()
    {
        var handles = new WaitHandle[] { _activationEvent, _cancellation.Token.WaitHandle };
        while (!_cancellation.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(handles) == 1 || _cancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                _activate();
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not activate the existing application instance.", exception);
            }
        }
    }
}
