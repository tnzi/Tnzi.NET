namespace Tnzi.AI.Middleware;

/// <summary>
/// 输出 Guardrail 中间件 — After: 检查 AI 响应输出。
/// 流式模式下使用滑动窗口缓冲检查。
/// </summary>
public class OutputGuardrailMiddleware : IAiMiddleware
{
    private readonly GuardrailRunner _guardrailRunner;
    private readonly IEnumerable<IOutputGuardrail> _outputGuardrails;
    private readonly IOptions<AIOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutputGuardrailMiddleware> _logger;

    public int Order => AiMiddlewareOrders.OutputGuardrail;

    public OutputGuardrailMiddleware(
        GuardrailRunner guardrailRunner,
        IEnumerable<IOutputGuardrail> outputGuardrails,
        IOptions<AIOptions> options,
        IServiceProvider serviceProvider,
        ILogger<OutputGuardrailMiddleware> logger)
    {
        _guardrailRunner = Check.NotNull(guardrailRunner);
        _outputGuardrails = Check.NotNull(outputGuardrails);
        _options = Check.NotNull(options);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        var result = await next(context, cancellationToken);

        // Guardrails 未启用时跳过检查
        if (!_options.Value.Guardrails.Enabled)
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

                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", rejection.Reason ?? "Output rejected", "output");

                return new AgentRunResult
                {
                    Response = rejection.Reason ?? "Output rejected by guardrail",
                    RunId = result.RunId,
                    ThreadId = result.ThreadId,
                    Usage = result.Usage,
                    Citations = result.Citations,
                    FinishReason = FinishReasons.GuardrailRejected,
                    Status = AgentRunStatus.Failed
                };
            }

            // 如果 guardrail 修改了输出文本
            if (text != result.Response)
            {
                return new AgentRunResult
                {
                    Response = text,
                    RunId = result.RunId,
                    ThreadId = result.ThreadId,
                    Usage = result.Usage,
                    Citations = result.Citations,
                    FinishReason = result.FinishReason,
                    Status = result.Status
                };
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Output tripwire triggered by {GuardrailName}: {Reason}",
                ex.GuardrailName, ex.Message);

            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, "output");

            return new AgentRunResult
            {
                Response = ex.Message,
                RunId = result.RunId,
                ThreadId = result.ThreadId,
                Usage = result.Usage,
                FinishReason = FinishReasons.GuardrailRejected,
                Status = AgentRunStatus.Failed
            };
        }

        return result;
    }

    /// <summary>
    /// 流式输出 Guardrail — 滑动窗口缓冲检查。
    /// 当没有注册任何 IOutputGuardrail 或 StreamingBufferSize 为 0 时，直接透传不缓冲。
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var guardrailOptions = _options.Value.Guardrails;
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
            if (rejected)
            {
                break;
            }

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

                // 保留尾部重叠区域用于跨窗口检测（如关键词跨越窗口边界）
                var overlapSize = _options.Value.Guardrails.StreamingOverlapSize;
                if (buffer.Length > overlapSize)
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
                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", rejection.Reason ?? "Output rejected", "output");
                return rejection.Reason;
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Streaming output tripwire triggered: {Message}", ex.Message);
            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, "output");
            return ex.Message;
        }

        return null;
    }

    /// <summary>发布 Guardrail 拦截事件（静默失败）</summary>
    private async Task PublishGuardrailRejectionEventAsync(AiMiddlewareContext context, string guardrailName, string reason, string direction)
    {
        try
        {
            var eventBus = _serviceProvider.GetService<IEventBus>();
            if (eventBus == null) return;

            await eventBus.PublishAsync(new GuardrailRejectionEvent
            {
                UserId = context.Request.UserId,
                ThreadId = context.Request.ThreadId,
                GuardrailName = guardrailName,
                Reason = reason,
                Direction = direction
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish GuardrailRejectionEvent");
        }
    }
}
