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
    /// <remarks>
    /// <para>
    /// 窗口 = 上一窗口已发出的重叠尾巴 + 本窗口待发的文本。守卫返回 <c>GuardrailResult.Modified</c>（脱敏等）时，
    /// 发出的是改写后的待发部分，而不是原样放行缓冲的 chunk：此前流式路径丢弃了改写结果，
    /// 非流式响应被掩码、SSE（界面默认路径）却把原文流给了客户端。
    /// </para>
    /// <para>
    /// 已发出的重叠部分收不回来：改写若动到了它（例如一个卡号正好跨在上一个窗口的边界上，前半截已经发出），
    /// 按拦截处理并结束本次输出（失败关闭），不假装已经脱敏。下一个窗口的重叠取自实际发出的文本。
    /// 因此流式下的改写是逐窗口生效的局部改写（脱敏、替换）；「在整段回复末尾追加一句」这类改写只在非流式下有意义。
    /// </para>
    /// </remarks>
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
        var window = new StringBuilder();
        // 窗口开头已经发出过的字符数（上一窗口留下的重叠）
        var releasedLength = 0;
        var pendingChunks = new List<AgentStreamChunk>();
        string? blockReason = null;

        await foreach (var chunk in next(context, cancellationToken))
        {
            if (chunk.Text != null)
            {
                window.Append(chunk.Text);
            }
            pendingChunks.Add(chunk);

            // 达到窗口大小时检查
            if (window.Length >= bufferSize)
            {
                var check = await CheckWindowAsync(context, window.ToString(), releasedLength, cancellationToken);
                if (check.BlockReason != null)
                {
                    blockReason = check.BlockReason;
                    break;
                }

                // 检查通过，释放所有待发送的 chunks（守卫改写过则发改写后的文本）
                foreach (var pending in Release(pendingChunks, check.Replacement))
                {
                    yield return pending;
                }
                pendingChunks.Clear();

                // 保留尾部重叠区域用于跨窗口检测（如关键词跨越窗口边界），取自实际发出的文本。
                // 必须严格小于窗口大小：overlap >= bufferSize 时缓冲区永不收缩，
                // 之后每来一个 chunk 都会重跑一次 guardrail（LLM-Judge 成本失控）。
                var emitted = check.EmittedWindow;
                var overlapSize = Math.Min(_options.CurrentValue.Guardrails.StreamingOverlapSize, bufferSize - 1);
                window.Clear();
                if (overlapSize > 0 && emitted.Length > overlapSize)
                {
                    // 重叠的起点不落在代理对中间：落在后半个上时往后挪一位，下一个窗口不以孤立的低代理项开头
                    // （窗口原样交给每个输出守卫，LLM 评审的提示词里那是一个替换字符，按 Unicode 处理文本的守卫会直接抛异常）。
                    var overlapStart = emitted.Length - overlapSize;
                    if (char.IsLowSurrogate(emitted[overlapStart]))
                    {
                        overlapStart++;
                    }

                    window.Append(emitted, overlapStart, emitted.Length - overlapStart);
                }
                releasedLength = window.Length;
            }
        }

        if (blockReason == null && pendingChunks.Count > 0)
        {
            // 处理最后一批未检查的 chunks（只剩已发出的重叠时无需再查）
            string? replacement = null;
            if (window.Length > releasedLength)
            {
                var check = await CheckWindowAsync(context, window.ToString(), releasedLength, cancellationToken);
                blockReason = check.BlockReason;
                replacement = check.Replacement;
            }

            if (blockReason == null)
            {
                foreach (var pending in Release(pendingChunks, replacement))
                {
                    yield return pending;
                }
                yield break;
            }
        }

        if (blockReason != null)
        {
            // 被拦下的是待发文本，不是已经发生的 LLM 调用：丢弃的 chunk 里的用量随拒绝 chunk 一起交给外层结算
            yield return new AgentStreamChunk
            {
                Text = $"\n\n[Output blocked: {blockReason}]",
                FinishReason = FinishReasons.GuardrailRejected,
                Usage = StreamUsage.Sum(pendingChunks)
            };
        }
    }

    /// <summary>
    /// 一次窗口检查的结论：<see cref="BlockReason"/> 非空 = 拦截；否则 <see cref="Replacement"/> 非空 = 待发文本被改写为它，
    /// <see cref="EmittedWindow"/> 是发出之后这个窗口的完整文本（下一窗口的重叠从这里取）。
    /// </summary>
    private readonly record struct WindowCheck(string? BlockReason, string? Replacement, string EmittedWindow);

    /// <summary>
    /// 检查一个窗口。<paramref name="releasedLength"/> 是窗口开头已经发出过的字符数：改写只能落在它之后。
    /// </summary>
    private async Task<WindowCheck> CheckWindowAsync(AiMiddlewareContext context, string window, int releasedLength, CancellationToken ct)
    {
        try
        {
            var (text, rejection) = await _guardrailRunner.RunOutputGuardrailsAsync(window, ct);
            if (rejection != null)
            {
                _logger.LogWarning("Streaming output rejected by guardrail {GuardrailName}: {Reason}",
                    rejection.GuardrailName, rejection.Reason);
                var reason = rejection.Reason ?? "Output rejected";
                await PublishGuardrailRejectionEventAsync(context, rejection.GuardrailName ?? "unknown", reason, GuardrailDirections.Output);
                return new WindowCheck(reason, null, window);
            }

            if (string.Equals(text, window, StringComparison.Ordinal))
            {
                return new WindowCheck(null, null, window);
            }

            if (!text.AsSpan().StartsWith(window.AsSpan(0, releasedLength), StringComparison.Ordinal))
            {
                // 改写动到了已经发出的文本，收不回来：失败关闭
                const string reason = "An output guardrail modified content that had already been streamed";
                _logger.LogWarning("Streaming output blocked: a guardrail modification reached text that was already sent to the client");
                await PublishGuardrailRejectionEventAsync(context, StreamingModificationGuardName, reason, GuardrailDirections.Output);
                return new WindowCheck(reason, null, window);
            }

            return new WindowCheck(null, text[releasedLength..], text);
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Streaming output tripwire triggered: {Message}", ex.Message);
            await PublishGuardrailRejectionEventAsync(context, ex.GuardrailName, ex.Message, GuardrailDirections.Output);
            return new WindowCheck(ex.Message, null, window);
        }
    }

    /// <summary>流式改写动到已发出文本而被拦下时，拦截事件上记的守卫名（改写来自哪个守卫，runner 的返回值里没有）。</summary>
    private const string StreamingModificationGuardName = "OutputGuardrail.StreamingModification";

    /// <summary>
    /// 待发 chunk 原样释放；<paramref name="replacement"/> 非空时，改写后的文本放在第一个带文本的 chunk 上，
    /// 其余带文本的 chunk 去掉文本（用量、结束原因、工具调用等其它字段原样保留）。chunk 本身不修改，发的是副本。
    /// </summary>
    private static IReadOnlyList<AgentStreamChunk> Release(List<AgentStreamChunk> pending, string? replacement)
    {
        if (replacement == null)
        {
            return pending;
        }

        var released = new List<AgentStreamChunk>(pending.Count + 1);
        var placed = false;
        foreach (var chunk in pending)
        {
            if (chunk.Text == null)
            {
                released.Add(chunk);
                continue;
            }

            released.Add(chunk.WithText(placed ? null : replacement));
            placed = true;
        }

        if (!placed && replacement.Length > 0)
        {
            released.Insert(0, new AgentStreamChunk { Text = replacement });
        }

        return released;
    }

    /// <summary>发布 Guardrail 拦截事件（静默失败）</summary>
    private Task PublishGuardrailRejectionEventAsync(AiMiddlewareContext context, string guardrailName, string reason, string direction)
        => GuardrailEventPublisher.PublishGuardrailRejectionEventAsync(
            _eventBus, _logger, context.Request.UserId, context.Request.ThreadId, guardrailName, reason, direction);
}
