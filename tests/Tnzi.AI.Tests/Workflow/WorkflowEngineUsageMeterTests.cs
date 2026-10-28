namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// The engine reports what its nodes consumed even when it does not return normally, counts
/// the tokens of a failed node whatever policy absorbs the failure, and adds a resumed segment
/// to the run's totals instead of overwriting them.
/// </summary>
public class WorkflowEngineUsageMeterTests
{
    private const string MeteredType = "metered-test";
    private const string CancellingType = "cancelling-test";
    private const string FailingType = "failing-test";

    [Fact]
    public async Task ExecuteAsync_SiblingIsCancelled_MeterKeepsTheUsageOfTheNodeThatFinished()
    {
        // Both nodes are in the first layer. One finishes and is billed; the other is the moment
        // the client disconnects. Task.WhenAll then throws, and the finished result never reaches
        // the engine's own totals.
        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(cts);
        services.AddScoped<IWorkflowNode, MeteredNode>();
        services.AddScoped<IWorkflowNode, CancellingNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var provider = services.BuildServiceProvider();

        var graph = new WorkflowGraph([Step("done", MeteredType), Step("aborted", CancellingType)]);
        var meter = new WorkflowUsageMeter();

        var thrown = await Should.ThrowAsync<Exception>(() =>
            new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>())
                .ExecuteAsync(graph, "start", provider, new WorkflowExecutionOptions(), meter, cts.Token));
        thrown.ShouldBeAssignableTo<OperationCanceledException>();

        meter.InputTokens.ShouldBe(100);
        meter.OutputTokens.ShouldBe(50);
    }

    [Fact]
    public async Task ExecuteAsync_FailedNodeUnderSkipPolicy_ItsTokensAreStillCounted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkflowNode, FailingNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var provider = services.BuildServiceProvider();

        var step = Step("flaky", FailingType);
        step.OnError = NodeErrorPolicy.Skip;
        var meter = new WorkflowUsageMeter();

        var result = await new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>())
            .ExecuteAsync(new WorkflowGraph([step]), "start", provider, new WorkflowExecutionOptions(), meter);

        result.Usage.ShouldNotBeNull("the provider billed the failed attempt");
        result.Usage!.InputTokens.ShouldBe(30);
        result.Usage.OutputTokens.ShouldBe(10);
        meter.TotalTokens.ShouldBe(40);
    }

    [Fact]
    public async Task ExecuteAsync_ResumedRun_AddsTheSegmentToTheRunTotals()
    {
        var existingRun = new AgentRun { Id = Guid.NewGuid(), TotalInputTokens = 500, TotalOutputTokens = 250 };
        var runStore = new Mock<IRunStore>();
        runStore.Setup(s => s.GetWithNodesAsync(existingRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existingRun);
        runStore.Setup(s => s.AddNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunNode node, CancellationToken _) => node);

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(s => s.GetCheckpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowCheckpoint?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runStore.Object);
        services.AddScoped<IWorkflowNode, MeteredNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var provider = services.BuildServiceProvider();

        await new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>()).ExecuteAsync(
            new WorkflowGraph([Step("after-approval", MeteredType)]),
            "start",
            provider,
            new WorkflowExecutionOptions
            {
                ExecutionId = "exec-resume",
                RunId = existingRun.Id,
                Resume = true,
                CheckpointStore = checkpointStore.Object
            });

        existingRun.TotalInputTokens.ShouldBe(600, "the run covers both segments, not only the resumed one");
        existingRun.TotalOutputTokens.ShouldBe(300);
    }

    private static WorkflowStepDto Step(string stepId, string nodeType) => new()
    {
        StepId = stepId,
        Configuration = new Dictionary<string, string> { ["nodeType"] = nodeType }
    };

    private sealed class MeteredNode : IWorkflowNode
    {
        public string NodeType => MeteredType;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult
            {
                Output = "ok",
                IsSuccess = true,
                Usage = new TokenUsageDto { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 }
            });
    }

    /// <summary>Stands for the request being aborted while this node waits on its model call.</summary>
    private sealed class CancellingNode(CancellationTokenSource cts) : IWorkflowNode
    {
        public string NodeType => CancellingType;

        public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, CancellationToken.None);
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new WorkflowNodeResult { Output = "never", IsSuccess = true };
        }
    }

    private sealed class FailingNode : IWorkflowNode
    {
        public string NodeType => FailingType;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult
            {
                Output = "bad",
                IsSuccess = false,
                Error = "model returned garbage",
                Usage = new TokenUsageDto { InputTokens = 30, OutputTokens = 10, TotalTokens = 40 }
            });
    }
}
