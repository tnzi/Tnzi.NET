namespace Tnzi.AI.Services;

/// <summary>
/// IAiUtility 默认实现 - 通过 IChatClientFactory 构建精简 ChatClient
/// </summary>
public class AiUtilityService : IAiUtility
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptionsMonitor<AiUtilityOptions> _options;
    private readonly ILogger<AiUtilityService> _logger;

    public AiUtilityService(
        IChatClientFactory chatClientFactory,
        IOptionsMonitor<AiUtilityOptions> options,
        ILogger<AiUtilityService>? logger = null)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _logger = logger ?? NullLogger<AiUtilityService>.Instance;
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

        try
        {
            var defaults = _options.CurrentValue;

            // 模型解析优先级: CallOptions > AiUtilityOptions > Provider 默认
            var model = options?.Model ?? defaults.Model;

            var chatClient = _chatClientFactory.GetChatClient(model: model);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userMessage)
            };

            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = options?.MaxTokens ?? defaults.MaxTokens,
                Temperature = (float?)(options?.Temperature ?? defaults.Temperature)
            };

            var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

            var result = response.Text?.Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (OperationCanceledException)
        {
            throw; // 取消应该向上传播
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IAiUtility.ExecuteAsync failed");
            return null;
        }
    }
}
