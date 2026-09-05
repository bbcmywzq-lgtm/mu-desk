using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace PersonalToolbox.Services;

internal sealed record FileShelfBridgeRequest(string Command, string[]? Paths = null);

internal sealed record FileShelfBridgeResponse(
    bool Ok,
    int AddedCount,
    int ItemCount,
    string Message);

internal sealed class FileShelfBridgeServer : IDisposable
{
    internal const string PipeName = "MuDesk.FileShelf.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<FileShelfBridgeRequest, Task<FileShelfBridgeResponse>> _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;

    public FileShelfBridgeServer(
        Func<FileShelfBridgeRequest, Task<FileShelfBridgeResponse>> handler)
    {
        _handler = handler;
    }

    public void Start() => _listener ??= ListenAsync(_shutdown.Token);

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A client may disappear while dragging. Keep the next request available.
            }
            catch (JsonException)
            {
                // Malformed local requests are ignored without affecting the shelf process.
            }
        }
    }

    private async Task HandleConnectionAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        await using var writer = new StreamWriter(stream, leaveOpen: true)
        {
            AutoFlush = true,
        };
        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var request = JsonSerializer.Deserialize<FileShelfBridgeRequest>(line, JsonOptions)
            ?? throw new JsonException("Empty file shelf bridge request.");
        var response = await _handler(request);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
