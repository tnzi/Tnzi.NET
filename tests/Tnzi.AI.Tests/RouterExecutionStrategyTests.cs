
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
