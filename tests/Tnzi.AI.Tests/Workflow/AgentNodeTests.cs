namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// AgentNode 单元测试 — 覆盖 NodeType、基本执行、步骤级指令覆盖、步骤级 Provider/Model 覆盖
/// </summary>
public class AgentNodeTests
{
    private readonly Mock<IAgentFactory> _mockAgentFactory = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    /// <summary>
    /// 构建 IWorkflowNodeServiceContext，包含 AgentFactory
    /// </summary>
    private IWorkflowNodeServiceContext BuildNodeServiceContext()
    {
        var mock = new Mock<IWorkflowNodeServiceContext>();
        mock.Setup(c => c.AgentFactory).Returns(_mockAgentFactory.Object);
        mock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);
        return mock.Object;
    }

    private AgentNode CreateNode()
    {
        return new AgentNode(
            BuildNodeServiceContext(),
            Mock.Of<ILogger<AgentNode>>());
    }

    private void SetupAgentFactory(string expectedResponse = "Agent response")
    {
        var executor = new AgentExecutor(
            _mockChatClient.Object,
            new AgentExecutorOptions { Name = "test-agent" });

        _mockAgentFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(),
                It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        // 设置 ChatClient 返回响应
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, expectedResponse));
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public void NodeType_ReturnsAgent()
    {
        var node = CreateNode();

        node.NodeType.ShouldBe(WorkflowNodeTypes.Agent);
    }

    [Fact]
    public async Task ExecuteAsync_ValidContext_ReturnsSuccessResult()
    {
        // Arrange
        SetupAgentFactory("Hello from Agent");
        var node = CreateNode();

        var context = new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "agent-step",
                Provider = "openai",
                Model = "gpt-4"
            },
            State = new WorkflowState("Test input"),
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

        // Act
        var result = await node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("Hello from Agent");
    }

    [Fact]
    public async Task ExecuteAsync_StepLevelInstructions_OverridesAgentDefaults()
    {
        // Arrange
        string? capturedInstructions = null;
        var executor = new AgentExecutor(
            _mockChatClient.Object,
            new AgentExecutorOptions { Name = "test-agent" });

        _mockAgentFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(),
                It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, instructions, _, _, _, _, _, _, _, _, _) => capturedInstructions = instructions)
            .ReturnsAsync(executor);

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "response"));
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var node = CreateNode();

        var context = new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "agent-step",
                Instructions = "Custom step instructions"
            },
            State = new WorkflowState("Test input"),
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

        // Act
        await node.ExecuteAsync(context);

        // Assert
        capturedInstructions.ShouldBe("Custom step instructions");
    }

    [Fact]
    public async Task ExecuteAsync_StepLevelProviderModel_UsesOverrides()
    {
        // Arrange
        string? capturedProvider = null;
        string? capturedModel = null;
        var executor = new AgentExecutor(
            _mockChatClient.Object,
            new AgentExecutorOptions { Name = "test-agent" });

        _mockAgentFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(),
                It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (provider, model, _, _, _, _, _, _, _, _, _, _) =>
                {
                    capturedProvider = provider;
                    capturedModel = model;
                })
            .ReturnsAsync(executor);

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "response"));
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var node = CreateNode();

        var context = new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "agent-step",
                Provider = "anthropic",
                Model = "claude-sonnet-4-20250514"
            },
            State = new WorkflowState("Test input"),
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

        // Act
        await node.ExecuteAsync(context);

        // Assert
        capturedProvider.ShouldBe("anthropic");
        capturedModel.ShouldBe("claude-sonnet-4-20250514");
    }
}
