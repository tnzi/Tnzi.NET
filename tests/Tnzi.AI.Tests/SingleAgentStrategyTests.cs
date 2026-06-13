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

    [Fact]
    public async Task ExecuteAsync_WithUsageDetails_PreservesUsageInResult()
    {
        // 验证 token 用量信息正确传递
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Response with usage"))
            {
                Usage = new UsageDetails { InputTokenCount = 50, OutputTokenCount = 25, TotalTokenCount = 75 }
            });

        var agent = new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = "UsageAgent" });
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Track usage") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        result.Response.Usage.ShouldNotBeNull();
        result.Response.Usage!.InputTokens.ShouldBe(50);
        result.Response.Usage!.OutputTokens.ShouldBe(25);
        result.Response.Usage!.TotalTokens.ShouldBe(75);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleMessages_PassesAllMessagesToAgent()
    {
        // 验证多条消息全部传递给 Agent
        List<ChatMessage>? capturedMessages = null;
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
            {
                capturedMessages = msgs.ToList();
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK")));

        var agent = new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = "MultiMsgAgent" });
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helper"),
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there"),
            new(ChatRole.User, "Follow up question")
        };

        await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        capturedMessages.ShouldNotBeNull();
        capturedMessages!.Count.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_WithCancellation_StopsEarly()
    {
        // 验证取消令牌能中止流式输出
        using var cts = new CancellationTokenSource();
        var chunkIndex = 0;

        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateCancellableAsyncEnumerable(cts));

        var agent = new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = "CancelAgent" });
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Cancel me") };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, context, cts.Token))
        {
            chunks.Add(chunk);
            chunkIndex++;
            if (chunkIndex >= 2)
            {
                await cts.CancelAsync();
                break;
            }
        }

        // 最多收到 2-3 个 chunk（在取消生效前可能多发送了一个）
        chunks.Count.ShouldBeInRange(1, 3);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullHandoffPathAndFinalAgentName()
    {
        // SingleAgentStrategy 不设置 HandoffPath 和 FinalAgentName（单 Agent 无需编排元数据）
        var agent = CreateAgent("Solo", "Solo response");
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Solo request") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        result.HandoffPath.ShouldBeNull();
        result.FinalAgentName.ShouldBeNull();
        result.AggregatedUsage.ShouldBeNull();
        result.Response.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteStreamingAsync_EmptyResponse_YieldsNoTextChunks()
    {
        // Agent 返回空流式响应 → 不应产生文本内容
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyAsyncEnumerable());

        var agent = new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = "EmptyAgent" });
        var strategy = SingleAgentStrategy.Instance;
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Empty response") };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, context, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // 不应该有文本内容（可能有元数据 chunk）
        var textContent = string.Join("", chunks.Where(c => c.Text != null).Select(c => c.Text));
        textContent.ShouldBeEmpty();
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

    private static IAsyncEnumerable<ChatResponseUpdate> CreateCancellableAsyncEnumerable(CancellationTokenSource cts)
    {
        return AsyncEnumerable();

        async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
        {
            for (var i = 0; i < 100; i++)
            {
                if (cts.Token.IsCancellationRequested) yield break;
                await Task.Yield();
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent($"Chunk{i}")]
                };
            }
        }
    }

    private static IAsyncEnumerable<ChatResponseUpdate> CreateEmptyAsyncEnumerable()
    {
        return AsyncEnumerable();

        static async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static ExecutionStrategyContext CreateContext()
    {
        return new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            Logger = Mock.Of<ILogger>()
        };
    }
}
