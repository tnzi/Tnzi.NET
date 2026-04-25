namespace Tnzi.AI.Services;

public class NoOpExternalCliExecutor : IExternalCliExecutor, INoOpService
{
    private const string Message =
        "ExternalCli execution requires Tnzi.AI.Cli module. Add [DependsOn(typeof(AICliModule))] to enable CLI-based agents.";

    public Task<AgentRunResult> ExecuteCliAsync(AiMiddlewareContext context, CancellationToken ct)
        => Task.FromException<AgentRunResult>(new BusinessException(Message, ErrorCodes.InternalError, 501));

    public async IAsyncEnumerable<AgentStreamChunk> ExecuteCliStreamingAsync(
        AiMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new AgentStreamChunk
        {
            Text = Message,
            FinishReason = FinishReasons.Error
        };

        await Task.CompletedTask;
    }
}
