namespace Tnzi.AI.Services;

/// <summary>
/// 后续建议生成服务 — 基于对话上下文生成语言感知的后续问题
/// </summary>
public class SuggestionService : ISuggestionService
{
    private readonly IAiUtility _aiUtility;
    private readonly IAgentThreadInternalService _threadService;
    private readonly IOptions<SuggestionOptions> _options;
    private readonly ILogger<SuggestionService> _logger;

    public SuggestionService(
        IAiUtility aiUtility,
        IAgentThreadInternalService threadService,
        IOptions<SuggestionOptions> options,
        ILogger<SuggestionService> logger)
    {
        _aiUtility = Check.NotNull(aiUtility);
        _threadService = Check.NotNull(threadService);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<List<string>> GenerateAsync(Guid threadId, int count = 3, CancellationToken ct = default)
    {
        try
        {
            // 1. 加载最近消息
            var messages = await _threadService.GetMessageHistoryAsync(threadId, 6, ct);
            if (messages is not { Count: > 0 })
                return [];

            // 2. 检测用户语言
            var userContents = messages
                .Where(m => m.Role == ChatRole.User)
                .SelectMany(m => m.Contents.OfType<TextContent>())
                .Select(tc => tc.Text ?? "")
                .ToList();
            var isChinese = userContents.Any(ContainsChinese);

            // 3. 构建提示词
            var config = _options.Value;
            var effectiveCount = count > 0 ? count : config.Count;
            var (systemPrompt, userMessage) = BuildPrompt(messages, isChinese, effectiveCount, config);

            // 4. 调用 LLM（轻量级，不使用 tools/middleware）
            var callOptions = config.ModelName != null
                ? new AiUtilityCallOptions { Model = config.ModelName }
                : null;
            var response = await _aiUtility.ExecuteAsync(systemPrompt, userMessage, callOptions, ct);
            if (string.IsNullOrWhiteSpace(response))
                return [];

            // 5. 解析 JSON 数组（处理 markdown 代码围栏）
            var suggestions = ParseSuggestions(response);

            // 6. 清理和截断
            var maxChars = isChinese ? config.MaxCharsCn : config.MaxWordsEn * 6; // 近似字符
            return suggestions
                .Select(s => s.Trim().Truncate(maxChars))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(effectiveCount)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate suggestions for thread {ThreadId}", threadId);
            return [];
        }
    }

    private static (string systemPrompt, string userMessage) BuildPrompt(
        IList<ChatMessage> messages, bool isChinese, int count, SuggestionOptions config)
    {
        var conversationSummary = new StringBuilder();
        foreach (var msg in messages.TakeLast(6))
        {
            var role = msg.Role == ChatRole.User ? "User" : "Assistant";
            var text = string.Join("", msg.Contents.OfType<TextContent>().Select(tc => tc.Text ?? ""));
            var content = text.Length > 200 ? text[..200] + "..." : text;
            conversationSummary.AppendLine($"{role}: {content}");
        }

        var languageInstruction = isChinese
            ? "Generate suggestions in Chinese (中文). Each suggestion should be at most 40 Chinese characters."
            : $"Generate suggestions in English. Each suggestion should be at most {config.MaxWordsEn} words.";

        var systemPrompt = "You are a helpful assistant that generates follow-up questions based on conversation context. " +
                           "Output ONLY a JSON array of strings, nothing else.";

        var userMessage = $"""
            Based on the following conversation, generate exactly {count} follow-up questions or suggestions that the user might want to ask next.

            {languageInstruction}

            Requirements:
            - Questions should be natural, relevant, and diverse
            - Cover different aspects of the conversation topic
            - Be actionable and specific (not vague)
            - Output ONLY a JSON array of strings, nothing else

            Conversation:
            {conversationSummary}

            Output (JSON array only):
            """;

        return (systemPrompt, userMessage);
    }

    private static List<string> ParseSuggestions(string response)
    {
        // 去除 markdown 代码围栏
        var cleaned = AiTextHelper.StripCodeFence(response);

        try
        {
            return JsonSerializer.Deserialize<List<string>>(cleaned, TnziJsonDefaults.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ContainsChinese(string text) => AiTextHelper.ContainsChinese(text);
}
