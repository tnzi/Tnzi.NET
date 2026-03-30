namespace Tnzi.AI.Tests.Workflow;

public class RouterNodeTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesToCorrectKey_WhenLlmReturnsExactMatch()
    {
        // Arrange
        var (node, _) = CreateNodeWithMockLlm("technical");

        var context = CreateContext("How do I fix this bug?", new Dictionary<string, string>
        {
            ["routes"] = """{"technical":"Technical issues","billing":"Billing questions"}"""
        });

        // Act
        var result = await node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("technical");
        result.Output.Metadata.ShouldContainKeyAndValue("route", "technical");
    }

    [Fact]
    public async Task ExecuteAsync_RoutesToCorrectKey_CaseInsensitiveMatch()
    {
        var (node, _) = CreateNodeWithMockLlm("BILLING");

        var context = CreateContext("Invoice question", new Dictionary<string, string>
        {
            ["routes"] = """{"technical":"Tech","billing":"Billing"}"""
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("billing");
    }

    [Fact]
    public async Task ExecuteAsync_ContainsMatch_WhenNoExactMatch()
    {
        // LLM 返回包含路由 key 的文本
        var (node, _) = CreateNodeWithMockLlm("I think this is a technical issue");

        var context = CreateContext("question", new Dictionary<string, string>
        {
            ["routes"] = """{"technical":"Tech","billing":"Billing"}"""
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("technical");
    }

    [Fact]
    public async Task ExecuteAsync_FallbackToFirstRoute_WhenNoMatch()
    {
        var (node, _) = CreateNodeWithMockLlm("completely unrelated response");

        var context = CreateContext("question", new Dictionary<string, string>
        {
            ["routes"] = """{"technical":"Tech","billing":"Billing"}"""
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("technical"); // fallback 到第一个
    }

    [Fact]
    public async Task ExecuteAsync_NoRoutes_ReturnsFailure()
    {
        var (node, _) = CreateNodeWithMockLlm("anything");

        var context = CreateContext("question", new Dictionary<string, string>());

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRoutesJson_ReturnsFailure()
    {
        var (node, _) = CreateNodeWithMockLlm("anything");

        var context = CreateContext("question", new Dictionary<string, string>
        {
            ["routes"] = "invalid json"
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_UsesInitialInput_WhenNoDependencies()
    {
        var capturedMessages = new List<ChatMessage>();
        var (node, _) = CreateNodeWithMockLlm("billing", capturedMessages);

        var context = CreateContext("What is my invoice?", new Dictionary<string, string>
        {
            ["routes"] = """{"technical":"Tech","billing":"Billing"}"""
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("What is my invoice?");
    }

    private static (RouterNode node, Mock<IAgentFactory> factory) CreateNodeWithMockLlm(
        string llmResponse,
        List<ChatMessage>? capturedMessages = null)
    {
        var mockFactory = new Mock<IAgentFactory>();

        // 创建真实的 AgentExecutor（使用 mock IChatClient）
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
            {
                if (capturedMessages != null)
                    capturedMessages.AddRange(msgs);
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponse)));

        var executor = new AgentExecutor(
            mockChatClient.Object,
            new AgentExecutorOptions { Name = "router-test" });

        mockFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var nodeCtxMock = new Mock<IWorkflowNodeServiceContext>();
        nodeCtxMock.Setup(c => c.AgentFactory).Returns(mockFactory.Object);
        nodeCtxMock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);

        var node = new RouterNode(nodeCtxMock.Object, Mock.Of<ILogger<RouterNode>>());
        return (node, mockFactory);
    }

    private static WorkflowNodeContext CreateContext(
        string initialInput,
        Dictionary<string, string> config,
        Dictionary<string, WorkflowStepOutput>? dependencyOutputs = null)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "router-test",
                Configuration = config
            },
            State = new WorkflowState(initialInput),
            DependencyOutputs = dependencyOutputs ?? new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }
}
