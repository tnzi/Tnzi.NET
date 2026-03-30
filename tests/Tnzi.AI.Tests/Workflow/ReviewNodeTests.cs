namespace Tnzi.AI.Tests.Workflow;

public class ReviewNodeTests
{
    [Fact]
    public async Task ExecuteAsync_AcceptsOnFirstRound_ReturnsAcceptVerdict()
    {
        var (node, _) = CreateNodeWithMockLlm("""{"verdict":"accept","feedback":"Looks great!"}""");

        var context = CreateContext("Review this code");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("accept");
        result.Output.Metadata.ShouldContainKeyAndValue("verdict", "accept");
        result.Output.Metadata.ShouldContainKeyAndValue("feedback", "Looks great!");
        result.Output.Text.ShouldBe("Looks great!");
    }

    [Fact]
    public async Task ExecuteAsync_ReworkVerdict_RoutesToRework()
    {
        var (node, _) = CreateNodeWithMockLlm("""{"verdict":"rework","feedback":"Needs improvement"}""");

        var context = CreateContext("Review this");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("rework");
        result.Output.Metadata.ShouldContainKeyAndValue("verdict", "rework");
    }

    [Fact]
    public async Task ExecuteAsync_RejectVerdict_RoutesToReject()
    {
        var (node, _) = CreateNodeWithMockLlm("""{"verdict":"reject","feedback":"Not acceptable"}""");

        var context = CreateContext("Review this");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("reject");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidVerdict_DefaultsToReject()
    {
        var (node, _) = CreateNodeWithMockLlm("""{"verdict":"maybe","feedback":"Not sure"}""");

        var context = CreateContext("Review this");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("reject"); // 无效 verdict 默认 reject
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonResponse_ExtractsVerdictFromText()
    {
        var (node, _) = CreateNodeWithMockLlm("I think we should accept this submission.");

        var context = CreateContext("Review this");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("accept"); // 从文本中包含匹配
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonNoMatch_DefaultsToReject()
    {
        var (node, _) = CreateNodeWithMockLlm("This is an interesting piece of work.");

        var context = CreateContext("Review this");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("reject"); // 无匹配默认 reject
    }

    [Fact]
    public async Task ExecuteAsync_JsonWithMarkdownFence_ParsesCorrectly()
    {
        var responseWithFence = """
            ```json
            {"verdict":"accept","feedback":"Well done"}
            ```
            """;

        var (node, _) = CreateNodeWithMockLlm(responseWithFence);

        var context = CreateContext("Review this");

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("accept");
        result.Output.Text.ShouldBe("Well done");
    }

    [Fact]
    public async Task ExecuteAsync_CustomVerdictOptions_RespectsCustomOptions()
    {
        var (node, _) = CreateNodeWithMockLlm("""{"verdict":"pass","feedback":"OK"}""");

        var context = CreateContext("Review this", new Dictionary<string, string>
        {
            ["verdictOptions"] = "pass,fail,skip"
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("pass");
    }

    [Fact]
    public async Task ExecuteAsync_UsesUpstreamDependencyOutput()
    {
        var (node, _) = CreateNodeWithMockLlm("""{"verdict":"accept","feedback":"Approved"}""");

        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["writer-step"] = new WorkflowStepOutput { Text = "Draft content from writer" }
        };

        var context = CreateContext("Review the writer output", depOutputs: depOutputs);

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("accept");
    }

    private static (ReviewNode node, Mock<IAgentFactory> factory) CreateNodeWithMockLlm(string llmResponse)
    {
        var mockFactory = new Mock<IAgentFactory>();
        var mockChatClient = new Mock<IChatClient>();

        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponse)));

        var executor = new AgentExecutor(
            mockChatClient.Object,
            new AgentExecutorOptions { Name = "review-test" });

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

        var node = new ReviewNode(nodeCtxMock.Object, Mock.Of<ILogger<ReviewNode>>());
        return (node, mockFactory);
    }

    private static WorkflowNodeContext CreateContext(
        string initialInput,
        Dictionary<string, string>? config = null,
        Dictionary<string, WorkflowStepOutput>? depOutputs = null)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "review-test",
                Configuration = config ?? new Dictionary<string, string>()
            },
            State = new WorkflowState(initialInput),
            DependencyOutputs = depOutputs ?? new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }
}
