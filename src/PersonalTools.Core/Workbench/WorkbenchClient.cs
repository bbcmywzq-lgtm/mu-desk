using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalTools.Core;

public sealed class WorkbenchOptions
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } =
        "https://scnf2yqkgg3u.feishuapp.com/app/app_17dgx84zp3z";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = string.Empty;
}

public sealed class WorkbenchCommandResult
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public int? Revision { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class WorkbenchSnapshot
{
    [JsonPropertyName("entries")]
    public List<JsonElement> Entries { get; set; } = new();

    [JsonPropertyName("reminders")]
    public List<JsonElement> Reminders { get; set; } = new();

    [JsonPropertyName("cursor")]
    public long Cursor { get; set; }
}
/// <summary>
/// 工作台同步客户端：负责配置加载、命令推送与离线待发队列。
/// </summary>
public sealed class WorkbenchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _configPath;
    private readonly string _outboxPath;
    private readonly HttpClient _http = new();
    private WorkbenchOptions _options = new();
    private List<JsonElement> _outbox = new();
    private int _operationCounter;

    public WorkbenchClient(string dataDirectory)
    {
        _configPath = Path.Combine(dataDirectory, "workbench.json");
        _outboxPath = Path.Combine(dataDirectory, "workbench-outbox.json");
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.Secret);

    public async Task LoadAsync()
    {
        if (File.Exists(_configPath))
        {
            var json = await File.ReadAllTextAsync(_configPath);
            _options =
                JsonSerializer.Deserialize<WorkbenchOptions>(json, JsonOptions)
                ?? new WorkbenchOptions();
        }
        if (File.Exists(_outboxPath))
        {
            var json = await File.ReadAllTextAsync(_outboxPath);
            _outbox =
                JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions)
                ?? new List<JsonElement>();
        }
    }

    public async Task SaveOptionsAsync(WorkbenchOptions options)
    {
        _options = options;
        var json = JsonSerializer.Serialize(options, JsonOptions);
        await File.WriteAllTextAsync(_configPath, json);
    }

    public string NextOperationId()
    {
        _operationCounter += 1;
        return $"desk-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{_operationCounter}-{Random.Shared.Next(1000, 9999)}";
    }

    public async Task EnqueueAsync(JsonElement command)
    {
        _outbox.Add(command);
        await PersistOutboxAsync();
    }

    public int OutboxCount => _outbox.Count;

    private async Task PersistOutboxAsync()
    {
        var json = JsonSerializer.Serialize(_outbox, JsonOptions);
        await File.WriteAllTextAsync(_outboxPath, json);
    }

    /// <summary>
    /// 把命令直接发给工作台。网络失败时抛出异常，由调用方决定是否入队。
    /// </summary>
    public async Task<IReadOnlyList<WorkbenchCommandResult>> SendAsync(
        IReadOnlyList<JsonElement> commands,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/openapi/v1/sync/commands");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            _options.ApiKey);
        request.Headers.Add("x-mujun-sync-secret", _options.Secret);
        var body = JsonSerializer.Serialize(
            new Dictionary<string, object> { ["commands"] = commands },
            JsonOptions);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
        {
            throw new UnauthorizedAccessException("工作台同步密钥无效，请检查 workbench.json");
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var results = new List<WorkbenchCommandResult>();
        if (document.RootElement.TryGetProperty("results", out var array))
        {
            foreach (var item in array.EnumerateArray())
            {
                results.Add(item.Deserialize<WorkbenchCommandResult>(JsonOptions)
                    ?? new WorkbenchCommandResult());
            }
        }
        return results;
    }

    /// <summary>
    /// 冲刷待发队列：成功或确认冲突的命令出队，其余保留待下次重试。
    /// 返回本次成功出队的数量；网络仍然不可用时返回 -1。
    /// </summary>
    public async Task<int> FlushOutboxAsync(CancellationToken cancellationToken = default)
    {
        if (_outbox.Count == 0)
        {
            return 0;
        }
        var batch = _outbox.Take(50).ToList();
        var remainders = _outbox.Skip(50).ToList();
        IReadOnlyList<WorkbenchCommandResult> results;
        try
        {
            results = await SendAsync(batch, cancellationToken);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            return -1;
        }
        var completed = new HashSet<string>(
            results
                .Where(result => result.Status is "done" or "conflict")
                .Select(result => result.OperationId));
        _outbox = remainders
            .Concat(batch.Where(command =>
            {
                var operationId = command.TryGetProperty("operationId", out var id)
                    ? id.GetString()
                    : null;
                return operationId is null || !completed.Contains(operationId);
            }))
            .ToList();
        await PersistOutboxAsync();
        return completed.Count;
    }

    public async Task<WorkbenchSnapshot> FetchSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.BaseUrl.TrimEnd('/')}/openapi/v1/sync/snapshot");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            _options.ApiKey);
        request.Headers.Add("x-mujun-sync-secret", _options.Secret);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<WorkbenchSnapshot>(
            stream,
            JsonOptions,
            cancellationToken) ?? new WorkbenchSnapshot();
    }
}
