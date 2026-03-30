namespace Tnzi.AI.Tests.Workflow;

public class ParallelNodeTests
{
    [Fact]
    public async Task ExecuteAsync_MultipleWorkers_AllExecuteAndMerge()
    {
        // Arrange
        var sp = BuildNodeServiceContext(new Dictionary<string, string>
        {
            ["w1"] = "Result from worker 1",
            ["w2"] = "Result from worker 2"
        });

        var node = new ParallelNode(sp, Mock.Of<ILogger<ParallelNode>>());

        var context = CreateContext("analyze this", new Dictionary<string, string>
        {
            ["workers"] = """[{"stepId":"w1"},{"stepId":"w2"}]"""
        });

        // Act
        var result = await node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldContain("[w1]");
        result.Output.Text.ShouldContain("Result from worker 1");
        result.Output.Text.ShouldContain("[w2]");
        result.Output.Text.ShouldContain("Result from worker 2");
        result.Output.Metadata.ShouldContainKeyAndValue("worker_w1", "success");
        result.Output.Metadata.ShouldContainKeyAndValue("worker_w2", "success");
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkers_ReturnsFailure()
    {
        var sp = BuildNodeServiceContext(new Dictionary<string, string>());
        var node = new ParallelNode(sp, Mock.Of<ILogger<ParallelNode>>());

        var context = CreateContext("input", new Dictionary<string, string>());

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidWorkersJson_ReturnsFailure()
    {
        var sp = BuildNodeServiceContext(new Dictionary<string, string>());
        var node = new ParallelNode(sp, Mock.Of<ILogger<ParallelNode>>());

        var context = CreateContext("input", new Dictionary<string, string>
        {
            ["workers"] = "not json"
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_OneWorkerFails_ResultContainsError()
    {
        // 构建一个 factory，让 w2 抛出异常
        var mockFactory = new Mock<IAgentFactory>();
        var callCount = 0;

        mockFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 2)
                {
                    // 第二个 worker 返回一个会抛异常的 executor
                    var failClient = new Mock<IChatClient>();
                    failClient
                        .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("LLM call failed"));
                    return new AgentExecutor(failClient.Object, new AgentExecutorOptions { Name = "fail" });
                }

                var okClient = new Mock<IChatClient>();
                okClient
                    .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK")));
                return new AgentExecutor(okClient.Object, new AgentExecutorOptions { Name = "ok" });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockFactory.Object);
        var builtSp = services.BuildServiceProvider();

        var nodeCtxMock = new Mock<IWorkflowNodeServiceContext>();
        nodeCtxMock.Setup(c => c.AgentFactory).Returns(mockFactory.Object);
        nodeCtxMock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);
        nodeCtxMock.Setup(c => c.CreateScope()).Returns(() => builtSp.CreateScope());

        var node = new ParallelNode(nodeCtxMock.Object, Mock.Of<ILogger<ParallelNode>>());

        var context = CreateContext("input", new Dictionary<string, string>
        {
            ["workers"] = """[{"stepId":"w1"},{"stepId":"w2"}]"""
        });

        var result = await node.ExecuteAsync(context);

        // 部分失败时 IsSuccess 为 false
        result.IsSuccess.ShouldBeFalse();
        // 但输出仍包含成功 worker 的结果
        result.Output.Text.ShouldContain("[w1]");
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesTokenUsage()
    {
        var sp = BuildNodeServiceContext(new Dictionary<string, string>
        {
            ["w1"] = "result1",
            ["w2"] = "result2"
        }, inputTokens: 10, outputTokens: 20);

        var node = new ParallelNode(sp, Mock.Of<ILogger<ParallelNode>>());

        var context = CreateContext("input", new Dictionary<string, string>
        {
            ["workers"] = """[{"stepId":"w1"},{"stepId":"w2"}]"""
        });

        var result = await node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Usage.ShouldNotBeNull();
        result.Usage!.InputTokens.ShouldBe(20);  // 10 * 2 workers
        result.Usage!.OutputTokens.ShouldBe(40);  // 20 * 2 workers
    }

    private static IWorkflowNodeServiceContext BuildNodeServiceContext(
        Dictionary<string, string> workerResponses,
        int inputTokens = 0,
        int outputTokens = 0)
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
                var responseText = workerResponses.GetValueOrDefault(name ?? "", "default response");

                var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
                if (inputTokens > 0 || outputTokens > 0)
                {
                    chatResponse.Usage = new UsageDetails
                    {
                        InputTokenCount = inputTokens,
                        OutputTokenCount = outputTokens
                    };
                }

                var mockClient = new Mock<IChatClient>();
                mockClient
                    .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(chatResponse);

                return new AgentExecutor(mockClient.Object, new AgentExecutorOptions { Name = name ?? "worker" });
            });

        // 构建 IServiceProvider 用于 CreateScope（ParallelNode 每个 worker 需要独立 scope）
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockFactory.Object);
        var sp = services.BuildServiceProvider();

        var mock = new Mock<IWorkflowNodeServiceContext>();
        mock.Setup(c => c.AgentFactory).Returns(mockFactory.Object);
        mock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);
        mock.Setup(c => c.CreateScope()).Returns(() => sp.CreateScope());
        return mock.Object;
    }

    private static WorkflowNodeContext CreateContext(string initialInput, Dictionary<string, string> config)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "parallel-test",
                Configuration = config
            },
            State = new WorkflowState(initialInput),
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }
}
