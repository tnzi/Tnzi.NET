namespace Tnzi.AI.Middleware;

/// <summary>
/// 输出 Guardrail 中间件 - After: 检查 AI 响应输出。
/// 流式模式下使用滑动窗口缓冲检查。
/// </summary>
public class OutputGuardrailMiddleware : IAiMiddleware
{
    private readonly GuardrailRunner _guardrailRunner;
    private readonly IEnumerable<IOutputGuardrail> _outputGuardrails;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<OutputGuardrailMiddleware> _logger;

    public int Order => AiMiddlewareOrders.OutputGuardrail;

    public OutputGuardrailMiddleware(
        GuardrailRunner guardrailRunner,
        IEnumerable<IOutputGuardrail> outputGuardrails,
        IOptionsMonitor<AIOptions> options,
        ILogger<OutputGuardrailMiddleware> logger,
        IEventBus? eventBus = null)
    {
        _guardrailRunner = Check.NotNull(guardrailRunner);
        _outputGuardrails = Check.NotNull(outputGuardrails);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        var result = await next(context, cancellationToken);

        // Guardrails 未启用时跳过检查
        if (!_options.CurrentValue.Guardrails.Enabled)
        {
            return result;
        }

        // After: 检查输出
        if (string.IsNullOrEmpty(result.Response))
        {
            return result;
        }

        try
        {
            var (text, rejection) = await _guardrailRunner.RunOutputGuardrailsAsync(
                result.Response, cancellationToken);

            if (rejection != null)
            {
                _logger.LogWarning("Output rejected by guardrail {GuardrailName}: {Reason}",
                    rejection.GuardrailName, rejection.Reason);

                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", rejection.Reason ?? "Output rejected", GuardrailDirections.Output);

                return result.CloneWith(
                    response: rejection.Reason ?? "Output rejected by guardrail",
                    finishReason: FinishReasons.GuardrailRejected,
                    status: AgentRunStatus.Failed);
            }

            // 如果 guardrail 修改了输出文本
            if (text != result.Response)
            {
                return result.CloneWith(response: text);
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Output tripwire triggered by {GuardrailName}: {Reason}",
                ex.GuardrailName, ex.Message);

            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, GuardrailDirections.Output);

            return result.CloneWith(
                response: ex.Message,
                finishReason: FinishReasons.GuardrailRejected,
                status: AgentRunStatus.Failed);
        }

        return result;
    }

    /// <summary>
    /// 流式输出 Guardrail - 滑动窗口缓冲检查。
    /// 当没有注册任何 IOutputGuardrail 或 StreamingBufferSize 为 0 时，直接透传不缓冲。
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var guardrailOptions = _options.CurrentValue.Guardrails;
        var hasOutputGuardrails = _outputGuardrails.Any();

        // 无需缓冲的情况：Guardrails 未启用、无输出 guardrail 注册、或缓冲大小为 0
        if (!guardrailOptions.Enabled || !hasOutputGuardrails || guardrailOptions.StreamingBufferSize <= 0)
        {
            await foreach (var chunk in next(context, cancellationToken))
            {
                yield return chunk;
            }
            yield break;
        }

        // 滑动窗口缓冲：累积文本直到窗口大小，检查后释放
        var bufferSize = guardrailOptions.StreamingBufferSize;
        var buffer = new StringBuilder();
        var pendingChunks = new List<AgentStreamChunk>();
        var rejected = false;
        string? rejectionMessage = null;

        await foreach (var chunk in next(context, cancellationToken))
        {
            if (chunk.Text != null)
            {
                buffer.Append(chunk.Text);
            }
            pendingChunks.Add(chunk);

            // 达到窗口大小时检查
            if (buffer.Length >= bufferSize)
            {
                var checkResult = await CheckOutputSafeAsync(context, buffer.ToString(), cancellationToken);
                if (checkResult != null)
                {
                    rejected = true;
                    rejectionMessage = checkResult;
                    break;
                }

                // 检查通过，释放所有待发送的 chunks
                foreach (var pending in pendingChunks)
                {
                    yield return pending;
                }
                pendingChunks.Clear();

                // 保留尾部重叠区域用于跨窗口检测（如关键词跨越窗口边界）。
                // 必须严格小于窗口大小：overlap >= bufferSize 时缓冲区永不收缩，
                // 之后每来一个 chunk 都会重跑一次 guardrail（LLM-Judge 成本失控）。
                var overlapSize = Math.Min(_options.CurrentValue.Guardrails.StreamingOverlapSize, bufferSize - 1);
                if (overlapSize > 0 && buffer.Length > overlapSize)
                {
                    var overlap = buffer.ToString(buffer.Length - overlapSize, overlapSize);
                    buffer.Clear();
                    buffer.Append(overlap);
                }
                else
                {
                    buffer.Clear();
                }
            }
        }

        if (rejected)
        {
            yield return new AgentStreamChunk
            {
                Text = $"\n\n[Output blocked: {rejectionMessage}]",
                FinishReason = FinishReasons.GuardrailRejected
            };
            yield break;
        }

        // 处理最后一批未检查的 chunks
        if (pendingChunks.Count > 0)
        {
            if (buffer.Length > 0)
            {
                var checkResult = await CheckOutputSafeAsync(context, buffer.ToString(), cancellationToken);
                if (checkResult != null)
                {
                    yield return new AgentStreamChunk
                    {
                        Text = $"\n\n[Output blocked: {checkResult}]",
                        FinishReason = FinishReasons.GuardrailRejected
                    };
                    yield break;
                }
            }

            foreach (var pending in pendingChunks)
            {
                yield return pending;
            }
        }
    }

    /// <summary>
    /// 安全检查输出，返回拒绝原因（null 表示通过）
    /// </summary>
    private async Task<string?> CheckOutputSafeAsync(AiMiddlewareContext context, string text, CancellationToken ct)
    {
        try
        {
            var (_, rejection) = await _guardrailRunner.RunOutputGuardrailsAsync(text, ct);
            if (rejection != null)
            {
                _logger.LogWarning("Streaming output rejected by guardrail {GuardrailName}: {Reason}",
                    rejection.GuardrailName, rejection.Reason);
                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", rejection.Reason ?? "Output rejected", GuardrailDirections.Output);
                return rejection.Reason;
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Streaming output tripwire triggered: {Message}", ex.Message);
            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, GuardrailDirections.Output);
            return ex.Message;
        }

        return null;
    }

    /// <summary>发布 Guardrail 拦截事件（静默失败）</summary>
    private Task PublishGuardrailRejectionEventAsync(AiMiddlewareContext context, string guardrailName, string reason, string direction)
        => GuardrailEventPublisher.PublishGuardrailRejectionEventAsync(
            _eventBus, _logger, context.Request.UserId, context.Request.ThreadId, guardrailName, reason, direction);
}
