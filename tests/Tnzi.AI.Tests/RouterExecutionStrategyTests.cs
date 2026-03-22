
namespace Tnzi.AI.Tests;

/// <summary>
/// RouterExecutionStrategy 单元测试
/// </summary>
public class RouterExecutionStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_WhenRouteSelected_DelegatesToTargetAgent()
    {
        var targetAgentId = Guid.NewGuid();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = targetAgentId },
            AllowDirectResponse = false
        });

        var routerAgent = CreateRouterAgent("Router", "specialist", "Routing to specialist");
        var targetAgent = CreateTextAgent("Specialist", "Specialist answer");
        var context = CreateContext(targetAgentId, "Specialist", targetAgent);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Need specialist help") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("Specialist");
        result.HandoffPath.ShouldBe(["Router", "Specialist"]);
        result.Response.Text.ShouldBe("Specialist answer");
        result.AggregatedUsage.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoRouteSelected_ReturnsRouterResponse()
    {
        var strategy = new RouterExecutionStrategy(new RouterConfiguration());
        var routerAgent = CreateTextAgent("Router", "Direct router answer");
        var context = CreateEmptyContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Simple request") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("Router");
        result.HandoffPath.ShouldBe(["Router"]);
        result.Response.Text.ShouldBe("Direct router answer");
    }

    [Fact]
    public async Task ExecuteAsync_SingleCandidateAgent_RoutesToItDirectly()
    {
        // 只有一个候选 Agent 时，仍通过 LLM 路由
        var targetAgentId = Guid.NewGuid();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["only-agent"] = targetAgentId },
            AllowDirectResponse = false
        });

        var routerAgent = CreateRouterAgent("Router", "only-agent", "Routing to only-agent");
        var targetAgent = CreateTextAgent("OnlyAgent", "I am the only agent");
        var context = CreateContext(targetAgentId, "OnlyAgent", targetAgent);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Help me") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("OnlyAgent");
        result.HandoffPath.ShouldBe(["Router", "OnlyAgent"]);
        result.Response.Text.ShouldBe("I am the only agent");
    }

    [Fact]
    public async Task ExecuteAsync_RouteTargetNotInAllowedTargets_ReturnsRouterResponse()
    {
        // Router 选择了一个不在 Targets 映射中的名称 → 回退到 router 自己的响应
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["agent-a"] = Guid.NewGuid() },
            AllowDirectResponse = true
        });

        var routerAgent = CreateRouterAgent("Router", "unknown-agent", "Trying unknown");
        var context = CreateEmptyContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Route me") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        // 路由目标不在 Targets 中，回退到 router 响应
        result.FinalAgentName.ShouldBe("Router");
        result.HandoffPath.ShouldBe(["Router"]);
    }

    [Fact]
    public async Task ExecuteAsync_TargetAgentNotFoundOrDisabled_ReturnsRouterResponse()
    {
        // 路由目标存在于 Targets 映射，但 Agent 实体不存在或已禁用
        var targetAgentId = Guid.NewGuid();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["missing-agent"] = targetAgentId },
            AllowDirectResponse = true
        });

        var routerAgent = CreateRouterAgent("Router", "missing-agent", "Routing to missing");

        // 返回 null（Agent 不存在）
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(targetAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var context = new ExecutionStrategyContext
        {
            AgentFactory = Mock.Of<IAgentFactory>(),
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>()
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, "Route to missing") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("Router");
        result.HandoffPath.ShouldBe(["Router"]);
    }

    [Fact]
    public async Task ExecuteAsync_NoRouteAndAllowDirectResponseFalse_FallsBackToFirstTarget()
    {
        // AllowDirectResponse = false 且 Router 没有调用 route_to_agent → 自动回退到第一个 Target
        var targetAgentId = Guid.NewGuid();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["fallback"] = targetAgentId },
            AllowDirectResponse = false
        });

        var routerAgent = CreateTextAgent("Router", "Direct answer without routing");
        var targetAgent = CreateTextAgent("Fallback", "Fallback response");
        var context = CreateContext(targetAgentId, "Fallback", targetAgent);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Help") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        // 应该回退到第一个 target 而非直接返回 router 的答案
        result.FinalAgentName.ShouldBe("Fallback");
        result.HandoffPath.ShouldBe(["Router", "Fallback"]);
        result.Response.Text.ShouldBe("Fallback response");
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesTokenUsageFromBothAgents()
    {
        // 验证 router + target 的 token 用量正确累加
        var targetAgentId = Guid.NewGuid();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["target"] = targetAgentId },
            AllowDirectResponse = false
        });

        var routerAgent = CreateRouterAgent("Router", "target", "Context for target");
        var targetAgent = CreateTextAgent("Target", "Final answer");
        var context = CreateContext(targetAgentId, "Target", targetAgent);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Question") };

        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        result.AggregatedUsage.ShouldNotBeNull();
        // Router: 12 input + 6 output, Target: 8 input + 4 output
        result.AggregatedUsage!.InputTokens.ShouldBe(20);
        result.AggregatedUsage!.OutputTokens.ShouldBe(10);
        result.AggregatedUsage!.TotalTokens.ShouldBe(30);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_WhenNoRouteAndAllowDirect_YieldsRouterOutput()
    {
        // 流式模式下 Router 没有路由且 AllowDirectResponse=true → 直接输出 router 的流式内容
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            AllowDirectResponse = true
        });

        var routerAgent = CreateStreamingAgent("Router", ["Direct", "Stream"]);
        var context = CreateEmptyContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "Direct stream") };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in strategy.ExecuteStreamingAsync(routerAgent, messages, context, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.ShouldNotBeEmpty();
        var textContent = string.Join("", chunks.Where(c => c.Text != null).Select(c => c.Text));
        textContent.ShouldContain("Direct");
        textContent.ShouldContain("Stream");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleTargets_RoutesToCorrectOne()
    {
        // 多个候选 Agent，Router 选择第二个
        var targetAId = Guid.NewGuid();
        var targetBId = Guid.NewGuid();
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid>
            {
                ["agent-a"] = targetAId,
                ["agent-b"] = targetBId
            },
            AllowDirectResponse = false
        });

        var routerAgent = CreateRouterAgent("Router", "agent-b", "Context for B");
        var targetBAgent = CreateTextAgent("AgentB", "Response from B");

        // 只 mock agent-b 的解析
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(targetBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = targetBId, Name = "AgentB", Provider = "test", IsEnabled = true });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                "AgentB", It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(),
                It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                targetBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetBAgent);

        var context = new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = Mock.Of<IServiceProvider>(),
            Logger = Mock.Of<ILogger>()
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, "Route to B") };
        var result = await strategy.ExecuteAsync(routerAgent, messages, context, CancellationToken.None);

        result.FinalAgentName.ShouldBe("AgentB");
        result.HandoffPath.ShouldBe(["Router", "AgentB"]);
        result.Response.Text.ShouldBe("Response from B");
    }

    private static AgentExecutor CreateRouterAgent(string name, string routeTarget, string finalText)
    {
        var toolCall = new FunctionCallContent("route_1", "route_to_agent", new Dictionary<string, object?>
        {
            ["targetAgentName"] = routeTarget,
            ["reason"] = "Best suited"
        });

        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])])
                    : new ChatResponse(new ChatMessage(ChatRole.Assistant, finalText))
                    {
                        Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 6, TotalTokenCount = 18 }
                    };
            });

        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }

    private static AgentExecutor CreateTextAgent(string name, string responseText)
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                Usage = new UsageDetails { InputTokenCount = 8, OutputTokenCount = 4, TotalTokenCount = 12 }
            });

        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }

    private static ExecutionStrategyContext CreateContext(Guid targetAgentId, string targetName, AgentExecutor targetAgent)
    {
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(targetAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = targetAgentId,
                Name = targetName,
                Provider = "test",
                IsEnabled = true
            });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(x => x.CreateAgentAsync(
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
            Logger = Mock.Of<ILogger>()
        };
    }

    private static AgentExecutor CreateStreamingAgent(string name, string[] chunks)
    {
        var mock = new Mock<IChatClient>();

        // 非流式路径（router 执行用）
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Join("", chunks)))
            {
                Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3, TotalTokenCount = 8 }
            });

        // 流式路径
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(chunks));

        return new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = name });

        static async IAsyncEnumerable<ChatResponseUpdate> CreateAsyncEnumerable(string[] textChunks)
        {
            foreach (var chunk in textChunks)
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

    private static ExecutionStrategyContext CreateEmptyContext()
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
