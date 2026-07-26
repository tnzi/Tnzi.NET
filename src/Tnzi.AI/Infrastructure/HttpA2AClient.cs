namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 基于 HTTP 的 A2A 客户端实现 - 通过 HTTP 协议与远程 Agent 通信
/// </summary>
[ExperimentalApi(Reason = "A2A protocol is in preview")]
public class HttpA2AClient : IA2AClient
{
    /// <summary>Named HttpClient registered in AIModule with AllowAutoRedirect = false.</summary>
    internal const string HttpClientName = "Tnzi.AI.A2A";

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

        var url = BuildUrl(endpoint, ".well-known/agent-card");

        var blockReason = await EgressGuard.CheckAsync(url, ct);
        if (blockReason != null)
        {
            _logger.LogWarning("A2A discover blocked by egress guard: {Reason}", blockReason);
            throw new InvalidOperationException($"A2A request blocked: {blockReason}");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        _logger.LogDebug("Discovering agent card at {Url}", url);

        var response = await client.GetAsync(url, ct);

        if (IsRedirect(response.StatusCode))
        {
            _logger.LogWarning("A2A discover received redirect {StatusCode} - redirect following is disabled to prevent SSRF", (int)response.StatusCode);
            throw new InvalidOperationException($"A2A discover received unexpected redirect ({(int)response.StatusCode}). Redirect following is disabled.");
        }

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

        var url = BuildUrl(endpoint, "tasks");

        var blockReason = await EgressGuard.CheckAsync(url, ct);
        if (blockReason != null)
        {
            _logger.LogWarning("A2A send-task blocked by egress guard: {Reason}", blockReason);
            return new A2AResponse { TaskId = request.TaskId, Status = "failed", Error = $"A2A request blocked: {blockReason}" };
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        _logger.LogDebug("Sending task {TaskId} to {Url}", request.TaskId, url);

        var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(url, content, ct);

        if (IsRedirect(response.StatusCode))
        {
            _logger.LogWarning("A2A send-task received redirect {StatusCode} - treating as failure to prevent SSRF", (int)response.StatusCode);
            return new A2AResponse { TaskId = request.TaskId, Status = "failed", Error = $"A2A request received unexpected redirect ({(int)response.StatusCode}). Redirect following is disabled." };
        }

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

        var url = BuildUrl(endpoint, $"tasks/{taskId}");

        var blockReason = await EgressGuard.CheckAsync(url, ct);
        if (blockReason != null)
        {
            _logger.LogWarning("A2A get-task-status blocked by egress guard: {Reason}", blockReason);
            return new A2AResponse { TaskId = taskId, Status = "failed", Error = $"A2A request blocked: {blockReason}" };
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        _logger.LogDebug("Getting task status for {TaskId} from {Url}", taskId, url);

        var response = await client.GetAsync(url, ct);

        if (IsRedirect(response.StatusCode))
        {
            _logger.LogWarning("A2A get-task-status received redirect {StatusCode} - treating as failure to prevent SSRF", (int)response.StatusCode);
            return new A2AResponse { TaskId = taskId, Status = "failed", Error = $"A2A request received unexpected redirect ({(int)response.StatusCode}). Redirect following is disabled." };
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<A2AResponse>(json, JsonOptions);

        return result ?? throw new InvalidOperationException("Failed to deserialize A2A response.");
    }

    private static bool IsRedirect(System.Net.HttpStatusCode statusCode)
        => (int)statusCode is >= 300 and <= 399;

    /// <summary>
    /// 构建完整 URL - 确保 endpoint 以 / 结尾并拼接 path，并拒绝非 http/https 协议
    /// </summary>
    private static string BuildUrl(string endpoint, string path)
    {
        var trimmed = endpoint.TrimEnd('/');

        // Reject non-http(s) schemes early, before DNS resolution
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Scheme '{uri.Scheme}' is not allowed in A2A endpoint. Only http and https are permitted.", nameof(endpoint));
        }

        return $"{trimmed}/{path}";
    }
}
