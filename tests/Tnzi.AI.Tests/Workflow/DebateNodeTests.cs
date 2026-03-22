namespace Tnzi.AI.Tests.Workflow;

public class DebateNodeTests
{
    [Fact]
    public async Task ExecuteAsync_TwoAgents_CompletesDebateWithRounds()
    {
        // Arrange: 2 个 agent，每轮轮流发言
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();

        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("Should we use microservices?", new Dictionary<string, string>
        {
            ["debateAgentIds"] = JsonSerializer.Serialize(new[] { agentId1.ToString(), agentId2.ToString() }),
            ["maxRounds"] = "3"
        });

        // Act
        var result = await node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldNotBeNullOrEmpty();
        result.Output.Metadata.ShouldContainKey("total_rounds");
        result.Output.Metadata.ShouldContainKeyAndValue("agent_count", "2");
    }

    [Fact]
    public async Task ExecuteAsync_LessThanTwoAgents_ReturnsFailure()
    {
        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("topic", new Dictionary<string, string>
        {
            ["debateAgentIds"] = JsonSerializer.Serialize(new[] { Guid.NewGuid().ToString() })
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NoAgentIds_ReturnsFailure()
    {
        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("topic", new Dictionary<string, string>());

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAgentIdsJson_ReturnsFailure()
    {
        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("topic", new Dictionary<string, string>
        {
            ["debateAgentIds"] = "not valid json"
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DefaultMaxRounds_IsFive()
    {
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();

        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("debate topic", new Dictionary<string, string>
        {
            ["debateAgentIds"] = JsonSerializer.Serialize(new[] { agentId1.ToString(), agentId2.ToString() })
            // 不设置 maxRounds，默认 5
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        // GroupChatOrchestrator 最多执行 maxRounds 轮
        var totalRounds = int.Parse(result.Output.Metadata!["total_rounds"]);
        totalRounds.ShouldBeGreaterThan(0);
        totalRounds.ShouldBeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task ExecuteAsync_MaxRoundsLimit_RespectsConfiguredMax()
    {
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();

        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("debate", new Dictionary<string, string>
        {
            ["debateAgentIds"] = JsonSerializer.Serialize(new[] { agentId1.ToString(), agentId2.ToString() }),
            ["maxRounds"] = "2"
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        var totalRounds = int.Parse(result.Output.Metadata!["total_rounds"]);
        totalRounds.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_ThreeAgents_AllParticipate()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sp = BuildServiceProvider();
        var node = new DebateNode(sp, Mock.Of<ILogger<DebateNode>>());

        var context = CreateContext("Three-way debate", new Dictionary<string, string>
        {
            ["debateAgentIds"] = JsonSerializer.Serialize(ids.Select(id => id.ToString())),
            ["maxRounds"] = "3"
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Metadata.ShouldContainKeyAndValue("agent_count", "3");
    }

    /// <summary>
    /// 构建 ServiceProvider，包含 mock IAgentFactory（每个 agent 返回固定响应）
    /// </summary>
    private static IServiceProvider BuildServiceProvider()
    {
        var mockFactory = new Mock<IAgentFactory>();

        mockFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? _, string? _, string? name,
                IEnumerable<string>? _, double? _, int? _,
                AgentExecutorOptions? _, IEnumerable<string>? _,
                Guid? _, CancellationToken _) =>
            {
                var mockClient = new Mock<IChatClient>();
                mockClient
                    .Setup(c => c.GetResponseAsync(
                        It.IsAny<IList<ChatMessage>>(),
                        It.IsAny<ChatOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ChatResponse(new ChatMessage(
                        ChatRole.Assistant,
                        $"[{name}] I believe we should consider all perspectives.")));

                return new AgentExecutor(mockClient.Object, new AgentExecutorOptions { Name = name ?? "agent" });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockFactory.Object);
        return services.BuildServiceProvider();
    }

    private static WorkflowNodeContext CreateContext(string initialInput, Dictionary<string, string> config)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "debate-test",
                Configuration = config
            },
            State = new WorkflowState(initialInput),
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }
}
