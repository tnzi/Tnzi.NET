namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// <see cref="WorkflowService.RunStreamingAsync"/> against the real engine and the real execution row.
/// </summary>
/// <remarks>
/// The stream used to await the whole run and then replay the step results: a long workflow sent
/// nothing, not even response headers, until its last node finished, and behind a proxy with an
/// idle timeout the connection was cut, the request cancelled the engine and the execution was
/// recorded as failed. Events now leave as steps finish, while later nodes are still running.
/// </remarks>
public class WorkflowServiceStepStreamingTests : WorkflowServiceSqliteTestBase
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly NodeGate _gate = new();

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton(_gate);
        services.AddScoped<IWorkflowNode, EchoNode>();
        services.AddScoped<IWorkflowNode, GatedNode>();
    }

    private const string FirstThenGated = """
        [
          { "stepId": "a", "configuration": { "nodeType": "agent" } },
          { "stepId": "b", "dependsOn": ["a"], "configuration": { "nodeType": "gated-test" } }
        ]
        """;

    [Fact]
    public async Task RunStreaming_StepEventsLeaveWhileLaterNodesStillRun()
    {
        var definition = await InsertDefinitionAsync(FirstThenGated);
        var stream = CreateService().RunStreamingAsync(definition.Id, "start").GetAsyncEnumerator();

        try
        {
            var started = await NextAsync(stream);
            started.Status.ShouldBe("Running", "the first event says the run has started");
            started.ExecutionId.ShouldNotBeNullOrWhiteSpace("the caller can follow or cancel the run from the first event");
            started.StepResults.ShouldBeNull();

            var first = await NextAsync(stream);
            first.StepResults.ShouldNotBeNull().ShouldHaveSingleItem().StepId.ShouldBe("a");
            first.Output.ShouldBe("a-out");
            _gate.Entered.Task.IsCompleted.ShouldBeFalse("the engine waits while the consumer handles an event");

            var second = NextAsync(stream);
            await _gate.Entered.Task.WaitAsync(EventTimeout);
            second.IsCompleted.ShouldBeFalse("step b has not finished yet");

            _gate.Release.SetResult();
            (await second).StepResults.ShouldNotBeNull().ShouldHaveSingleItem().StepId.ShouldBe("b");

            var summary = await NextAsync(stream);
            summary.Status.ShouldBe("Completed");
            summary.StepResults.ShouldNotBeNull().Select(s => s.StepId).ShouldBe(["a", "b"]);
            (await stream.MoveNextAsync()).ShouldBeFalse();

            (await LoadExecutionAsync(summary.ExecutionId!))!.Status.ShouldBe(WorkflowExecutionStatus.Completed);
        }
        finally
        {
            _gate.Release.TrySetResult();
            await stream.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunStreaming_ConsumerStopsEarly_TheRunStillFinishesAndIsRecorded()
    {
        var definition = await InsertDefinitionAsync(FirstThenGated);
        var stream = CreateService().RunStreamingAsync(definition.Id, "start").GetAsyncEnumerator();

        var started = await NextAsync(stream);
        (await NextAsync(stream)).StepResults.ShouldNotBeNull().ShouldHaveSingleItem().StepId.ShouldBe("a");

        // The consumer goes away after step a. The engine was waiting for it to ask for the next
        // event; it must stop waiting and finish the run, not hang the disposal forever. The outcome
        // is the engine's, as it was when the stream replayed a finished run.
        var disposal = stream.DisposeAsync().AsTask();
        await _gate.Entered.Task.WaitAsync(EventTimeout);
        disposal.IsCompleted.ShouldBeFalse("disposal waits for the run it is attached to");
        _gate.Release.SetResult();
        await disposal.WaitAsync(EventTimeout);

        (await LoadExecutionAsync(started.ExecutionId!))!.Status.ShouldBe(WorkflowExecutionStatus.Completed);
    }

    [Fact]
    public async Task RunStreaming_ConsumerStopsAfterTheStartedEvent_TheRunStillRuns()
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");
        var stream = CreateService().RunStreamingAsync(definition.Id, "start").GetAsyncEnumerator();

        var started = await NextAsync(stream);
        await stream.DisposeAsync().AsTask().WaitAsync(EventTimeout);

        (await LoadExecutionAsync(started.ExecutionId!))!.Status.ShouldBe(WorkflowExecutionStatus.Completed);
    }

    private static async Task<WorkflowExecutionResultDto> NextAsync(IAsyncEnumerator<WorkflowExecutionResultDto> stream)
    {
        (await stream.MoveNextAsync().AsTask().WaitAsync(EventTimeout)).ShouldBeTrue();
        return stream.Current;
    }

    private sealed class NodeGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class EchoNode : IWorkflowNode
    {
        public string NodeType => WorkflowNodeTypes.Agent;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult { Output = $"{context.Step.StepId}-out", IsSuccess = true });
    }

    /// <summary>Runs until the test releases it.</summary>
    private sealed class GatedNode(NodeGate gate) : IWorkflowNode
    {
        public string NodeType => "gated-test";

        public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
            return new WorkflowNodeResult { Output = $"{context.Step.StepId}-out", IsSuccess = true };
        }
    }
}
