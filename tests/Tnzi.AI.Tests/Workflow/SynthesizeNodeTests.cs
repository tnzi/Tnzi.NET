namespace Tnzi.AI.Tests.Workflow;

public class SynthesizeNodeTests
{
    [Fact]
    public async Task ExecuteAsync_CombinesMultipleInputs_ReturnsSynthesizedOutput()
    {
        var (node, _) = CreateNodeWithMockLlm("Combined summary of all inputs.");

        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["analyst-1"] = new WorkflowStepOutput { Text = "Analysis from perspective A" },
            ["analyst-2"] = new WorkflowStepOutput { Text = "Analysis from perspective B" }
        };

        var context = CreateContext("Synthesize these", depOutputs);

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("Combined summary of all inputs.");
        var metadata = result.Output.Metadata;
        metadata.ShouldNotBeNull();
        metadata.ShouldContainKeyAndValue("source_count", "2");
    }

    [Fact]
    public async Task ExecuteAsync_NoDependencies_UsesInitialInput()
    {
        var (node, _) = CreateNodeWithMockLlm("Synthesized initial input.");

        var context = CreateContext("Raw input text");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("Synthesized initial input.");
        var metadata = result.Output.Metadata;
        metadata.ShouldNotBeNull();
        metadata.ShouldContainKeyAndValue("source_count", "0");
    }

    [Fact]
    public async Task ExecuteAsync_SingleDependency_IncludesInPrompt()
    {
        var capturedMessages = new List<ChatMessage>();
        var (node, _) = CreateNodeWithMockLlm("Summary.", capturedMessages);

        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["step1"] = new WorkflowStepOutput { Text = "Output from step 1" }
        };

        var context = CreateContext("Synthesize", depOutputs);

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        var metadata = result.Output.Metadata;
        metadata.ShouldNotBeNull();
        metadata.ShouldContainKeyAndValue("source_count", "1");

        // 验证 LLM 收到的消息包含上游输出
        capturedMessages.ShouldNotBeEmpty();
        var userMessage = capturedMessages.First(m => m.Role == ChatRole.User);
        var userMessageText = userMessage.Text;
        userMessageText.ShouldNotBeNull();
        userMessageText.ShouldContain("step1");
        userMessageText.ShouldContain("Output from step 1");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsUsageFromLlm()
    {
        var (node, _) = CreateNodeWithMockLlm("result", usage: new UsageDetails
        {
            InputTokenCount = 100,
            OutputTokenCount = 50
        });

        var context = CreateContext("input");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Usage.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CustomInstructions_IncludedInPrompt()
    {
        var capturedMessages = new List<ChatMessage>();
        var (node, _) = CreateNodeWithMockLlm("summary", capturedMessages);

        var context = CreateContext("input", instructions: "Focus on key findings only.");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        // 用户消息应包含自定义指令
        capturedMessages.ShouldNotBeEmpty();
        var userMessage = capturedMessages.First(m => m.Role == ChatRole.User);
        var userMessageText = userMessage.Text;
        userMessageText.ShouldNotBeNull();
        userMessageText.ShouldContain("Focus on key findings only.");
    }

    private static (SynthesizeNode node, Mock<IAgentFactory> factory) CreateNodeWithMockLlm(
        string llmResponse,
        List<ChatMessage>? capturedMessages = null,
        UsageDetails? usage = null)
    {
        var mockFactory = new Mock<IAgentFactory>();
        var mockChatClient = new Mock<IChatClient>();

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponse));
        if (usage != null)
            chatResponse.Usage = usage;

        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
            {
                capturedMessages?.AddRange(msgs);
            })
            .ReturnsAsync(chatResponse);

        var executor = new AgentExecutor(
            mockChatClient.Object,
            new AgentExecutorOptions { Name = "synthesize-test" });

        mockFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var nodeCtxMock = new Mock<IWorkflowNodeServiceContext>();
        nodeCtxMock.Setup(c => c.AgentFactory).Returns(mockFactory.Object);
        nodeCtxMock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);

        var node = new SynthesizeNode(nodeCtxMock.Object, Mock.Of<ILogger<SynthesizeNode>>());
        return (node, mockFactory);
    }

    private static WorkflowNodeContext CreateContext(
        string initialInput,
        Dictionary<string, WorkflowStepOutput>? depOutputs = null,
        string? instructions = null)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "synth-test",
                Instructions = instructions,
                Configuration = new Dictionary<string, string>()
            },
            State = new WorkflowState(initialInput),
            DependencyOutputs = depOutputs ?? new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }
}
