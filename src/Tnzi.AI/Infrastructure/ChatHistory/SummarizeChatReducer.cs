namespace Tnzi.AI.Infrastructure.ChatHistory;

/// <summary>
/// 摘要式聊天消息压缩器 - 使用 AI 总结旧的对话历史
/// </summary>
/// <remarks>
/// <para>
/// 实现 IHistoryReducer 接口，通过 AI 生成摘要来压缩对话历史。
/// </para>
/// <para>
/// 摘要策略：
/// <list type="bullet">
/// <item><description>当消息数量或 Token 数超过阈值时触发摘要</description></item>
/// <item><description>保留最近 N 轮完整对话</description></item>
/// <item><description>将旧的对话用 AI 生成摘要替换</description></item>
/// <item><description>摘要作为系统消息注入到上下文中</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class SummarizeChatReducer : IHistoryReducer
{
    private readonly SummarizeOptions _options;
    private readonly IChatClient _chatClient;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<SummarizeChatReducer> _logger;

    // 缓存的摘要（避免重复生成，基于内容 hash 判断是否命中）
    private string? _cachedSummary;
    private int _cachedMessagesHash;

    private const string DefaultSummaryPrompt = """
        Summarize the following conversation. Match the language of the conversation.
        Focus on: key topics, decisions, outstanding questions.
        Output only the summary.

        Conversation to summarize:
        """;

    /// <summary>
    /// 初始化 SummarizeChatReducer
    /// </summary>
    /// <param name="options">摘要配置选项</param>
    /// <param name="chatClient">用于生成摘要的 ChatClient</param>
    /// <param name="tokenEstimator">Token 估算器</param>
    /// <param name="logger">日志记录器</param>
    public SummarizeChatReducer(
        SummarizeOptions options,
        IChatClient chatClient,
        ITokenEstimator tokenEstimator,
        ILogger<SummarizeChatReducer> logger)
    {
        _options = Check.NotNull(options);
        _chatClient = Check.NotNull(chatClient);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 压缩消息列表
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>压缩后的消息列表</returns>
    public async Task<HistoryReductionResult> ReduceAsync(
        List<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var originalCount = messages.Count;
        var originalTokens = messages.Sum(m => _tokenEstimator.Estimate(m.Text ?? ""));

        if (originalCount == 0)
        {
            return new HistoryReductionResult(messages, 0, 0, 0, 0, "Summarize");
        }

        // 检查是否需要压缩
        if (!ShouldSummarize(messages))
        {
            return new HistoryReductionResult(messages, originalCount, messages.Count, originalTokens, originalTokens, "Summarize");
        }

        try
        {
            var result = await SummarizeMessagesAsync(messages, ct);

            _logger.LogDebug(
                "Summarized chat history from {OriginalCount} to {NewCount} messages",
                originalCount, result.Count);

            var reducedTokens = result.Sum(m => _tokenEstimator.Estimate(m.Text ?? ""));
            return new HistoryReductionResult(result, originalCount, result.Count, originalTokens, reducedTokens, "Summarize");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to summarize chat history, returning original messages");
            return new HistoryReductionResult(messages, originalCount, messages.Count, originalTokens, originalTokens, "Summarize");
        }
    }

    /// <summary>
    /// 判断是否需要进行摘要
    /// </summary>
    private bool ShouldSummarize(List<ChatMessage> messages)
    {
        // 排除系统消息
        var nonSystemMessages = messages.Where(m => m.Role != ChatRole.System).ToList();

        // 按消息数量判断
        if (nonSystemMessages.Count > _options.MessageThreshold)
        {
            return true;
        }

        // 按估算 Token 数判断（使用 FormatMessageText 确保工具消息也被计算）
        if (_options.TokenThreshold.HasValue)
        {
            var text = string.Join("\n", nonSystemMessages.Select(ChatHistoryHelper.FormatMessageText));
            var estimatedTokens = _tokenEstimator.Estimate(text, baseOverhead: 0);
            if (estimatedTokens > _options.TokenThreshold.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 执行消息摘要
    /// </summary>
    private async Task<List<ChatMessage>> SummarizeMessagesAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // 分离系统消息
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        var nonSystemMessages = messages.Where(m => m.Role != ChatRole.System).ToList();

        // 按轮次分组
        var turns = ChatHistoryHelper.GroupMessagesByTurns(nonSystemMessages);

        // 确定保留的轮次数量
        var keepTurns = Math.Min(_options.KeepRecentTurns, turns.Count);
        var turnsToSummarize = turns.Count > keepTurns ? turns.Take(turns.Count - keepTurns).ToList() : [];
        var turnsToKeep = turns.Skip(turnsToSummarize.Count).ToList();

        // 如果没有需要摘要的轮次，直接返回
        if (turnsToSummarize.Count == 0)
        {
            return messages;
        }

        // 检查是否可以使用缓存的摘要（基于内容 hash 而非消息数量）
        var messagesToSummarize = turnsToSummarize.SelectMany(t => t).ToList();
        var messagesHash = ComputeMessagesHash(messagesToSummarize);
        if (_cachedSummary != null && _cachedMessagesHash == messagesHash)
        {
            // 使用缓存的摘要
            return BuildResultWithSummary(systemMessages, _cachedSummary, turnsToKeep);
        }

        // 生成新的摘要
        var summary = await GenerateSummaryAsync(messagesToSummarize, cancellationToken);

        // 缓存摘要
        _cachedSummary = summary;
        _cachedMessagesHash = messagesHash;

        return BuildResultWithSummary(systemMessages, summary, turnsToKeep);
    }

    /// <summary>
    /// 生成对话摘要
    /// </summary>
    private async Task<string> GenerateSummaryAsync(
        List<ChatMessage> messagesToSummarize,
        CancellationToken cancellationToken)
    {
        // 构建摘要请求
        var conversationText = FormatMessagesForSummary(messagesToSummarize);
        var summaryPrompt = _options.SummaryPrompt ?? DefaultSummaryPrompt;

        var summaryRequest = new List<ChatMessage>
        {
            new(ChatRole.System, summaryPrompt),
            new(ChatRole.User, conversationText)
        };

        _logger.LogDebug(
            "Generating summary for {MessageCount} messages using model {ModelId}",
            messagesToSummarize.Count, _options.SummaryModelId ?? "default");

        // 调用 AI 生成摘要
        var options = new ChatOptions
        {
            MaxOutputTokens = _options.MaxSummaryTokens
        };

        if (!string.IsNullOrEmpty(_options.SummaryModelId))
        {
            options.ModelId = _options.SummaryModelId;
        }

        var response = await _chatClient.GetResponseAsync(summaryRequest, options, cancellationToken);

        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// 构建包含摘要的结果消息列表
    /// </summary>
    private static List<ChatMessage> BuildResultWithSummary(
        List<ChatMessage> systemMessages,
        string summary,
        List<List<ChatMessage>> turnsToKeep)
    {
        var result = new List<ChatMessage>();

        // 添加原有的系统消息
        result.AddRange(systemMessages);

        // 添加摘要作为系统消息
        if (!string.IsNullOrWhiteSpace(summary))
        {
            result.Add(new ChatMessage(
                ChatRole.System,
                $"[Previous conversation summary]\n{summary}\n[End of summary]"));
        }

        // 添加保留的消息
        foreach (var turn in turnsToKeep)
        {
            result.AddRange(turn);
        }

        return result;
    }

    /// <summary>
    /// 将消息格式化为文本用于摘要
    /// </summary>
    private static string FormatMessagesForSummary(List<ChatMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            var role = message.Role.Value;
            var content = ChatHistoryHelper.FormatMessageText(message);

            sb.AppendLine($"{role}: {content}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 计算消息列表的内容 hash（用于缓存判断）
    /// </summary>
    private static int ComputeMessagesHash(List<ChatMessage> messages)
    {
        var hash = new HashCode();
        foreach (var m in messages)
        {
            hash.Add(ChatHistoryHelper.FormatMessageText(m));
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// 清除缓存的摘要
    /// </summary>
    public void ClearCache()
    {
        _cachedSummary = null;
        _cachedMessagesHash = 0;
    }
}
