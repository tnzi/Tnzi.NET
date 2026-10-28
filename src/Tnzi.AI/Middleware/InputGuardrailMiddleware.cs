namespace Tnzi.AI.Middleware;

/// <summary>
/// 输入 Guardrail 中间件 - Before only: 检查用户输入是否安全
/// </summary>
public class InputGuardrailMiddleware : IAiMiddleware
{
    private readonly GuardrailRunner _guardrailRunner;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<InputGuardrailMiddleware> _logger;

    public int Order => AiMiddlewareOrders.InputGuardrail;

    public InputGuardrailMiddleware(
        GuardrailRunner guardrailRunner,
        ILogger<InputGuardrailMiddleware> logger,
        IEventBus? eventBus = null)
    {
        _guardrailRunner = Check.NotNull(guardrailRunner);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        try
        {
            var rejection = await GuardUserTurnAsync(context, cancellationToken);

            if (rejection != null)
            {
                _logger.LogWarning("Input rejected by guardrail {GuardrailName}: {Reason}",
                    rejection.GuardrailName, rejection.Reason);

                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", rejection.Reason ?? "Input rejected", GuardrailDirections.Input);

                return new AgentRunResult
                {
                    Response = rejection.Reason ?? "Input rejected by guardrail",
                    FinishReason = FinishReasons.GuardrailRejected,
                    Status = AgentRunStatus.Failed
                };
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Input tripwire triggered by {GuardrailName}: {Reason}",
                ex.GuardrailName, ex.Message);

            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, GuardrailDirections.Input);

            return new AgentRunResult
            {
                Response = ex.Message,
                FinishReason = FinishReasons.GuardrailRejected,
                Status = AgentRunStatus.Failed
            };
        }

        return await next(context, cancellationToken);
    }

    /// <summary>
    /// 流式路径 - Before: 检查输入安全后再委托给下游。
    /// 将 guardrail 检查从 async iterator 中分离，避免 try/catch + yield 的 C# 限制。
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Before: 输入检查（在 yield 之前执行，可安全使用 try/catch）
        AgentStreamChunk? rejectionChunk = null;
        try
        {
            var rejection = await GuardUserTurnAsync(context, cancellationToken);

            if (rejection != null)
            {
                _logger.LogWarning("Input rejected by guardrail {GuardrailName}: {Reason}",
                    rejection.GuardrailName, rejection.Reason);

                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", rejection.Reason ?? "Input rejected", GuardrailDirections.Input);

                rejectionChunk = new AgentStreamChunk
                {
                    Text = rejection.Reason ?? "Input rejected by guardrail",
                    FinishReason = FinishReasons.GuardrailRejected
                };
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Input tripwire triggered by {GuardrailName}: {Reason}",
                ex.GuardrailName, ex.Message);

            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, GuardrailDirections.Input);

            rejectionChunk = new AgentStreamChunk
            {
                Text = ex.Message,
                FinishReason = FinishReasons.GuardrailRejected
            };
        }

        // 如果被拒绝，返回拒绝 chunk 后结束
        if (rejectionChunk != null)
        {
            yield return rejectionChunk;
            yield break;
        }

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 对本轮用户输入跑输入守卫，返回拒绝结果（null = 放行）。改写结果写到 context 上供下游使用。
    /// </summary>
    /// <remarks>
    /// 评估的是<b>模型实际收到的文本</b>（见 <see cref="UserTurn"/>）：带内容块时是其中的文本块，不是 Message。
    /// 带内容块时 Message 不进模型，但它仍会进标题生成与运行摘要，与模型文本不同时也单独过一遍守卫（只取拒绝结论），
    /// 免得这条入口在改为评估内容块之后反而没人看。
    /// </remarks>
    private async Task<GuardrailResult?> GuardUserTurnAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var parts = request.ContentParts;
        var modelText = UserTurn.TextOf(request.UserMessage, parts);

        var (text, rejection) = await _guardrailRunner.RunInputGuardrailsAsync(modelText, cancellationToken);
        if (rejection != null)
        {
            return rejection;
        }

        if (parts is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(request.UserMessage)
            && !string.Equals(request.UserMessage, modelText, StringComparison.Ordinal))
        {
            var (_, messageRejection) = await _guardrailRunner.RunInputGuardrailsAsync(request.UserMessage, cancellationToken);
            if (messageRejection != null)
            {
                return messageRejection;
            }
        }

        // 守卫改写了文本（如 PII 脱敏）：写到 context 上供 AgentRuntime / 历史落库 / 上下文检索使用，请求原值不动。
        // 带内容块时模型只读内容块，改写必须落进内容块副本，否则模型收到的仍是原文。
        if (!string.Equals(text, modelText, StringComparison.Ordinal))
        {
            context.EffectiveUserMessage = text;
            if (parts is { Count: > 0 })
            {
                context.EffectiveContentParts = UserTurn.WithText(parts, text);
            }
        }

        return null;
    }

    /// <summary>发布 Guardrail 拦截事件（静默失败）</summary>
    private Task PublishGuardrailRejectionEventAsync(AiMiddlewareContext context, string guardrailName, string reason, string direction)
        => GuardrailEventPublisher.PublishGuardrailRejectionEventAsync(
            _eventBus, _logger, context.Request.UserId, context.Request.ThreadId, guardrailName, reason, direction);
}
