namespace Toolbox.Core;

public sealed class ModuleErrorEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
