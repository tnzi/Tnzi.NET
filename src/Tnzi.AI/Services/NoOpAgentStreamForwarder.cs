namespace Tnzi.AI.Services;

public class NoOpAgentStreamForwarder : IAgentStreamForwarder, INoOpService
{
    public Task WriteAsync(string agentName, string delta, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
