namespace Tnzi.AI.Middleware;

/// <summary>
/// 工具级 Guardrail 中间件。
/// </summary>
/// <remarks>
/// 真正的拦截发生在 <see cref="IToolExecutionMiddleware"/> 路径，确保在工具副作用发生前完成评估，
/// 非流式与流式共用同一条执行链。保留 <see cref="IAiMiddleware"/> 仅用于维持既有 AI 管道顺序与扩展点。
/// </remarks>
public class ToolGuardrailMiddleware : IAiMiddleware, IToolExecutionMiddleware
{
    public const string DeniedPropertyName = "ToolGuardrailDenied";
    public const string ReasonPropertyName = "ToolGuardrailReason";

    private readonly IEnumerable<IGuardrailProvider> _providers;
    private readonly IOptions<AIOptions> _options;
    private readonly IEventBus? _eventBus;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly ILogger<ToolGuardrailMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ToolGuardrail;

    public ToolGuardrailMiddleware(
        IEnumerable<IGuardrailProvider> providers,
        IOptions<AIOptions> options,
        ILogger<ToolGuardrailMiddleware> logger,
        IEventBus? eventBus = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        _providers = Check.NotNull(providers);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
        _executionContextAccessor = executionContextAccessor;
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    public async Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next)
    {
        if (!_options.Value.Guardrails.Enabled)
        {
            return await next();
        }

        var denial = await EvaluateToolAsync(context, context.CancellationToken);
        if (denial == null)
        {
            return await next();
        }

        var (reason, _) = denial.Value;
        var request = _executionContextAccessor?.CurrentRequest;
        context.Properties[DeniedPropertyName] = true;
        context.Properties[ReasonPropertyName] = reason;

        await PublishGuardrailEventAsync(request, context.ToolName, reason);
        return BuildBlockedToolResult(context.ToolName, reason);
    }

    public static string? GetDenialReason(ToolExecutionContext context)
    {
        var denied = context.Properties.TryGetValue(DeniedPropertyName, out var flag)
            && flag is true;
        if (!denied)
        {
            return null;
        }

        return context.Properties.TryGetValue(ReasonPropertyName, out var reason)
            ? reason as string ?? "Tool blocked by guardrail policy"
            : "Tool blocked by guardrail policy";
    }

    private async Task<(string Reason, string ProviderName)?> EvaluateToolAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var request = BuildGuardrailRequest(context);
        var failClosed = _options.Value.Guardrails.FailClosed;

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
                        context.ToolName, provider.Name, reason);

                    return (reason, provider.Name);
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
                    var reason = $"Guardrail evaluation failed: {ex.Message}";
                    _logger.LogWarning(ex, "Guardrail provider '{Provider}' threw exception, treating as deny (fail-closed)", provider.Name);
                    return (reason, provider.Name);
                }

                _logger.LogWarning(ex, "Guardrail provider '{Provider}' threw exception, skipping (fail-open)", provider.Name);
            }
        }

        return null;
    }

    private GuardrailRequest BuildGuardrailRequest(ToolExecutionContext context)
    {
        var request = _executionContextAccessor?.CurrentRequest;
        return new GuardrailRequest
        {
            ToolName = context.ToolName,
            ToolInput = context.Arguments?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? (object)"null"),
            AgentId = request?.AgentId,
            ThreadId = request?.ThreadId
        };
    }

    private static string BuildBlockedToolResult(string toolName, string reason)
        => $"Tool '{toolName}' blocked by guardrail policy: {reason}";

    private async Task PublishGuardrailEventAsync(AgentRunRequest? request, string toolName, string reason)
    {
        try
        {
            if (_eventBus == null) return;

            await _eventBus.PublishAsync(new GuardrailRejectionEvent
            {
                UserId = request?.UserId,
                ThreadId = request?.ThreadId,
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
