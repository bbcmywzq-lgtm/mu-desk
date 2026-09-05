using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalTools.Core;

internal sealed class AtomicJsonFile<TDocument>
    where TDocument : class
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly Func<TDocument> _createEmpty;
    private readonly Func<TDocument, bool> _isValid;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TDocument? _document;

    public AtomicJsonFile(
        string path,
        Func<TDocument> createEmpty,
        Func<TDocument, bool>? isValid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _createEmpty = createEmpty ?? throw new ArgumentNullException(nameof(createEmpty));
        _isValid = isValid ?? (_ => true);
    }

    public async Task<TResult> ReadAsync<TResult>(
        Func<TDocument, TResult> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return read(_document!);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> UpdateAsync<TResult>(
        Func<TDocument, (TDocument Document, TResult Result)> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = update(_document!);
            if (!_isValid(next))
            {
                throw new InvalidDataException("The updated personal-tool document is invalid.");
            }

            await SaveAsync(next, cancellationToken).ConfigureAwait(false);
            _document = next;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_document is not null)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            _document = _createEmpty();
            return;
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var loaded = await JsonSerializer.DeserializeAsync<TDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (loaded is null || !_isValid(loaded))
            {
                throw new JsonException("The personal-tool document is empty or invalid.");
            }

            _document = loaded;
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or InvalidDataException)
        {
            PreserveCorruptFile();
            _document = _createEmpty();
        }
    }

    private async Task SaveAsync(TDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void PreserveCorruptFile()
    {
        var backup = $"{_path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        try
        {
            File.Copy(_path, backup, overwrite: false);
        }
        catch (IOException)
        {
            // Recovery must remain available even if a diagnostic backup cannot be made.
        }
        catch (UnauthorizedAccessException)
        {
            // Recovery must remain available even if a diagnostic backup cannot be made.
        }
    }
}
