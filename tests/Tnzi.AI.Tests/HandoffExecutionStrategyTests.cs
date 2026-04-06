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

    [Fact]
    public async Task ExecuteAsync_AllowReturnToSource_NoHandoff_WorksNormally()
    {
        var agentAId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = Guid.NewGuid() },
            MaxHandoffs = 5,
            AllowReturnToSource = true
        };
        var strategy = new HandoffExecutionStrategy(config);
        var agent = CreateAgent("AgentA", "Direct response");
        var context = CreateContext(agentAId);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);
        result.Response.Text.ShouldBe("Direct response");
        result.FinalAgentName.ShouldBe("AgentA");
    }

    [Fact]
    public async Task ExecuteAsync_AllowReturnToSourceFalse_PreservesOriginalBehavior()
    {
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = Guid.NewGuid() },
            MaxHandoffs = 5,
            AllowReturnToSource = false
        };
        var strategy = new HandoffExecutionStrategy(config);
        var agent = CreateAgent("AgentA", "Hello");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);
        result.Response.Text.ShouldBe("Hello");
        result.FinalAgentName.ShouldBe("AgentA");
    }

    [Fact]
    public async Task ExecuteAsync_SourceAlreadyInTargets_NoDuplicate()
    {
        var agentAId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid>
            {
                ["AgentB"] = Guid.NewGuid(),
                ["AgentA"] = agentAId
            },
            AllowReturnToSource = true
        };
        var strategy = new HandoffExecutionStrategy(config);
        var agent = CreateAgent("AgentA", "Hello");
        var context = CreateContext(agentAId);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agent, messages, context, CancellationToken.None);
        result.Response.Text.ShouldBe("Hello");
    }

    [Fact]
    public void DefaultConfig_AllowReturnToSourceIsTrue()
    {
        var config = new HandoffConfiguration();
        config.AllowReturnToSource.ShouldBeTrue();
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

    private static ExecutionStrategyContext CreateContext(Guid? startingAgentId = null)
    {
        return new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = Mock.Of<IRepository<Agent, Guid>>(),
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>(),
            StartingAgentId = startingAgentId
        };
    }

    // ------------------------------------------------------------------
    // Helper — create AgentExecutor that triggers transfer_to_agent
    // ------------------------------------------------------------------

    /// <summary>
    /// 创建一个调用 handoff_to_agent 工具转接到 targetAgentName 的 AgentExecutor
    /// </summary>
    private static AgentExecutor CreateHandoffAgent(string name, string targetAgentName)
    {
        var toolCall = new FunctionCallContent(
            callId: "hoff_1",
            name: "handoff_to_agent",
            arguments: new Dictionary<string, object?>
            {
                ["targetAgentName"] = targetAgentName,
                ["reason"] = "Better suited agent"
            });

        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First call: return tool call message
                // Second call (after tool execution): return empty text (handoff already recorded)
                return callCount == 1
                    ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])])
                    : new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
            });

        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }

    /// <summary>
    /// 创建上下文，其中 targetAgentId 映射到 targetAgent
    /// </summary>
    private static ExecutionStrategyContext CreateContextWithTarget(
        Guid targetAgentId,
        string targetName,
        AgentExecutor targetAgent,
        Guid? startingAgentId = null)
    {
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(targetAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = targetAgentId,
                Name = targetName,
                Provider = "test",
                IsEnabled = true
            });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                targetName,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<double?>(),
                It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(),
                targetAgentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetAgent);

        return new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>(),
            StartingAgentId = startingAgentId
        };
    }

    // ------------------------------------------------------------------
    // Actual handoff scenarios
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithHandoff_AgentBResponds()
    {
        var agentBId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        var agentA = CreateHandoffAgent("AgentA", "AgentB");
        var agentB = CreateAgent("AgentB", "Hello from B");
        var context = CreateContextWithTarget(agentBId, "AgentB", agentB);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("AgentB");
        result.Response.Text.ShouldBe("Hello from B");
        result.HandoffPath.ShouldBe(["AgentA", "AgentB"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithHandoff_HandoffPathTracked()
    {
        var agentBId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 10
        };
        var strategy = new HandoffExecutionStrategy(config);

        var agentA = CreateHandoffAgent("AgentA", "AgentB");
        var agentB = CreateAgent("AgentB", "B done");
        var context = CreateContextWithTarget(agentBId, "AgentB", agentB);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Help") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        result.HandoffPath.ShouldNotBeNull();
        result.HandoffPath.Count.ShouldBe(2);
        result.HandoffPath[0].ShouldBe("AgentA");
        result.HandoffPath[1].ShouldBe("AgentB");
    }

    [Fact]
    public async Task ExecuteAsync_TargetNotFound_ReturnsSourceAgentResult()
    {
        var missingId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = missingId },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        var agentA = CreateHandoffAgent("AgentA", "AgentB");

        // Repository returns null (agent not found)
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var context = new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>()
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // Target not found — strategy should return current agent's last response
        result.FinalAgentName.ShouldBe("AgentA");
        result.HandoffPath.ShouldNotBeNull();
        result.HandoffPath.ShouldContain("AgentA");
    }

    [Fact]
    public async Task ExecuteAsync_TargetDisabled_ReturnsSourceAgentResult()
    {
        var disabledId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = disabledId },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        var agentA = CreateHandoffAgent("AgentA", "AgentB");

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(disabledId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = disabledId,
                Name = "AgentB",
                Provider = "test",
                IsEnabled = false  // disabled!
            });

        var context = new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>()
        };
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // Disabled agent → ResolveAgentAsync returns null → fall back to source
        result.FinalAgentName.ShouldBe("AgentA");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTarget_ReturnsSourceAgentResult()
    {
        var agentBId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        // AgentA tries to hand off to "AgentC" which is NOT in Targets
        var agentA = CreateHandoffAgent("AgentA", "AgentC");

        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // Target not in allowed list — source agent's response is returned
        result.FinalAgentName.ShouldBe("AgentA");
        var handoffPath = result.HandoffPath;
        handoffPath.ShouldNotBeNull();
        handoffPath.Count.ShouldBe(1);
        handoffPath[0].ShouldBe("AgentA");
    }

    [Fact]
    public async Task ExecuteAsync_MaxHandoffsReached_ReturnsMaxHopMessage()
    {
        var agentBId = Guid.NewGuid();
        var agentAId = Guid.NewGuid();

        // MaxHandoffs=3: AgentA hands off to AgentB each hop — factory creates a fresh agent per call
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 3,
            AllowReturnToSource = false
        };
        var strategy = new HandoffExecutionStrategy(config);

        // AgentA always hands off to AgentB
        var agentA = CreateHandoffAgent("AgentA", "AgentB");

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(agentBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentBId, Name = "AgentB", Provider = "test", IsEnabled = true });

        // Factory always returns a FRESH handoff agent (new callCount per invocation)
        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), "AgentB",
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                agentBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateHandoffAgent("AgentB", "AgentB")); // fresh instance each call

        var context = new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>(),
            StartingAgentId = agentAId
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // Max handoffs reached
        result.Response.Text.ShouldBe("Max handoff limit reached");
        result.Response.FinishReason.ShouldBe(FinishReasons.MaxHandoffs);
    }

    [Fact]
    public async Task ExecuteAsync_BidirectionalHandoff_AllowReturnToSource_AgentBCanHandbackToA()
    {
        var agentAId = Guid.NewGuid();
        var agentBId = Guid.NewGuid();

        // Config: AgentA can hand off to AgentB; AllowReturnToSource=true so AgentB can return to A
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 5,
            AllowReturnToSource = true
        };
        var strategy = new HandoffExecutionStrategy(config);

        // AgentA hands off to AgentB
        var agentA = CreateHandoffAgent("AgentA", "AgentB");
        // AgentB hands back to AgentA (AllowReturnToSource injects AgentA into effectiveTargets)
        var agentBHandsBackToA = CreateHandoffAgent("AgentB", "AgentA");
        // AgentA (as returned from factory on second load) answers directly
        var agentAFinal = CreateAgent("AgentA", "Final answer from A");

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(agentBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentBId, Name = "AgentB", Provider = "test", IsEnabled = true });
        repository.Setup(r => r.GetAsync(agentAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentAId, Name = "AgentA", Provider = "test", IsEnabled = true });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), "AgentB",
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                agentBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentBHandsBackToA);
        agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), "AgentA",
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                agentAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentAFinal);

        var context = new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>(),
            StartingAgentId = agentAId
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // A → B → A: final answer from A
        result.FinalAgentName.ShouldBe("AgentA");
        result.Response.Text.ShouldBe("Final answer from A");
        result.HandoffPath.ShouldBe(["AgentA", "AgentB", "AgentA"]);
    }

    [Fact]
    public async Task ExecuteAsync_TokenUsage_AggregatedAcrossHops()
    {
        var agentBId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        var agentA = CreateHandoffAgent("AgentA", "AgentB");

        // AgentB returns with non-zero usage
        var agentBClient = new Mock<IChatClient>();
        agentBClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done from B"))
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 }
            });
        var agentB = new AgentExecutor(agentBClient.Object, new AgentExecutorOptions { Name = "AgentB" });

        var context = CreateContextWithTarget(agentBId, "AgentB", agentB);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // Usage should be aggregated (AgentB contributes 10 input, 5 output)
        result.AggregatedUsage.ShouldNotBeNull();
        result.AggregatedUsage!.InputTokens.ShouldBeGreaterThan(0);
        result.AggregatedUsage.OutputTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithHandoff_AgentExecutorHasHandoffTool()
    {
        // Verify that when handoff config has targets, the injected handoff tool
        // is actually available and can trigger a transfer
        var agentBId = Guid.NewGuid();
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = agentBId },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        // Agent that hands off to B
        var agentA = CreateHandoffAgent("AgentA", "AgentB");
        var agentB = CreateAgent("AgentB", "B answered");
        var context = CreateContextWithTarget(agentBId, "AgentB", agentB);
        var messages = new List<ChatMessage> { new(ChatRole.User, "question") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        // Confirms the handoff_to_agent tool was injected and invoked
        var handoffPath = result.HandoffPath;
        handoffPath.ShouldNotBeNull();
        handoffPath.ShouldContain("AgentB");
        result.FinalAgentName.ShouldBe("AgentB");
    }

    [Fact]
    public async Task ExecuteAsync_HandoffToSelf_NotInTargets_RemainsAtSource()
    {
        // AgentA tries to hand off to itself (not in targets) — should remain at AgentA
        var config = new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["AgentB"] = Guid.NewGuid() },
            MaxHandoffs = 5
        };
        var strategy = new HandoffExecutionStrategy(config);

        // AgentA hands off to "AgentA" (itself) which is NOT in targets
        var agentA = CreateHandoffAgent("AgentA", "AgentA");
        var context = CreateContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        var result = await strategy.ExecuteAsync(agentA, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("AgentA");
        var handoffPath = result.HandoffPath;
        handoffPath.ShouldNotBeNull();
        handoffPath.Count.ShouldBe(1);
    }
}
