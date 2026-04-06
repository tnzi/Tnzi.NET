using Tnzi.AI.Tools;

namespace Tnzi.AI.Middleware;

/// <summary>
/// 工具错误恢复中间件。
/// 真正的恢复逻辑发生在 <see cref="IToolExecutionMiddleware"/> 路径，
/// 避免将整个 AI 管道的异常误判为“工具错误”。
/// </summary>
public class ToolErrorRecoveryMiddleware : IAiMiddleware, IToolExecutionMiddleware
{
    internal const string RecoveredErrorPropertyName = "ToolErrorRecovery.Error";
    private readonly ILogger<ToolErrorRecoveryMiddleware> _logger;
    private const int MaxErrorDetailLength = 500;

    public int Order => AiMiddlewareOrders.ToolErrorRecovery;

    public ToolErrorRecoveryMiddleware(ILogger<ToolErrorRecoveryMiddleware> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    public async Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next)
    {
        try
        {
            return await next();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var errorDetail = ex.Message.Length > MaxErrorDetailLength
                ? ex.Message[..MaxErrorDetailLength] + "..."
                : ex.Message;
            var recoveredError = $"{ex.GetType().Name}: {errorDetail}";

            _logger.LogError(ex, "Tool '{ToolName}' execution failed, injecting recoverable tool result", context.ToolName);
            context.Properties[RecoveredErrorPropertyName] = recoveredError;

            return $"Tool execution failed: {recoveredError}";
        }
    }

    public static string? GetRecoveredError(ToolExecutionContext context)
    {
        return context.Properties.TryGetValue(RecoveredErrorPropertyName, out var value)
            ? value as string
            : null;
    }
}
