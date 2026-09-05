using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace LightPet.App.Services;

internal sealed record FileShelfResult(
    bool Ok,
    int AddedCount,
    int ItemCount,
    string Message);

internal sealed class FileShelfClient
{
    private const string PipeName = "MuDesk.FileShelf.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<FileShelfResult> OpenAsync() => SendWithStartupAsync(
        new FileShelfRequest("show"));

    public Task<FileShelfResult> AddPathsAsync(IEnumerable<string> paths) => SendWithStartupAsync(
        new FileShelfRequest("addPaths", paths.ToArray()));

    private static async Task<FileShelfResult> SendWithStartupAsync(FileShelfRequest request)
    {
        var firstAttempt = await TrySendAsync(request, 180);
        if (firstAttempt is not null)
        {
            return firstAttempt;
        }

        var executable = FindToolboxExecutable();
        if (executable is null)
        {
            return Failure("没有找到 MU Desk，无法打开 Drop。");
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(executable, "--minimized")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable),
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failure("MU Desk 启动失败，Drop 暂时不可用。");
        }

        for (var attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(250);
            var response = await TrySendAsync(request, 250);
            if (response is not null)
            {
                return response;
            }
        }

        return Failure("MU Desk 已启动，但 Drop 没有及时响应。");
    }

    private static async Task<FileShelfResult?> TrySendAsync(
        FileShelfRequest request,
        int connectTimeoutMilliseconds)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(connectTimeoutMilliseconds);
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true)
            {
                AutoFlush = true,
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            var line = await reader.ReadLineAsync(timeout.Token);
            return string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<FileShelfResult>(line, JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private static string? FindToolboxExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("PERSONALTOOLBOX_EXE");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "tools", "MUDesk", "PersonalToolbox.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "PersonalToolbox.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "PersonalToolbox-win-x64-shelf", "PersonalToolbox.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "PersonalToolbox-win-x64-next", "PersonalToolbox.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "PersonalToolbox-win-x64", "PersonalToolbox.exe")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "PersonalToolbox-win-x64-next", "PersonalToolbox.exe")),
        };

        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static FileShelfResult Failure(string message) => new(false, 0, 0, message);

    private sealed record FileShelfRequest(string Command, string[]? Paths = null);
}
