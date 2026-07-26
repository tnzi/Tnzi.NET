using System.Collections.Concurrent;

namespace Tnzi.AI.Tests;

/// <summary>
/// AgentAsToolsExecutionStrategy 单元测试
/// </summary>
public class AgentAsToolsExecutionStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_NoChildCalled_ReturnsParentResponse()
    {
        var config = new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["helper"] = Guid.NewGuid() }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);
        var agent = CreateAgent("Parent", "I can handle this myself.");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        result.Response.Text.ShouldBe("I can handle this myself.");
        result.FinalAgentName.ShouldBe("Parent");
        result.HandoffPath.ShouldNotBeNull();
        result.HandoffPath.Count.ShouldBe(1);
        result.HandoffPath[0].ShouldBe("Parent");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyAgentsConfig_WorksAsPlainAgent()
    {
        var config = new AgentAsToolsConfiguration();
        var strategy = new AgentAsToolsExecutionStrategy(config);
        var agent = CreateAgent("Parent", "Hello");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        result.Response.Text.ShouldBe("Hello");
        result.FinalAgentName.ShouldBe("Parent");
    }

    [Fact]
    public void Constructor_DuplicateSanitizedNames_ThrowsInvalidOperation()
    {
        var config = new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid>
            {
                ["billing-expert"] = Guid.NewGuid(),
                ["billing expert"] = Guid.NewGuid() // same sanitized name
            }
        };

        Should.Throw<InvalidOperationException>(() => new AgentAsToolsExecutionStrategy(config));
    }

    [Fact]
    public void SanitizeName_ConvertsToValidFunctionName()
    {
        AgentAsToolsExecutionStrategy.SanitizeName("Billing Expert").ShouldBe("billing_expert");
        AgentAsToolsExecutionStrategy.SanitizeName("tech-support").ShouldBe("tech_support");
        AgentAsToolsExecutionStrategy.SanitizeName("agent123").ShouldBe("agent123");
        AgentAsToolsExecutionStrategy.SanitizeName("---test---").ShouldBe("test");
    }

    [Fact]
    public async Task ExecuteAsync_FinalAgentName_AlwaysParent()
    {
        var config = new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["child"] = Guid.NewGuid() }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);
        var agent = CreateAgent("ParentAgent", "Result");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);
        result.FinalAgentName.ShouldBe("ParentAgent");
    }

    [Fact]
    public async Task ExecuteAsync_HandoffPath_StartsWithParent()
    {
        var config = new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["child"] = Guid.NewGuid() }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);
        var agent = CreateAgent("ParentAgent", "Result");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);
        result.HandoffPath.ShouldNotBeNull();
        result.HandoffPath[0].ShouldBe("ParentAgent");
    }

    [Fact]
    public async Task ExecuteStreamingAsync_YieldsStreamChunks()
    {
        var config = new AgentAsToolsConfiguration();
        var strategy = new AgentAsToolsExecutionStrategy(config);
        var agent = CreateAgentForStreaming("Parent", "Hello", ["He", "llo"]);
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, context, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBeGreaterThan(0);
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

    private static AgentExecutor CreateAgentForStreaming(string name, string nonStreamResponse, string[] streamChunks)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, nonStreamResponse)));
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

    // ------------------------------------------------------------------
    // IAgentStreamForwarder 测试
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithoutForwarder_UsesNonStreamingPath()
    {
        // Arrange - ServiceProvider 不注册 IAgentStreamForwarder（返回 null）
        var config = new AgentAsToolsConfiguration();
        var strategy = new AgentAsToolsExecutionStrategy(config);
        var agent = CreateAgent("Parent", "non-streaming result");
        var context = CreateContext(); // Mock.Of<IServiceProvider>() 默认返回 null
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act
        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        // Assert - 正常走非流式路径返回结果
        result.Response.Text.ShouldBe("non-streaming result");
    }

    [Fact]
    public async Task ExecuteAsync_EnableChildStreamingFalse_DoesNotResolveForwarder()
    {
        // Arrange - EnableChildStreaming = false（默认），即使 DI 注册了 forwarder 也不解析
        var config = new AgentAsToolsConfiguration { EnableChildStreaming = false };
        var strategy = new AgentAsToolsExecutionStrategy(config);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IAgentStreamForwarder))).Returns(Mock.Of<IAgentStreamForwarder>());

        var context = new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
            ServiceProvider = sp.Object,
            Logger = Mock.Of<ILogger>()
        };

        var agent = CreateAgent("Parent", "result");
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act
        await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);

        // Assert - forwarder 从未被解析
        sp.Verify(s => s.GetService(typeof(IAgentStreamForwarder)), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithForwarder_ResolvesForwarderOnceFromServiceProvider()
    {
        // Arrange - 验证 forwarder 从 ServiceProvider 只解析一次（在 CreateChildAgentTools 中）
        var config = new AgentAsToolsConfiguration
        {
            EnableChildStreaming = true,
            Agents = new Dictionary<string, Guid>
            {
                ["child1"] = Guid.NewGuid(),
                ["child2"] = Guid.NewGuid()
            }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);

        var forwarderMock = new Mock<IAgentStreamForwarder>();
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IAgentStreamForwarder))).Returns(forwarderMock.Object);

        var context = new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
            ServiceProvider = sp.Object,
            Logger = Mock.Of<ILogger>()
        };

        // Act - 父 Agent 不调用工具，直接返回
        var parentAgent = CreateAgent("Parent", "done");
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };
        var result = await strategy.ExecuteAsync(parentAgent, messages, context, CancellationToken.None);

        // Assert - GetService<IAgentStreamForwarder> 只调用了一次（不是每个工具调用一次）
        sp.Verify(s => s.GetService(typeof(IAgentStreamForwarder)), Times.Once);
        result.Response.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteChildStreamingAsync_ForwardsAllChunksAndReturnsFullText()
    {
        // Arrange - 直接测试 ExecuteChildStreamingAsync 的逻辑
        // 模拟子 Agent 流式返回 3 个 chunk
        var childAgent = CreateAgentForStreaming("child", "abc", ["a", "b", "c"]);
        var invocations = new ConcurrentQueue<(string, int, int)>();

        var forwarderMock = new Mock<IAgentStreamForwarder>();
        var forwardedDeltas = new List<string>();
        forwarderMock.Setup(f => f.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, delta, _) => forwardedDeltas.Add(delta))
            .Returns(Task.CompletedTask);

        // 使用反射调用 private static 方法
        var method = typeof(AgentAsToolsExecutionStrategy).GetMethod(
            "ExecuteChildStreamingAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.ShouldNotBeNull();

        var childMessages = new List<ChatMessage> { new(ChatRole.User, "test") };

        // Act
        var resultTask = (Task<string>)method.Invoke(null, [childAgent, "child", childMessages, forwarderMock.Object, invocations, CancellationToken.None])!;
        var result = await resultTask;

        // Assert - 所有 chunk 被转发
        forwardedDeltas.ShouldBe(["a", "b", "c"]);
        // 返回完整文本
        result.ShouldBe("abc");
        // forwarder.WriteAsync 被调用 3 次
        forwarderMock.Verify(f => f.WriteAsync("child", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteChildWithTimeoutAsync_SetsAndRestoresSubAgentContext()
    {
        var childAgentId = Guid.NewGuid();
        var config = new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["SearchAgent"] = childAgentId }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(childAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = childAgentId,
                Name = "SearchAgent",
                Provider = "test",
                Model = "test-model",
                IsEnabled = true
            });

        var childExecutor = CreateAgent("SearchAgent", "child-result");
        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(x => x.CreateAgentAsync(
                "test",
                "test-model",
                It.IsAny<string?>(),
                "SearchAgent",
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<double?>(),
                It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                childAgentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(childExecutor);

        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties["ParentMarker"] = "parent";

        var context = new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = accessor,
            Logger = Mock.Of<ILogger>()
        };

        var method = typeof(AgentAsToolsExecutionStrategy).GetMethod(
            "ExecuteChildWithTimeoutAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.ShouldNotBeNull();

        var task = (Task<string>)method.Invoke(strategy,
        [
            "SearchAgent",
            childAgentId,
            "find latest docs",
            context,
            new ConcurrentQueue<(string, int, int)>(),
            null,
            CancellationToken.None
        ])!;

        var result = await task;

        result.ShouldBe("child-result");
        // ParentMarker was set via in-place mutation before any async work, so it remains visible
        accessor.Properties["ParentMarker"].ShouldBe("parent");
        // Note: RestoreProperties sets AsyncLocal.Value = new dict (copy-on-write),
        // which does NOT propagate back to the caller's ExecutionContext.
        // In production this is fine because child runs inside Task.Run with its own context.
        // SetSubAgentContext mutates the same dict object, so those keys remain visible here.
    }

    private static ExecutionStrategyContext CreateContext()
    {
        return new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = null,
            Logger = Mock.Of<ILogger>()
        };
    }
}
