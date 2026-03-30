namespace Tnzi.AI.Middleware;

/// <summary>
/// 摘要中间件 — 当对话消息超过阈值时，自动调用 LLM 生成摘要替换旧消息，压缩上下文窗口。
/// </summary>
public class SummarizationMiddleware : IAiMiddleware
{
    private const string DefaultSummaryPrompt =
        "You are a conversation summarizer. Summarize the following conversation messages concisely, " +
        "preserving key facts, decisions, and context that would be needed to continue the conversation. " +
        "Output only the summary text, no preamble.";

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<SummarizationMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Summarization;

    public SummarizationMiddleware(
        IOptionsMonitor<AIOptions> options,
        ITokenEstimator tokenEstimator,
        ILogger<SummarizationMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context,
        AiMiddlewareDelegate next,
        CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
            return await next(context, cancellationToken);

        await TrySummarizeAsync(context, cancellationToken);

        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!context.ShouldSkipMiddleware)
        {
            await TrySummarizeAsync(context, cancellationToken);
        }

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

        if (!ShouldTrigger(messages, opts))
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

        // 获取 IChatClient 用于生成摘要
        var chatClient = context.ServiceProvider.GetService<IChatClient>();
        if (chatClient == null)
        {
            _logger.LogWarning("IChatClient not available, skipping summarization");
            return;
        }

        try
        {
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

    private bool ShouldTrigger(List<ChatMessage> messages, SummarizationOptions opts)
    {
        var trigger = opts.Trigger;

        return trigger.Type switch
        {
            SummarizationTriggerType.Messages => messages.Count >= trigger.MessageThreshold,
            SummarizationTriggerType.Tokens => EstimateTotalTokens(messages) >= trigger.TokenThreshold,
            SummarizationTriggerType.Fraction => opts.ModelContextWindow > 0
                && (double)EstimateTotalTokens(messages) / opts.ModelContextWindow >= trigger.FractionThreshold,
            _ => false
        };
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
}
