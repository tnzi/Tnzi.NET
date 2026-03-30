namespace Tnzi.AI.Tests;

/// <summary>
/// Shared test helper methods for creating minimal AI middleware contexts
/// </summary>
internal static class TestHelpers
{
    public static AiMiddlewareContext CreateMinimalContext(string userMessage = "Hello", Guid? threadId = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = userMessage,
                ThreadId = threadId ?? Guid.NewGuid()
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: "TestProvider",
                model: "test-model",
                agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };
    }
}
