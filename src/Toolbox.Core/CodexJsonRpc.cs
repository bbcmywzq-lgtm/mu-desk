using System.Text;
using System.Text.Json;

namespace Toolbox.Core;

public sealed class JsonRpcLineBuffer
{
    private readonly StringBuilder _buffer = new();

    public IReadOnlyList<JsonDocument> Push(string? chunk)
    {
        if (!string.IsNullOrEmpty(chunk))
        {
            _buffer.Append(chunk);
        }

        var documents = new List<JsonDocument>();
        while (true)
        {
            var content = _buffer.ToString();
            var newline = content.IndexOf('\n');
            if (newline < 0)
            {
                break;
            }

            var line = content[..newline].TrimEnd('\r');
            _buffer.Remove(0, newline + 1);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            documents.Add(JsonDocument.Parse(line));
        }

        return documents;
    }

    public void Clear() => _buffer.Clear();
}

public static class CodexJsonRpc
{
    public static string Request(long id, string method, object? parameters = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["method"] = method,
            ["id"] = id,
            ["params"] = parameters ?? new { },
        });

    public static string Notification(string method, object? parameters = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["method"] = method,
            ["params"] = parameters ?? new { },
        });

    public static string SanitizeDiagnostic(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var sanitized = message.ReplaceLineEndings(" ").Trim();
        foreach (var prefix in new[] { "sk-", "Bearer ", "access_token", "refresh_token", "password=" })
        {
            var index = sanitized.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                sanitized = sanitized[..index] + "[已隐藏敏感信息]";
            }
        }

        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }
}
