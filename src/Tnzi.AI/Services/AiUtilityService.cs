namespace Tnzi.AI.Services;

/// <summary>
/// IAiUtility 默认实现 — 通过 IChatClientFactory 构建精简 ChatClient
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
