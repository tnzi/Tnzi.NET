namespace Tnzi.AI.Tests;

/// <summary>
/// HandoffExecutionStrategy 单元测试 — 验证 Agent 转接编排
/// </summary>
public class HandoffExecutionStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_NoHandoff_ReturnsDirectResponse()
    {
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = Guid.NewGuid() },
            MaxHandoffs = 10
        };
        var strategy = new HandoffExecutionStrategy(config);
        var agent = CreateAgent("AgentA", "Hello from A");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        result.Response.Text.ShouldBe("Hello from A");
        result.FinalAgentName.ShouldBe("AgentA");
        result.HandoffPath.ShouldNotBeNull();
        result.HandoffPath.Count.ShouldBe(1);
        result.HandoffPath[0].ShouldBe("AgentA");
    }

    [Fact]
    public async Task ExecuteAsync_MaxHandoffsProperty()
    {
        var config = new HandoffConfiguration { MaxHandoffs = 3 };
        var strategy = new HandoffExecutionStrategy(config);
        var agent = CreateAgent("AgentA", "Hello");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);
        result.FinalAgentName.ShouldBe("AgentA");
        result.HandoffPath!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_NoHandoff_YieldsStreamChunks()
    {
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = Guid.NewGuid() },
            MaxHandoffs = 10
        };
        var strategy = new HandoffExecutionStrategy(config);

        // Agent 返回普通响应（无 handoff），streaming 也返回 chunks
        var agent = CreateAgentForStreaming("AgentA", "Hello from A", ["Hello", " from", " A"]);
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, context, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// 创建返回直接文本（无 handoff）的 AgentExecutor
    /// </summary>
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

    /// <summary>
    /// 创建同时支持非流式和流式输出的 AgentExecutor
    /// </summary>
    private static AgentExecutor CreateAgentForStreaming(string name, string nonStreamResponse, string[] streamChunks)
    {
        var mock = new Mock<IChatClient>();

        // 非流式：返回普通响应
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, nonStreamResponse)));

        // 流式：返回 chunks
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(streamChunks));

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
