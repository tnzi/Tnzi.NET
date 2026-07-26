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
        var inputText = context.Request.UserMessage ?? string.Empty;

        try
        {
            var (text, rejection) = await _guardrailRunner.RunInputGuardrailsAsync(
                inputText, cancellationToken);

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

            // 如果 guardrail 修改了文本（如 PII 脱敏），写入 EffectiveUserMessage 供 AgentRuntime 使用
            if (!string.Equals(text, inputText, StringComparison.Ordinal))
            {
                context.EffectiveUserMessage = text;
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
        var inputText = context.Request.UserMessage ?? string.Empty;

        // Before: 输入检查（在 yield 之前执行，可安全使用 try/catch）
        AgentStreamChunk? rejectionChunk = null;
        try
        {
            var (text, rejection) = await _guardrailRunner.RunInputGuardrailsAsync(
                inputText, cancellationToken);

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
            else if (!string.Equals(text, inputText, StringComparison.Ordinal))
            {
                context.EffectiveUserMessage = text;
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

    /// <summary>发布 Guardrail 拦截事件（静默失败）</summary>
    private Task PublishGuardrailRejectionEventAsync(AiMiddlewareContext context, string guardrailName, string reason, string direction)
        => GuardrailEventPublisher.PublishGuardrailRejectionEventAsync(
            _eventBus, _logger, context.Request.UserId, context.Request.ThreadId, guardrailName, reason, direction);
}
