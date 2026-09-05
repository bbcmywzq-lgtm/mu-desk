namespace Toolbox.Core;

public interface IToolModule : IDisposable
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    bool IsRunning { get; }

    bool IsPaused { get; }

    event EventHandler? StateChanged;

    event EventHandler<ModuleErrorEventArgs>? Error;

    bool Start();

    void Stop();

    void SetPaused(bool paused);

    bool OpenSettings();
}
