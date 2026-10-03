namespace Tnzi.AI.Services;

/// <summary>
/// IAiUtility 默认实现 - 通过 IChatClientFactory 构建精简 ChatClient
/// </summary>
public class AiUtilityService : IAiUtility
{
    /// <summary>调用方自带提供商走的协议实现名（OpenAI 兼容）。</summary>
    private const string InlineProtocol = "OpenAI";

    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptionsMonitor<AiUtilityOptions> _options;
    private readonly ILogger<AiUtilityService> _logger;
    private readonly IChatClientProvider? _inlineClientProvider;

    public AiUtilityService(
        IChatClientFactory chatClientFactory,
        IOptionsMonitor<AiUtilityOptions> options,
        ILogger<AiUtilityService>? logger = null,
        IEnumerable<IChatClientProvider>? chatClientProviders = null)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _logger = logger ?? NullLogger<AiUtilityService>.Instance;
        // 与 ChatClientFactory 的索引规则一致：同名时后注册的覆盖先注册的
        _inlineClientProvider = chatClientProviders?.LastOrDefault(
            p => string.Equals(p.ProviderName, InlineProtocol, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 只看配置来源的提供商（<c>IChatClientFactory.GetAvailableProviders</c>）。
    /// 数据库里登记的 <c>Provider</c> 实体不计入 —— 本属性是同步的、不查库，
    /// 于是「只在 admin 里配了提供商」的部署会被低报为不可用。方向刻意如此：
    /// 少显示一个本可用的入口，好过显示一个点了没反应的入口。
    /// <para>
    /// 已启用的提供商必有 API Key 是启动期保证的（<c>AIOptionsValidator</c> 对
    /// <c>Enabled</c> 却缺 <c>ApiKey</c> 的条目直接 fail-fast），故此处无需再查。
    /// </para>
    /// </remarks>
    public bool IsAvailable => _chatClientFactory.GetAvailableProviders().Count > 0;

    public async Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(systemPrompt);
        Check.NotNullOrWhiteSpace(userMessage);

        if (options?.Provider is { } inline)
        {
            return await ExecuteInlineAsync(systemPrompt, userMessage, options, inline, cancellationToken);
        }

        try
        {
            var defaults = _options.CurrentValue;

            // 模型解析优先级: CallOptions > AiUtilityOptions > Provider 默认
            var model = options?.Model ?? defaults.Model;

            var chatClient = _chatClientFactory.GetChatClient(model: model);

            var response = await chatClient.GetResponseAsync(
                BuildMessages(systemPrompt, userMessage), BuildChatOptions(options, defaults), cancellationToken);

            var result = response.Text?.Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 调用方主动取消应向上传播；超时（HttpClient / 弹性管线抛的 OCE）是失败，按契约返回 null
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IAiUtility.ExecuteAsync failed");
            return null;
        }
    }

    /// <summary>
    /// 用调用方自带的提供商执行一次请求：不经 <see cref="IChatClientFactory"/>（它只认配置与数据库登记的提供商），
    /// 直接用 OpenAI 兼容的 <see cref="IChatClientProvider"/> 现建一个客户端，用完即释放。
    /// </summary>
    /// <remarks>
    /// ★不缓存客户端：客户端在创建时固化密钥，缓存它就等于缓存了调用方的密钥。
    /// <para>
    /// 失败日志不带异常对象，改带<b>抹掉密钥后</b>的异常全文（含内层异常与堆栈）：SDK 把错误响应体写进异常消息，
    /// 而有的提供商会在 401 里回显收到的密钥 —— 整个异常对象进日志时没有地方能把它抹掉。
    /// </para>
    /// <para>
    /// 校验与模型解析规则与核心实现逐字相同（见 <see cref="AiUtilityInlineProvider"/>），
    /// 加载本模块不改变自带提供商的行为，只换掉发请求的那一层。
    /// </para>
    /// </remarks>
    private async Task<string?> ExecuteInlineAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions options,
        AiUtilityInlineProvider inline,
        CancellationToken cancellationToken)
    {
        var label = inline.DescribeForLog();

        var error = inline.GetValidationError();
        if (error != null)
        {
            _logger.LogWarning("IAiUtility call skipped: caller-supplied provider '{Provider}' is invalid: {Reason}.", label, error);
            return null;
        }

        var model = string.IsNullOrWhiteSpace(options.Model) ? inline.DefaultModel : options.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "IAiUtility call skipped: no model resolved for caller-supplied provider '{Provider}'. " +
                "Set AiUtilityCallOptions.Model or AiUtilityInlineProvider.DefaultModel.",
                label);
            return null;
        }

        if (_inlineClientProvider == null)
        {
            _logger.LogWarning(
                "IAiUtility call skipped: caller-supplied provider '{Provider}' needs an IChatClientProvider named '{Protocol}', and none is registered.",
                label, InlineProtocol);
            return null;
        }

        var providerOptions = new ProviderOptions
        {
            Name = label,
            Enabled = true,
            ApiKey = inline.ApiKey,
            BaseUrl = inline.BaseUrl.Trim(),
            DefaultModel = model,
            TimeoutSeconds = inline.TimeoutSeconds,
            // 不走按提供商名分的 Polly 客户端：自带提供商不在配置里，只能落到共用的回退管线，
            // 让互不相干的调用方端点共用一个熔断器不对（一家的 5xx 会把别家一起熔断）。改用核心注册的 Inline 客户端
            // （HttpClient.Timeout 无限），超时由 TimeoutSeconds / SDK 的 NetworkTimeout 决定，重试由 SDK 自带。
            HttpClientName = AiUtilityHttpClientNames.Inline
        };

        try
        {
            using var chatClient = _inlineClientProvider.CreateChatClient(providerOptions, model);

            var response = await chatClient.GetResponseAsync(
                BuildMessages(systemPrompt, userMessage), BuildChatOptions(options, _options.CurrentValue), cancellationToken);

            var result = response.Text?.Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 调用方主动取消应向上传播
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "IAiUtility.ExecuteAsync failed for caller-supplied provider '{Provider}': {Error}",
                label, inline.Redact(ex.ToString()));
            return null;
        }
    }

    private static List<ChatMessage> BuildMessages(string systemPrompt, string userMessage) =>
    [
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, userMessage)
    ];

    private static ChatOptions BuildChatOptions(AiUtilityCallOptions? options, AiUtilityOptions defaults) => new()
    {
        MaxOutputTokens = options?.MaxTokens ?? defaults.MaxTokens,
        Temperature = (float?)(options?.Temperature ?? defaults.Temperature)
    };
}
