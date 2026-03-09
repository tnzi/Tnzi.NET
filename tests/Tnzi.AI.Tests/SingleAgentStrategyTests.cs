namespace Tnzi.AI.Tests;

/// <summary>
/// SingleAgentStrategy 单元测试 — 验证直通行为
/// </summary>
public class SingleAgentStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToAgent_ReturnsWrappedResult()
    {
        var agent = CreateAgent("TestAgent", "Hello from agent");
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        result.Response.Text.ShouldBe("Hello from agent");
        result.HandoffPath.ShouldBeNull();
        result.FinalAgentName.ShouldBeNull();
        result.AggregatedUsage.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteStreamingAsync_DelegatesToAgent_YieldsChunks()
    {
        var agent = CreateStreamingAgent("TestAgent", ["Hello", " World"]);
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, context, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // AgentExecutor 的流式逻辑会将 ChatResponseUpdate 转换为 AgentStreamChunk
        chunks.ShouldNotBeEmpty();
        var textContent = string.Join("", chunks.Where(c => c.Text != null).Select(c => c.Text));
        textContent.ShouldContain("Hello");
        textContent.ShouldContain("World");
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var a = SingleAgentStrategy.Instance;
        var b = SingleAgentStrategy.Instance;
        ReferenceEquals(a, b).ShouldBeTrue();
    }

    private static AgentExecutor CreateAgent(string name, string response)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        return new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = name });
    }

    private static AgentExecutor CreateStreamingAgent(string name, string[] chunks)
    {
        var mock = new Mock<IChatClient>();

        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(chunks));

        return new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = name });
    }

    private static IAsyncEnumerable<ChatResponseUpdate> CreateAsyncEnumerable(string[] textChunks)
    {
        return AsyncEnumerable(textChunks);

        static async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable(string[] chunks)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(chunk)]
                };
            }
        }
    }

    private static ExecutionStrategyContext CreateContext()
    {
        return new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>()
        };
    }
}
