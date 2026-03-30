namespace Tnzi.AI.Middleware;

/// <summary>
/// Catches exceptions during tool execution and converts them to error messages,
/// allowing the agent to continue rather than crash. Preserves OperationCanceledException.
/// </summary>
public class ToolErrorRecoveryMiddleware : IAiMiddleware
{
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
        if (context.ShouldSkipMiddleware)
            return await next(context, cancellationToken);

        try
        {
            return await next(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed, injecting error result to continue agent execution");

            var errorDetail = ex.Message.Length > MaxErrorDetailLength
                ? ex.Message[..MaxErrorDetailLength] + "..."
                : ex.Message;

            context.Messages.Add(new ChatMessage(ChatRole.User,
                $"[TOOL ERROR] A tool execution failed with {ex.GetType().Name}: {errorDetail}. " +
                "Please try a different approach or respond to the user directly."));

            return new AgentRunResult
            {
                Response = $"An internal tool error occurred: {errorDetail}",
                FinishReason = FinishReasons.Error,
                ThreadId = context.Request.ThreadId,
                Status = AgentRunStatus.Failed
            };
        }
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        AgentStreamChunk? errorChunk = null;
        await using var enumerator = next(context, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (errorChunk is null)
        {
            AgentStreamChunk? chunk;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                chunk = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streaming tool execution failed, emitting error chunk");
                var errorDetail = ex.Message.Length > MaxErrorDetailLength
                    ? ex.Message[..MaxErrorDetailLength] + "..."
                    : ex.Message;
                errorChunk = new AgentStreamChunk
                {
                    Error = $"Tool error: {ex.GetType().Name}: {errorDetail}",
                    FinishReason = FinishReasons.Error
                };
                break;
            }

            yield return chunk;
        }

        if (errorChunk is not null)
        {
            yield return errorChunk;
        }
    }
}
