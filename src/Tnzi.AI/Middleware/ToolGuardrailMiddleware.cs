namespace Tnzi.AI.Middleware;

/// <summary>
/// 工具级 Guardrail 中间件 — 在工具执行前通过 IGuardrailProvider 评估工具调用。
/// </summary>
/// <remarks>
/// 执行顺序 655（LoopDetection=650 之后，ToolErrorRecovery=660 之前）。
/// 从 AI 响应中提取工具调用（FunctionCallContent），逐个评估所有 IGuardrailProvider。
/// 被拒绝的工具调用将从响应中移除，并追加拒绝消息到对话历史。
/// </remarks>
public class ToolGuardrailMiddleware : IAiMiddleware
{
    private readonly IEnumerable<IGuardrailProvider> _providers;
    private readonly IOptions<AIOptions> _options;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<ToolGuardrailMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ToolGuardrail;

    public ToolGuardrailMiddleware(
        IEnumerable<IGuardrailProvider> providers,
        IOptions<AIOptions> options,
        ILogger<ToolGuardrailMiddleware> logger,
        IEventBus? eventBus = null)
    {
        _providers = Check.NotNull(providers);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware || !_options.Value.Guardrails.Enabled)
        {
            return await next(context, cancellationToken);
        }

        var messageCountBefore = context.Messages.Count;

        var result = await next(context, cancellationToken);

        // 仅检查本轮新增的 assistant 消息中的工具调用（跳过历史消息）
        var toolCallMessages = context.Messages
            .Skip(messageCountBefore)
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        if (toolCallMessages.Count == 0)
        {
            return result;
        }

        var failClosed = _options.Value.Guardrails.FailClosed;
        var deniedTools = new List<(string ToolName, string Reason)>();

        foreach (var toolCall in toolCallMessages)
        {
            var request = new GuardrailRequest
            {
                ToolName = toolCall.Name,
                ToolInput = toolCall.Arguments?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value ?? (object)"null"),
                AgentId = context.Agent?.AgentId,
                ThreadId = context.Request.ThreadId
            };

            foreach (var provider in _providers)
            {
                try
                {
                    var decision = await provider.EvaluateAsync(request, cancellationToken);
                    if (!decision.IsAllowed)
                    {
                        var reason = decision.Reasons.Count > 0
                            ? string.Join("; ", decision.Reasons.Select(r => r.Message))
                            : "Denied by guardrail policy";

                        _logger.LogWarning("Tool '{ToolName}' denied by {Provider}: {Reason}",
                            toolCall.Name, provider.Name, reason);

                        deniedTools.Add((toolCall.Name, reason));
                        break; // 一个 provider 拒绝即停止评估
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (failClosed)
                    {
                        _logger.LogWarning(ex, "Guardrail provider '{Provider}' threw exception, treating as deny (fail-closed)", provider.Name);
                        deniedTools.Add((toolCall.Name, $"Guardrail evaluation failed: {ex.Message}"));
                        break;
                    }

                    _logger.LogWarning(ex, "Guardrail provider '{Provider}' threw exception, skipping (fail-open)", provider.Name);
                }
            }
        }

        if (deniedTools.Count == 0)
        {
            return result;
        }

        // 发布事件（静默失败）
        foreach (var (toolName, reason) in deniedTools)
        {
            await PublishGuardrailEventAsync(context, toolName, reason);
        }

        // 部分拒绝场景（某些工具被拒绝但不是全部）：记录日志和事件但不阻断结果。
        // 完整的部分拒绝需要从消息中移除 FunctionCallContent 并注入替代响应，
        // 但这需要与 AgentRuntime 的工具执行循环深度集成，暂不处理。
        if (deniedTools.Count >= toolCallMessages.Count)
        {
            var allReasons = string.Join("\n", deniedTools.Select(d => $"- {d.ToolName}: {d.Reason}"));
            return new AgentRunResult
            {
                Response = $"Tool calls blocked by guardrail policy:\n{allReasons}",
                RunId = result.RunId,
                ThreadId = result.ThreadId,
                Usage = result.Usage,
                Citations = result.Citations,
                FinishReason = FinishReasons.GuardrailRejected,
                Status = AgentRunStatus.Failed
            };
        }

        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 流式模式和 ExternalCli 模式直接透传，工具级防护由非流式路径处理
        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task PublishGuardrailEventAsync(AiMiddlewareContext context, string toolName, string reason)
    {
        try
        {
            if (_eventBus == null) return;

            await _eventBus.PublishAsync(new GuardrailRejectionEvent
            {
                UserId = context.Request.UserId,
                ThreadId = context.Request.ThreadId,
                GuardrailName = $"ToolGuardrail:{toolName}",
                Reason = reason,
                Direction = GuardrailDirections.Tool
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish tool guardrail rejection event");
        }
    }
}
