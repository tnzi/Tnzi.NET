namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IAiUtility"/> 的核心默认实现 —— 直接向 OpenAI 兼容的
/// <c>/chat/completions</c> 端点发一次请求，零第三方 SDK 依赖。
/// </summary>
/// <remarks>
/// ★存在的理由：让「预置提示词 + 一个问题」这类最常见的 AI 用法不必加载整个
/// <c>Tnzi.AI</c> 模块（那会带来一批实体表、默认控制器和一组权限码）。
/// 实现只用 in-box 的 <see cref="HttpClient"/> 与 <c>System.Text.Json</c>，
/// 核心因此不新增任何 NuGet 依赖 —— 与 <c>Tnzi.Documents</c> 用 in-box
/// <c>ClientWebSocket</c> 走 CDP 是同一取舍。
/// <para>
/// 覆盖面：OpenAI / Azure OpenAI / DeepSeek / Kimi / GLM / MiniMax / 通义 /
/// Ollama / vLLM 等所有说 OpenAI 协议的端点，以及 Anthropic 的 OpenAI 兼容端点。
/// 需要原生 Anthropic 协议、思考内容、降级链时加载 <c>Tnzi.AI</c>，
/// 它会用基于 <c>Microsoft.Extensions.AI</c> 的实现替换本类。
/// </para>
/// <para>
/// ★刻意不对 <c>BaseUrl</c> 跑 <see cref="Tnzi.Http.EgressGuard"/>：该值来自
/// <c>appsettings</c>，是部署方自己声明的信任边界，不是不可信输入；而 SSRF 守卫会
/// 拒绝 loopback 与私有网段，那恰恰是 Ollama / vLLM 等本地模型的正常部署形态。
/// 守卫属于「URL 由运行时输入决定」的场景（MCP 服务器注册、A2A 等，均在 <c>Tnzi.AI</c>）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class OpenAiCompatibleAiUtility : IAiUtility
{
    /// <summary>BaseUrl 未配置时使用的官方端点。</summary>
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    /// <summary>失败重试次数上限（不含首次尝试）。</summary>
    private const int MaxRetryAttempts = 2;

    /// <summary>重试基准退避（指数增长）。</summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>重试等待的上限，避免尊重 Retry-After 时被拖住整个请求。</summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AiProviderRegistryOptions> _providers;
    private readonly IOptionsMonitor<AiUtilityOptions> _utilityOptions;
    private readonly ILogger<OpenAiCompatibleAiUtility> _logger;

    public OpenAiCompatibleAiUtility(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AiProviderRegistryOptions> providers,
        IOptionsMonitor<AiUtilityOptions> utilityOptions,
        ILogger<OpenAiCompatibleAiUtility>? logger = null)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _providers = Check.NotNull(providers);
        _utilityOptions = Check.NotNull(utilityOptions);
        _logger = logger ?? NullLogger<OpenAiCompatibleAiUtility>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 只看配置来源：有一个已启用、且拿得到 API Key 的提供商就算可用。
    /// 刻意不缓存 —— <see cref="IOptionsMonitor{TOptions}"/> 的读取是内存字典查找，
    /// 而缓存会让配置热更新后本属性继续报旧答案。
    /// </remarks>
    public bool IsAvailable
    {
        get
        {
            var provider = _providers.CurrentValue.ResolveEnabled();
            return provider != null && !string.IsNullOrWhiteSpace(ResolveApiKey(provider));
        }
    }

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(systemPrompt);
        Check.NotNullOrWhiteSpace(userMessage);

        var registry = _providers.CurrentValue;
        var provider = registry.ResolveEnabled();
        if (provider == null)
        {
            _logger.LogWarning(
                "IAiUtility call skipped: no enabled provider named '{Provider}' under AI:Providers. " +
                "Configure one, or load the Tnzi.AI module for database-backed providers.",
                registry.DefaultProvider);
            return null;
        }

        var apiKey = ResolveApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "IAiUtility call skipped: provider '{Provider}' has no ApiKey. " +
                "Set AI:Providers:{ConfigKey}:ApiKey or the environment variable {EnvVar}.",
                provider.Name, provider.Name, ApiKeyEnvironmentVariable(provider.Name));
            return null;
        }

        var defaults = _utilityOptions.CurrentValue;
        var model = ResolveModel(provider, options?.Model ?? defaults.Model);
        if (model == null)
        {
            _logger.LogWarning(
                "IAiUtility call skipped: no model resolved for provider '{Provider}'. " +
                "Set AI:Providers:{ConfigKey}:DefaultModel or AI:Utility:Model.",
                provider.Name, provider.Name);
            return null;
        }

        if (!TryBuildEndpoint(provider, out var endpoint))
        {
            _logger.LogWarning(
                "IAiUtility call skipped: provider '{Provider}' BaseUrl '{BaseUrl}' is not a valid http(s) URL.",
                provider.Name, provider.BaseUrl);
            return null;
        }

        var payload = new ChatCompletionRequest(
            Model: model,
            Messages:
            [
                new ChatCompletionMessage("system", systemPrompt),
                new ChatCompletionMessage("user", userMessage)
            ],
            MaxTokens: options?.MaxTokens ?? defaults.MaxTokens,
            Temperature: options?.Temperature ?? defaults.Temperature);

        try
        {
            return await SendWithRetryAsync(provider, endpoint, apiKey, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 调用方主动取消应向上传播
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IAiUtility.ExecuteAsync failed for provider '{Provider}'", provider.Name);
            return null;
        }
    }

    /// <summary>
    /// 发起请求并按「可重试失败」判据重试。
    /// </summary>
    /// <remarks>
    /// ★不复用 <c>ResiliencePipelineNames.Default</c>：那条通用管线是给消费应用调外部服务用的，
    /// 框架自身的重试各带自己的失败判据与预算（见该常量的注释）。这里的判据是
    /// 429 / 5xx / 传输层异常 / 超时，且尊重 <c>Retry-After</c>。
    /// </remarks>
    private async Task<string?> SendWithRetryAsync(
        AiProviderOptions provider,
        Uri endpoint,
        string apiKey,
        ChatCompletionRequest payload,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(AiUtilityHttpClientNames.For(provider.Name));
        var body = JsonSerializer.Serialize(payload, RequestJsonOptions);

        for (var attempt = 0; ; attempt++)
        {
            using var timeoutCts = CreateTimeoutSource(provider, cancellationToken);
            var effectiveToken = timeoutCts?.Token ?? cancellationToken;

            TimeSpan? retryDelay;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

                using var response = await client.SendAsync(request, effectiveToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(effectiveToken);
                    return ExtractMessageContent(content, provider.Name);
                }

                retryDelay = ResolveRetryDelay(response, attempt);
                if (retryDelay == null)
                {
                    await LogFailureAsync(response, provider.Name, effectiveToken);
                    return null;
                }

                _logger.LogDebug(
                    "Provider '{Provider}' returned {Status}; retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    provider.Name, (int)response.StatusCode, retryDelay.Value.TotalMilliseconds, attempt + 1, MaxRetryAttempts);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetryAttempts)
            {
                retryDelay = BackoffDelay(attempt);
                _logger.LogDebug(ex,
                    "Provider '{Provider}' transport failure; retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    provider.Name, retryDelay.Value.TotalMilliseconds, attempt + 1, MaxRetryAttempts);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetryAttempts)
            {
                // 超时（本地 CTS 触发），不是调用方取消
                retryDelay = BackoffDelay(attempt);
                _logger.LogDebug(
                    "Provider '{Provider}' timed out after {Timeout}; retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    provider.Name, DescribeTimeout(provider), retryDelay.Value.TotalMilliseconds, attempt + 1, MaxRetryAttempts);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Provider '{Provider}' timed out after {Timeout} and retries are exhausted",
                    provider.Name, DescribeTimeout(provider));
                return null;
            }

            await Task.Delay(retryDelay.Value, cancellationToken);
        }
    }

    /// <summary>
    /// 从 OpenAI 兼容响应中取出首个 choice 的文本内容。
    /// </summary>
    private string? ExtractMessageContent(string responseBody, string providerName)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("Provider '{Provider}' returned a response without any choices", providerName);
                return null;
            }

            if (!choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                // 纯推理模型可能只回 reasoning_content 而 content 为空，视同无结果。
                _logger.LogWarning("Provider '{Provider}' returned a choice without text content", providerName);
                return null;
            }

            var text = content.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Provider '{Provider}' returned a non-JSON response", providerName);
            return null;
        }
    }

    /// <summary>
    /// 返回本次失败应等待的时长；返回 <see langword="null"/> 表示不可重试或次数已用尽。
    /// </summary>
    private static TimeSpan? ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (attempt >= MaxRetryAttempts)
        {
            return null;
        }

        var status = (int)response.StatusCode;
        var retryable = status == 429 || status >= 500;
        if (!retryable)
        {
            return null;
        }

        var retryAfter = response.Headers.RetryAfter;
        var hinted = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (hinted is { } delay && delay > TimeSpan.Zero)
        {
            return delay > MaxRetryDelay ? MaxRetryDelay : delay;
        }

        return BackoffDelay(attempt);
    }

    /// <summary>
    /// 超时时长的日志描述。
    /// </summary>
    /// <remarks>
    /// 未配置 <c>TimeoutSeconds</c> 时超时仍会发生 —— 由 <see cref="HttpClient"/> 自己的默认
    /// 超时抛出，形态同样是一个 <see cref="OperationCanceledException"/>。此时直接打印
    /// <c>null</c> 会得到「timed out after s」这种把人指向配置项的空洞消息。
    /// </remarks>
    private static string DescribeTimeout(AiProviderOptions provider)
        => provider.TimeoutSeconds is { } seconds
            ? seconds.ToString(CultureInfo.InvariantCulture) + "s"
            : "the HttpClient default timeout";

    private static TimeSpan BackoffDelay(int attempt)
        => TimeSpan.FromMilliseconds(RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));

    private async Task LogFailureAsync(HttpResponseMessage response, string providerName, CancellationToken ct)
    {
        string detail;
        try
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            detail = content.Length > 500 ? content[..500] : content;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            detail = "<response body unavailable>";
        }

        _logger.LogWarning(
            "Provider '{Provider}' returned {Status}: {Detail}",
            providerName, (int)response.StatusCode, detail);
    }

    /// <summary>
    /// 为本次请求建立超时令牌源；未配置超时时返回 <see langword="null"/>（沿用 HttpClient 默认值）。
    /// </summary>
    /// <remarks>
    /// 刻意不改 <c>HttpClient.Timeout</c>：命名客户端由工厂共享，就地改超时会波及并发请求。
    /// </remarks>
    private static CancellationTokenSource? CreateTimeoutSource(AiProviderOptions provider, CancellationToken ct)
    {
        if (provider.TimeoutSeconds is not { } seconds || seconds <= 0)
        {
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    /// <summary>
    /// 解析模型名：先查别名字典，再回退提供商默认模型。
    /// </summary>
    private static string? ResolveModel(AiProviderOptions provider, string? requested)
    {
        if (requested != null && provider.Models?.TryGetValue(requested, out var aliased) == true)
        {
            return aliased;
        }

        var model = requested ?? provider.DefaultModel;
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    /// <summary>
    /// 解析 API Key：配置值优先，留空时回退环境变量。
    /// </summary>
    private static string? ResolveApiKey(AiProviderOptions provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return provider.ApiKey;
        }

        return Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable(provider.Name));
    }

    private static string ApiKeyEnvironmentVariable(string providerName)
        => "AI__" + providerName.ToUpperInvariant() + "__APIKEY";

    /// <summary>
    /// 拼出 <c>/chat/completions</c> 端点；BaseUrl 非法时返回 <see langword="false"/>。
    /// </summary>
    private static bool TryBuildEndpoint(AiProviderOptions provider, out Uri endpoint)
    {
        endpoint = null!;

        var baseUrl = string.IsNullOrWhiteSpace(provider.BaseUrl) ? DefaultBaseUrl : provider.BaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl + "/chat/completions", UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        endpoint = uri;
        return true;
    }

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatCompletionMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens,
        [property: JsonPropertyName("temperature")] double? Temperature);

    private sealed record ChatCompletionMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
