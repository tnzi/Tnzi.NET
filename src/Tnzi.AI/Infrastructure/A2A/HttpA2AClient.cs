namespace Tnzi.AI.Infrastructure.A2A;

/// <summary>
/// 基于 HTTP 的 A2A 客户端实现 — 通过 HTTP 协议与远程 Agent 通信
/// </summary>
[ExperimentalApi(Reason = "A2A protocol is in preview")]
public class HttpA2AClient : IA2AClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpA2AClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HttpA2AClient(IHttpClientFactory httpClientFactory, ILogger<HttpA2AClient> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IAgentCard> DiscoverAsync(string endpoint, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(endpoint);

        var client = _httpClientFactory.CreateClient();
        var url = BuildUrl(endpoint, ".well-known/agent-card");

        _logger.LogDebug("Discovering agent card at {Url}", url);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var card = JsonSerializer.Deserialize<AgentCard>(json, JsonOptions);

        return card ?? throw new InvalidOperationException("Failed to deserialize agent card from response.");
    }

    /// <inheritdoc />
    public async Task<A2AResponse> SendTaskAsync(string endpoint, A2ATaskRequest request, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(endpoint);
        Check.NotNull(request);

        var client = _httpClientFactory.CreateClient();
        var url = BuildUrl(endpoint, "tasks");

        _logger.LogDebug("Sending task {TaskId} to {Url}", request.TaskId, url);

        var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<A2AResponse>(json, JsonOptions);

        return result ?? throw new InvalidOperationException("Failed to deserialize A2A response.");
    }

    /// <inheritdoc />
    public async Task<A2AResponse> GetTaskStatusAsync(string endpoint, string taskId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(endpoint);
        Check.NotNullOrWhiteSpace(taskId);

        var client = _httpClientFactory.CreateClient();
        var url = BuildUrl(endpoint, $"tasks/{taskId}");

        _logger.LogDebug("Getting task status for {TaskId} from {Url}", taskId, url);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<A2AResponse>(json, JsonOptions);

        return result ?? throw new InvalidOperationException("Failed to deserialize A2A response.");
    }

    /// <summary>
    /// 构建完整 URL — 确保 endpoint 以 / 结尾并拼接 path
    /// </summary>
    private static string BuildUrl(string endpoint, string path)
    {
        var baseUrl = endpoint.TrimEnd('/');
        return $"{baseUrl}/{path}";
    }
}
