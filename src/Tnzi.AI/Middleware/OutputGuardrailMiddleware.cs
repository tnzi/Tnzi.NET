namespace Tnzi.AI.Middleware;

/// <summary>
/// 输出 Guardrail 中间件 — After: 检查 AI 响应输出。
/// 流式模式下使用滑动窗口缓冲检查。
/// </summary>
public class OutputGuardrailMiddleware : IAiMiddleware
{
    private readonly GuardrailRunner _guardrailRunner;
    private readonly IOptions<AIOptions> _options;
    private readonly ILogger<OutputGuardrailMiddleware> _logger;

    /// <summary>流式缓冲检查的默认窗口大小（字符数）</summary>
    private const int DefaultBufferWindowSize = 500;

    public int Order => 900;

    public OutputGuardrailMiddleware(
        GuardrailRunner guardrailRunner,
        IOptions<AIOptions> options,
        ILogger<OutputGuardrailMiddleware> logger)
    {
        _guardrailRunner = Check.NotNull(guardrailRunner);
        _options = Check.NotNull(options);
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

                return new AgentRunResult
                {
                    Response = rejection.Reason ?? "Output rejected by guardrail",
                    RunId = result.RunId,
                    ThreadId = result.ThreadId,
                    Usage = result.Usage,
                    Citations = result.Citations,
                    FinishReason = "guardrail_rejected",
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

            return new AgentRunResult
            {
                Response = ex.Message,
                RunId = result.RunId,
                ThreadId = result.ThreadId,
                Usage = result.Usage,
                FinishReason = "guardrail_rejected",
                Status = AgentRunStatus.Failed
            };
        }

        return result;
    }

    /// <summary>
    /// 流式输出 Guardrail — 滑动窗口缓冲检查
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Guardrails.Enabled)
        {
            // Guardrails 未启用，直接传递
            await foreach (var chunk in next(context, cancellationToken))
            {
                yield return chunk;
            }
            yield break;
        }

        // 滑动窗口缓冲：累积文本直到窗口大小，检查后释放
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
            if (buffer.Length >= DefaultBufferWindowSize)
            {
                var checkResult = await CheckOutputSafeAsync(buffer.ToString(), cancellationToken);
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
                buffer.Clear(); // 清除已检查的缓冲区，避免重复检查
            }
        }

        if (rejected)
        {
            yield return new AgentStreamChunk
            {
                Text = $"\n\n[Output blocked: {rejectionMessage}]",
                FinishReason = "guardrail_rejected"
            };
            yield break;
        }

        // 处理最后一批未检查的 chunks
        if (pendingChunks.Count > 0)
        {
            if (buffer.Length > 0)
            {
                var checkResult = await CheckOutputSafeAsync(buffer.ToString(), cancellationToken);
                if (checkResult != null)
                {
                    yield return new AgentStreamChunk
                    {
                        Text = $"\n\n[Output blocked: {checkResult}]",
                        FinishReason = "guardrail_rejected"
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
    private async Task<string?> CheckOutputSafeAsync(string text, CancellationToken ct)
    {
        try
        {
            var (_, rejection) = await _guardrailRunner.RunOutputGuardrailsAsync(text, ct);
            if (rejection != null)
            {
                _logger.LogWarning("Streaming output rejected by guardrail {GuardrailName}: {Reason}",
                    rejection.GuardrailName, rejection.Reason);
                return rejection.Reason;
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Streaming output tripwire triggered: {Message}", ex.Message);
            return ex.Message;
        }

        return null;
    }
}
