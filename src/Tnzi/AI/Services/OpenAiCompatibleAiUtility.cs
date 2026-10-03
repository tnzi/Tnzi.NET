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
/// 调用方自带的提供商（<see cref="AiUtilityCallOptions.Provider"/>）同样不跑守卫：地址是调用方代码
/// 显式交进来的，信任边界归调用方（见 <see cref="AiUtilityInlineProvider"/>）。
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

    /// <summary>
    /// 未设置 <c>TimeoutSeconds</c> 时每次尝试的超时。取值就是此前实际生效的 <see cref="HttpClient"/> 默认值，
    /// 未设置超时的部署行为不变。
    /// </summary>
    private static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(100);

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AiProviderRegistryOptions> _providers;
    private readonly IOptionsMonitor<AiUtilityOptions> _utilityOptions;
    private readonly ILogger<OpenAiCompatibleAiUtility> _logger;
    private readonly TimeProvider _timeProvider;

    public OpenAiCompatibleAiUtility(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AiProviderRegistryOptions> providers,
        IOptionsMonitor<AiUtilityOptions> utilityOptions,
        ILogger<OpenAiCompatibleAiUtility>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _providers = Check.NotNull(providers);
        _utilityOptions = Check.NotNull(utilityOptions);
        _logger = logger ?? NullLogger<OpenAiCompatibleAiUtility>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
    /// <remarks>
    /// <see cref="AiUtilityCallOptions.Provider"/> 设置时完全绕过 <c>AI:Providers</c>；
    /// 两条路径在「解析出目标」之后共用同一段发送、重试与响应解析。
    /// </remarks>
    public async Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(systemPrompt);
        Check.NotNullOrWhiteSpace(userMessage);

        var defaults = _utilityOptions.CurrentValue;
        var target = options?.Provider is { } inline
            ? ResolveInlineTarget(inline, options.Model)
            : ResolveConfiguredTarget(options?.Model ?? defaults.Model);
        if (target == null)
        {
            return null;
        }

        var payload = new ChatCompletionRequest(
            Model: target.Model,
            Messages:
            [
                new ChatCompletionMessage("system", systemPrompt),
                new ChatCompletionMessage("user", userMessage)
            ],
            MaxTokens: options?.MaxTokens ?? defaults.MaxTokens,
            Temperature: options?.Temperature ?? defaults.Temperature);

        try
        {
            return await SendWithRetryAsync(target, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 调用方主动取消应向上传播
        }
        catch (Exception ex)
        {
            // 不把异常对象交给日志：抹不到它。改记抹掉密钥后的全文（含内层异常与堆栈）。
            _logger.LogWarning("IAiUtility.ExecuteAsync failed for provider '{Provider}': {Error}",
                target.Provider.Name, RedactKey(ex.ToString(), target.ApiKey));
            return null;
        }
    }

    /// <summary>
    /// 从 <c>AI:Providers</c> 解析本次调用的目标；解析不出时记 Warning 并返回 <see langword="null"/>。
    /// </summary>
    private ResolvedTarget? ResolveConfiguredTarget(string? requestedModel)
    {
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

        var model = ResolveModel(provider, requestedModel);
        if (model == null)
        {
            _logger.LogWarning(
                "IAiUtility call skipped: no model resolved for provider '{Provider}'. " +
                "Set AI:Providers:{ConfigKey}:DefaultModel or AI:Utility:Model.",
                provider.Name, provider.Name);
            return null;
        }

        if (!TryBuildEndpoint(provider.BaseUrl, out var endpoint))
        {
            _logger.LogWarning(
                "IAiUtility call skipped: provider '{Provider}' BaseUrl '{BaseUrl}' is not a valid http(s) URL.",
                provider.Name, provider.BaseUrl);
            return null;
        }

        return new ResolvedTarget(provider, AiUtilityHttpClientNames.For(provider.Name), endpoint, apiKey, model);
    }

    /// <summary>
    /// 从调用方自带的提供商解析本次调用的目标；不合法时记 Warning 并返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 日志只写 <see cref="AiUtilityInlineProvider.DescribeForLog"/>（主机名）：
    /// 不写地址全文（查询串可能带凭据，地址栏也可能被误填成密钥），更不写密钥。
    /// 模型不查别名字典、不回退 <c>AI:Utility:Model</c> —— 那些名字是给配置提供商定的。
    /// </remarks>
    private ResolvedTarget? ResolveInlineTarget(AiUtilityInlineProvider inline, string? requestedModel)
    {
        var label = inline.DescribeForLog();

        var error = inline.GetValidationError();
        if (error != null)
        {
            _logger.LogWarning("IAiUtility call skipped: caller-supplied provider '{Provider}' is invalid: {Reason}.", label, error);
            return null;
        }

        var model = string.IsNullOrWhiteSpace(requestedModel) ? inline.DefaultModel : requestedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "IAiUtility call skipped: no model resolved for caller-supplied provider '{Provider}'. " +
                "Set AiUtilityCallOptions.Model or AiUtilityInlineProvider.DefaultModel.",
                label);
            return null;
        }

        if (!TryBuildEndpoint(inline.BaseUrl.Trim(), out var endpoint))
        {
            _logger.LogWarning("IAiUtility call skipped: caller-supplied provider '{Provider}' BaseUrl is not a valid http(s) URL.", label);
            return null;
        }

        // 只承载日志标签与超时；密钥随 ResolvedTarget 走，不进这个对象。
        var provider = new AiProviderOptions { Name = label, Enabled = true, TimeoutSeconds = inline.TimeoutSeconds };
        return new ResolvedTarget(provider, AiUtilityHttpClientNames.Inline, endpoint, inline.ApiKey, model);
    }

    /// <summary>
    /// 发起请求并按「可重试失败」判据重试。
    /// </summary>
    /// <remarks>
    /// ★不复用 <c>ResiliencePipelineNames.Default</c>：那条通用管线是给消费应用调外部服务用的，
    /// 框架自身的重试各带自己的失败判据与预算（见该常量的注释）。这里的判据是
    /// 429 / 5xx / 传输层异常 / 超时，且尊重 <c>Retry-After</c>。
    /// </remarks>
    private async Task<string?> SendWithRetryAsync(ResolvedTarget target, ChatCompletionRequest payload, CancellationToken cancellationToken)
    {
        var provider = target.Provider;
        var client = _httpClientFactory.CreateClient(target.ClientName);
        var body = JsonSerializer.Serialize(payload, RequestJsonOptions);

        for (var attempt = 0; ; attempt++)
        {
            using var attemptTimeout = new AttemptTimeout(AttemptTimeoutOf(provider), _timeProvider, cancellationToken);
            var effectiveToken = attemptTimeout.Token;

            TimeSpan? retryDelay;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + target.ApiKey);

                using var response = await client.SendAsync(request, effectiveToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(effectiveToken);
                    return ExtractMessageContent(content, provider.Name);
                }

                retryDelay = ResolveRetryDelay(response, attempt);
                if (retryDelay == null)
                {
                    await LogFailureAsync(response, target, effectiveToken);
                    return null;
                }

                _logger.LogDebug(
                    "Provider '{Provider}' returned {Status}; retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    provider.Name, (int)response.StatusCode, retryDelay.Value.TotalMilliseconds, attempt + 1, MaxRetryAttempts);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetryAttempts)
            {
                retryDelay = BackoffDelay(attempt);
                _logger.LogDebug(
                    "Provider '{Provider}' transport failure; retrying in {Delay}ms (attempt {Attempt}/{Max}): {Error}",
                    provider.Name, retryDelay.Value.TotalMilliseconds, attempt + 1, MaxRetryAttempts,
                    RedactKey(ex.ToString(), target.ApiKey));
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

            // 退避与 Retry-After 换算都走注入的时钟：与每次尝试的超时同一口径，测试与宿主替换时钟时才不会一半快进一半真等。
            await Task.Delay(retryDelay.Value, _timeProvider, cancellationToken);
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
    private TimeSpan? ResolveRetryDelay(HttpResponseMessage response, int attempt)
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
            ?? (retryAfter?.Date is { } date ? date - _timeProvider.GetUtcNow() : null);

        if (hinted is { } delay && delay > TimeSpan.Zero)
        {
            return delay > MaxRetryDelay ? MaxRetryDelay : delay;
        }

        return BackoffDelay(attempt);
    }

    /// <summary>
    /// 超时时长的日志描述。
    /// </summary>
    private static string DescribeTimeout(AiProviderOptions provider)
        => AttemptTimeoutOf(provider).TotalSeconds.ToString(CultureInfo.InvariantCulture) + "s";

    /// <summary>每次尝试的超时：<c>TimeoutSeconds</c>，未设置时 <see cref="DefaultAttemptTimeout"/>。</summary>
    private static TimeSpan AttemptTimeoutOf(AiProviderOptions provider)
        => provider.TimeoutSeconds is { } seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultAttemptTimeout;

    /// <summary>
    /// 把 <paramref name="text"/> 里的密钥换成 <c>***</c>。用在所有要写进日志的第三方文本上
    /// （错误响应体、异常全文）：有的提供商在 401 里回显收到的密钥。
    /// </summary>
    private static string RedactKey(string text, string apiKey)
        => text.Replace(apiKey, "***", StringComparison.Ordinal);

    private static TimeSpan BackoffDelay(int attempt)
        => TimeSpan.FromMilliseconds(RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));

    /// <remarks>
    /// 响应体先抹掉密钥再截断：有的提供商在 401 里回显收到的密钥，先截断可能把它切成半截而漏抹。
    /// </remarks>
    private async Task LogFailureAsync(HttpResponseMessage response, ResolvedTarget target, CancellationToken ct)
    {
        string detail;
        try
        {
            var content = RedactKey(await response.Content.ReadAsStringAsync(ct), target.ApiKey);
            detail = content.Length > 500 ? content[..500] : content;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            detail = "<response body unavailable>";
        }

        _logger.LogWarning(
            "Provider '{Provider}' returned {Status}: {Detail}",
            target.Provider.Name, (int)response.StatusCode, detail);
    }

    /// <summary>
    /// 一次尝试的超时令牌：调用方取消或到时即取消。
    /// </summary>
    /// <remarks>
    /// ★超时由这里<b>独自</b>决定：<c>CoreServicesModule</c> 把本类用到的命名客户端的
    /// <see cref="HttpClient.Timeout"/> 设为无限。否则 HttpClient 默认的 100 秒会先到，
    /// <c>TimeoutSeconds</c> 超过 100 的配置被静默截在 100 秒。
    /// 刻意不在这里改 <c>HttpClient.Timeout</c>：命名客户端由工厂共享，就地改会波及并发请求。
    /// </remarks>
    private sealed class AttemptTimeout : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenSource _linked;

        public AttemptTimeout(TimeSpan timeout, TimeProvider timeProvider, CancellationToken callerToken)
        {
            _timeout = new CancellationTokenSource(timeout, timeProvider);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _timeout.Token);
        }

        public CancellationToken Token => _linked.Token;

        public void Dispose()
        {
            _linked.Dispose();
            _timeout.Dispose();
        }
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
    private static bool TryBuildEndpoint(string? configuredBaseUrl, out Uri endpoint)
    {
        endpoint = null!;

        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? DefaultBaseUrl : configuredBaseUrl.TrimEnd('/');
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

    /// <summary>
    /// 一次调用解析出的目标：日志标签与超时（<paramref name="Provider"/>）、命名客户端、端点、密钥、模型。
    /// </summary>
    /// <remarks><see cref="ToString"/> 被覆写成只输出标签 —— 记录类型默认会把 <paramref name="ApiKey"/> 一起打印出来。</remarks>
    private sealed record ResolvedTarget(AiProviderOptions Provider, string ClientName, Uri Endpoint, string ApiKey, string Model)
    {
        public override string ToString() => Provider.Name;
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
