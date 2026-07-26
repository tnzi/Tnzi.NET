namespace Tnzi.AI.Middleware;

/// <summary>
/// 摘要中间件 - 当对话消息超过阈值时，自动调用 LLM 生成摘要替换旧消息，压缩上下文窗口。
/// </summary>
public class SummarizationMiddleware : IAiMiddleware
{
    private const string DefaultSummaryPrompt =
        """
        Analyze the conversation and produce a structured summary. Skip sections that don't apply.
        Do NOT use any tools. Output only the summary.

        1. **Primary Request**: What is the user trying to accomplish?
        2. **Key Technical Concepts**: Important technical details, patterns, constraints.
        3. **Files and Code**: Files read/modified, key code changes, current file states.
        4. **Errors and Fixes**: Errors encountered and how they were resolved.
        5. **Problem Solving**: Approaches tried, what worked, what didn't.
        6. **All User Messages**: Chronological list of ALL user messages (preserve intent).
        7. **Pending Tasks**: Outstanding work items, incomplete tasks.
        8. **Current Work**: What was being done when the conversation was paused.
        9. **Next Step**: The immediate next action to take.

        Be factual and specific - include file paths, function names, error messages.
        IMPORTANT: Do NOT call any tools. Output only the summary text.
        """;

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<SummarizationMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Summarization;

