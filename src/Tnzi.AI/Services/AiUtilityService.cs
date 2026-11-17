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
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    public AiUtilityService(
        IChatClientFactory chatClientFactory,
        IOptionsMonitor<AiUtilityOptions> options,
        ILogger<AiUtilityService>? logger = null,
        IEnumerable<IChatClientProvider>? chatClientProviders = null,
        IOptionsMonitor<AIOptions>? aiOptions = null)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _logger = logger ?? NullLogger<AiUtilityService>.Instance;
        // 与 ChatClientFactory 的索引规则一致：同名时后注册的覆盖先注册的
        _inlineClientProvider = chatClientProviders?.LastOrDefault(
            p => string.Equals(p.ProviderName, InlineProtocol, StringComparison.OrdinalIgnoreCase));
        _aiOptions = aiOptions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 问的是 <see cref="ExecuteAsync"/> 不带提供商时会用的那一个：<c>AI:DefaultProvider</c> 经
    /// <see cref="IChatClientFactory.IsProviderAvailable"/> 解析，与 <see cref="IChatClientFactory.GetChatClient"/>
    /// 同一口径 —— 调用方租户的启用行优先、其次 System 行（无租户上下文时只看 System 行）、最后 <c>AI:Providers</c>。
    /// 于是只在管理端登记的提供商也算可用，而配置里有别的提供商、默认名却解析不到时不算。
    /// <para>
    /// 代价：工厂按（租户, 名字）缓存数据库解析结果 60 秒（未命中也缓存），每次读本属性是一次作用域创建加一次字典查找，
    /// 库每分钟至多查一次；管理端增删改提供商时 <see cref="IChatClientFactory.InvalidateProvider"/> 清掉这个名字的缓存，
    /// 刚登记的提供商下一次读就算数。
    /// </para>
    /// <para>
    /// 解析过程中的任何异常都答 <see langword="false"/>、不向外抛：调用方拿它决定显示什么，一个查询属性不该让请求失败。
    /// 数据库读失败时工厂本身已回落到配置，这里只兜工厂之外的意外（例如宿主关闭期间作用域已释放）。
    /// </para>
    /// </remarks>
    public bool IsAvailable
    {
        get
        {
            try
            {
                return _chatClientFactory.IsProviderAvailable(_aiOptions?.CurrentValue.DefaultProvider ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IAiUtility.IsAvailable could not resolve the default provider; reporting unavailable");
                return false;
            }
        }
    }

    public async Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteForResponseAsync(systemPrompt, userMessage, options, cancellationToken);
        return string.IsNullOrEmpty(response?.Text) ? null : response.Text;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 提供商是这次调用解析到的配置名（未指定时即 <c>AI:DefaultProvider</c>，与 <c>IChatClientFactory</c> 同一规则），
    /// 模型取响应自报的模型、没报时取请求的模型；调用方自带提供商时提供商名是它的日志描述。
    /// </remarks>
    public async Task<AiUtilityResponse?> ExecuteForResponseAsync(
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

            var provider = _aiOptions?.CurrentValue.DefaultProvider;
            return ToResponse(response, provider, model ?? _chatClientFactory.GetDefaultModel(provider));
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
    private async Task<AiUtilityResponse?> ExecuteInlineAsync(
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

            return ToResponse(response, label, model);
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

    /// <summary>
    /// 把一次 MEAI 响应投影成 <see cref="AiUtilityResponse"/>：文本、停止原因、用量、实际模型。
    /// </summary>
    /// <remarks>
    /// 响应里带着工具调用时一律报 <see cref="AiUtilityFinishReason.ToolCalls"/>：utility 调用不给工具，
    /// 模型仍要调就说明它没按要求作答，有的提供商此时的停止原因写的却是 stop。
    /// </remarks>
    internal static AiUtilityResponse ToResponse(ChatResponse response, string? provider, string? requestedModel)
    {
        var callsTools = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any();
        var finishReason = callsTools
            ? AiUtilityFinishReason.ToolCalls
            : response.FinishReason switch
            {
                { } r when r == ChatFinishReason.Stop => AiUtilityFinishReason.Stop,
                { } r when r == ChatFinishReason.Length => AiUtilityFinishReason.Length,
                { } r when r == ChatFinishReason.ToolCalls => AiUtilityFinishReason.ToolCalls,
                { } r when r == ChatFinishReason.ContentFilter => AiUtilityFinishReason.ContentFilter,
                _ => AiUtilityFinishReason.Unknown
            };

        return new AiUtilityResponse
        {
            Text = response.Text?.Trim() ?? string.Empty,
            FinishReason = finishReason,
            Provider = string.IsNullOrWhiteSpace(provider) ? null : provider,
            Model = string.IsNullOrWhiteSpace(response.ModelId) ? requestedModel : response.ModelId,
            InputTokens = ToInt(response.Usage?.InputTokenCount),
            OutputTokens = ToInt(response.Usage?.OutputTokenCount),
            CachedInputTokens = ToInt(response.Usage?.CachedInputTokenCount)
        };
    }

    private static int? ToInt(long? value) => value is { } v ? (int)Math.Clamp(v, 0, int.MaxValue) : null;
}