    public SummarizationMiddleware(
        IOptionsMonitor<AIOptions> options,
        IChatClientFactory chatClientFactory,
        ITokenEstimator tokenEstimator,
        ILogger<SummarizationMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context,
        AiMiddlewareDelegate next,
        CancellationToken cancellationToken = default)
    {
        ApplyMicroCompact(context);
        await TrySummarizeAsync(context, cancellationToken);

        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ApplyMicroCompact(context);
        await TrySummarizeAsync(context, cancellationToken);

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task TrySummarizeAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue.Summarization;
        if (!opts.Enabled)
            return;

        var messages = context.Messages;
        if (messages.Count == 0)
            return;

        var contextWindow = ResolveContextWindow(opts, context.EffectiveProvider);
        if (!ShouldTrigger(messages, opts, contextWindow))
            return;

        // 分离系统消息和非系统消息
        var systemMessages = new List<ChatMessage>();
        var nonSystemMessages = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.System)
                systemMessages.Add(msg);
            else
                nonSystemMessages.Add(msg);
        }

        // 计算保留的最近消息数
        var keepCount = Math.Min(opts.Keep.KeepLastMessages, nonSystemMessages.Count);
        var messagesToSummarize = nonSystemMessages.Count - keepCount;

        if (messagesToSummarize <= 0)
            return;

        var oldMessages = nonSystemMessages.GetRange(0, messagesToSummarize);

        // 检查待摘要消息的 token 数是否达到最低要求
        var oldTokens = oldMessages.Sum(m => _tokenEstimator.Estimate(m.Text ?? "", baseOverhead: 0));
        if (oldTokens < opts.TrimTokensToSummarize)
            return;

        try
        {
            // Auxiliary sub-run for compact/summarization: uses the same chat client factory as
            // the main agent path so provider configuration, pooling, and model aliases stay consistent.
            var chatClient = _chatClientFactory.GetChatClient(context.Agent.Provider, opts.ModelName ?? context.Agent.Model);
            var summaryText = await GenerateSummaryAsync(chatClient, oldMessages, opts, cancellationToken);

            if (string.IsNullOrWhiteSpace(summaryText))
            {
                _logger.LogWarning("Summarization returned empty result, keeping original messages");
                return;
            }

            // 重组消息：系统消息 + 摘要消息 + 保留的最近消息
            var keptMessages = nonSystemMessages.GetRange(messagesToSummarize, keepCount);
            var summaryMessage = new ChatMessage(ChatRole.System, $"[Conversation summary: {summaryText}]");

            context.Messages.Clear();

            if (opts.Keep.KeepSystemMessages)
            {
                context.Messages.AddRange(systemMessages);
            }

            context.Messages.Add(summaryMessage);
            context.Messages.AddRange(keptMessages);

            _logger.LogDebug(
                "Summarized {OldCount} messages ({OldTokens} tokens) into summary, keeping {KeptCount} recent messages",
                messagesToSummarize, oldTokens, keepCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Summarization failed, keeping original messages");
        }
    }

    private bool ShouldTrigger(List<ChatMessage> messages, SummarizationOptions opts, int contextWindow)
    {
        var trigger = opts.Trigger;

        return trigger.Type switch
        {
            SummarizationTriggerType.Messages => messages.Count >= trigger.MessageThreshold,
            SummarizationTriggerType.Tokens => EstimateTotalTokens(messages) >= trigger.TokenThreshold,
            SummarizationTriggerType.Fraction => contextWindow > 0
                && (double)EstimateTotalTokens(messages) / contextWindow >= trigger.FractionThreshold,
            _ => false
        };
    }

    /// <summary>
    /// Resolve context window size: provider-specific → global SummarizationOptions fallback
    /// </summary>
    private int ResolveContextWindow(SummarizationOptions opts, string? providerName)
    {
        if (!string.IsNullOrEmpty(providerName)
            && _options.CurrentValue.Providers.TryGetValue(providerName, out var providerOpts)
            && providerOpts.ContextWindowSize.HasValue)
        {
            return providerOpts.ContextWindowSize.Value;
        }

        return opts.ModelContextWindow;
    }

    private int EstimateTotalTokens(List<ChatMessage> messages)
    {
        return messages.Sum(m => _tokenEstimator.Estimate(m.Text ?? "", baseOverhead: 0));
    }

    private async Task<string?> GenerateSummaryAsync(
        IChatClient chatClient,
        List<ChatMessage> messagesToSummarize,
        SummarizationOptions opts,
        CancellationToken cancellationToken)
    {
        var prompt = opts.SummaryPrompt ?? DefaultSummaryPrompt;

        var summaryRequest = new List<ChatMessage>
        {
            new(ChatRole.System, prompt)
        };

        // 将待摘要的消息作为上下文
        foreach (var msg in messagesToSummarize)
        {
            summaryRequest.Add(new ChatMessage(msg.Role, msg.Text ?? string.Empty));
        }

        var chatOptions = new ChatOptions();
        if (!string.IsNullOrWhiteSpace(opts.ModelName))
        {
            chatOptions.ModelId = opts.ModelName;
        }

        var response = await chatClient.GetResponseAsync(summaryRequest, chatOptions, cancellationToken);
        return response.Text?.Trim();
    }

    /// <summary>
    /// 应用 MicroCompact - 在每次执行前清理过期工具结果（轻量级上下文压缩）
    /// </summary>
    private void ApplyMicroCompact(AiMiddlewareContext context)
    {
        var opts = _options.CurrentValue.Summarization;
        if (!opts.EnableMicroCompact) return;

        var compacted = MicroCompact(context.Messages, opts.KeepRecentToolResults);
        if (!ReferenceEquals(compacted, context.Messages))
        {
            context.Messages.Clear();
            context.Messages.AddRange(compacted);
            _logger.LogDebug("MicroCompact: cleared old tool results, keeping {KeepCount} recent", opts.KeepRecentToolResults);
        }
    }

    /// <summary>
    /// MicroCompact - 基于消息列表中的位置距离清理过期工具结果。
    /// 保留最近 N 个工具结果消息不清理，其余替换为 "[tool result cleared]"。
    /// 返回新 List（不可变原则）。如果无需修改则返回原引用。
    /// </summary>
    public static List<ChatMessage> MicroCompact(List<ChatMessage> messages, int keepRecentToolResults)
    {
        if (keepRecentToolResults < 0) return messages;

        // 找出所有包含工具结果的消息索引（从后往前计数）
        var toolResultIndices = new List<int>();
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Contents.OfType<FunctionResultContent>().Any(
                frc => frc.Result is string s && s != "[tool result cleared]"))
            {
                toolResultIndices.Add(i);
            }
        }

        // 如果所有工具结果都在保留范围内，不修改
        if (toolResultIndices.Count <= keepRecentToolResults)
            return messages;

        // 需要清理的索引（超出保留范围的）
        var indicesToClear = new HashSet<int>(toolResultIndices.Skip(keepRecentToolResults));

        return messages.Select((msg, idx) =>
        {
            if (!indicesToClear.Contains(idx)) return msg;

            var newContents = msg.Contents.Select<AIContent, AIContent>(c =>
                c is FunctionResultContent frc && frc.Result is string s && s != "[tool result cleared]"
                    ? new FunctionResultContent(frc.CallId, "[tool result cleared]")
                    : c).ToList();
            return new ChatMessage(msg.Role, newContents);
        }).ToList();
    }
}
