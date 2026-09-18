namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// HITL resume through the real <see cref="WorkflowService"/>, the real engine and the
/// real database checkpoint store.
/// </summary>
/// <remarks>
/// <para>
/// ★ Two defects hid behind each other here. The engine added an interrupted node to the
/// checkpoint's completed set (with the executor's <c>[Awaiting ...]</c> placeholder as its
/// output), and the store's union-merge undid the service's attempt to remove it again.
/// The interrupted node was therefore never re-entered: <c>ResumeWithInputAsync</c> reported
/// <c>Completed</c> while the posted input was discarded and downstream nodes consumed the
/// placeholder; approve-then-resume forwarded the truncated placeholder instead of the
/// approved content.
/// </para>
/// <para>
/// The in-memory checkpoint store used by the engine-level integration tests overwrites
/// on save, so it could not show the union-merge half; the service-level Moq fixtures never
/// drive the engine. This fixture uses both real halves.
/// </para>
/// </remarks>
public class WorkflowHitlResumeIntegrationTests : WorkflowServiceSqliteTestBase
{
    private readonly NodeRecorder _recorder = new();

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton(_recorder);
        services.AddScoped<IWorkflowNode, EchoAgentNode>();
        services.AddScoped<IWorkflowNode, HumanInputNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
    }

    [Fact]
    public async Task ResumeWithInputAsync_HumanInputInterrupt_ReExecutesNodeWithResumeData()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "nodeType": "agent" } },
              { "stepId": "x", "dependsOn": ["a"], "configuration": { "nodeType": "human_input" } },
              { "stepId": "b", "dependsOn": ["x"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Status.ShouldBe("AwaitingInput");
        var executionId = run.Data.ExecutionId!;

        var paused = await LoadExecutionAsync(executionId);
        paused.ShouldNotBeNull();
        paused!.Status.ShouldBe(WorkflowExecutionStatus.AwaitingInput);
        JsonSerializer.Deserialize<List<string>>(paused.CompletedSteps)!
            .ShouldNotContain("x", "an interrupted node is not completed; checkpointing it as completed is what made resume skip it");
        _recorder.Executions.ShouldNotContain("b", "downstream must not run on the placeholder");

        var resumed = await service.ResumeWithInputAsync(executionId, "x", new Dictionary<string, object> { ["answer"] = "42" });
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");

        _recorder.ResumeData.ShouldContainKey("x");
        _recorder.ResumeData["x"]["answer"].ToString().ShouldBe("42");
        _recorder.DependencyOutputs["b"]["x"].ShouldBe("input:42");

        var done = await LoadExecutionAsync(executionId);
        done!.Status.ShouldBe(WorkflowExecutionStatus.Completed);
        var outputs = JsonDocument.Parse(done.StepOutputs).RootElement;
        outputs.GetProperty("x").GetProperty("Text").GetString()!.ShouldNotStartWith("[Awaiting");
    }

    [Fact]
    public async Task ApproveThenResume_ApprovalNode_ForwardsUpstreamContentNotPlaceholder()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "agent" } },
              { "stepId": "approval", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["approval"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        var approve = await service.ApproveStepAsync(executionId, "approval");
        approve.Succeeded.ShouldBeTrue(approve.Message);

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");

        _recorder.Executions.Count(id => id == "publish").ShouldBe(1);
        _recorder.DependencyOutputs["publish"]["approval"].ShouldBe("draft-out");
    }

    [Fact]
    public async Task ApproveWithFeedbackThenResume_ApprovalNode_ForwardsFeedback()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "agent" } },
              { "stepId": "approval", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["approval"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        var executionId = run.Data!.ExecutionId!;

        (await service.ApproveStepAsync(executionId, "approval", "use this text instead")).Succeeded.ShouldBeTrue();
        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");

        _recorder.DependencyOutputs["publish"]["approval"].ShouldBe("use this text instead");
    }

    [Fact]
    public async Task ApproveThenResume_ApprovalNodeWithRequiresApprovalFlag_NeedsOnlyOneApproval()
    {
        // The visual editor writes both markers on an approval step. The node's own
        // interrupt is the gate; the flag must not add a second run-then-gate pause.
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "agent" } },
              { "stepId": "approval", "dependsOn": ["draft"], "requiresApproval": true, "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["approval"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        var executionId = run.Data!.ExecutionId!;

        (await service.ApproveStepAsync(executionId, "approval")).Succeeded.ShouldBeTrue();
        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");
        _recorder.DependencyOutputs["publish"]["approval"].ShouldBe("draft-out");
    }

    /// <summary>
    /// Two approval nodes in one DAG layer both interrupt on the first run. Approving both and
    /// resuming once must complete the run: the resume has to carry ResumeData for every
    /// approved step, not only the first one found, or the second node re-interrupts, overwrites
    /// its own approved metadata with the placeholder and asks to be approved again.
    /// </summary>
    [Fact]
    public async Task ApproveBothThenResume_TwoApprovalNodesInOneLayer_NeedsOneResume()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "agent" } },
              { "stepId": "legal", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "finance", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["legal", "finance"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        (await service.ApproveStepAsync(executionId, "legal", "legal ok")).Succeeded.ShouldBeTrue();
        (await service.ApproveStepAsync(executionId, "finance", "finance ok")).Succeeded.ShouldBeTrue();

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed", "both approvals were already given; a second approval round is a silent re-request");

        _recorder.Executions.Count(id => id == "publish").ShouldBe(1);
        _recorder.DependencyOutputs["publish"]["legal"].ShouldBe("legal ok");
        _recorder.DependencyOutputs["publish"]["finance"].ShouldBe("finance ok");
    }

    /// <summary>
    /// A node that ran to completion in the same layer as an interrupting approval node must be
    /// in the checkpoint: the pause checkpoint used to be written while handling the first
    /// interrupted result, before the later results of the layer were folded into the completed
    /// set, so a completed sibling was re-executed (and re-billed) on resume.
    /// </summary>
    [Fact]
    public async Task ApproveThenResume_CompletedSiblingInTheSameLayer_IsNotReExecuted()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "agent" } },
              { "stepId": "gate", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "side", "dependsOn": ["draft"], "configuration": { "nodeType": "agent" } },
              { "stepId": "publish", "dependsOn": ["gate", "side"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;
        _recorder.Executions.Count(id => id == "side").ShouldBe(1);

        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();
        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");

        _recorder.Executions.Count(id => id == "side").ShouldBe(1, "a completed sibling must be restored from the checkpoint, not run again");
        _recorder.DependencyOutputs["publish"]["side"].ShouldBe("side-out");
    }

    [Fact]
    public async Task RejectThenResume_ApprovalNode_AsksForApprovalAgain_NotDownstreamOnRejectionText()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "agent" } },
              { "stepId": "approval", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["approval"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        var executionId = run.Data!.ExecutionId!;

        (await service.RejectStepAsync(executionId, "approval", "not good enough")).Succeeded.ShouldBeTrue();
        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);

        resumed.Data!.Status.ShouldBe("AwaitingApproval");
        _recorder.Executions.ShouldNotContain("publish");
    }

    // -----------------------------------------------------------------------
    // Test nodes
    // -----------------------------------------------------------------------

    private sealed class NodeRecorder
    {
        public List<string> Executions { get; } = [];
        public Dictionary<string, Dictionary<string, object>> ResumeData { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> DependencyOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Record(WorkflowNodeContext context)
        {
            var stepId = context.Step.StepId!;
            lock (Executions)
            {
                Executions.Add(stepId);
                DependencyOutputs[stepId] = context.DependencyOutputs.ToDictionary(kv => kv.Key, kv => kv.Value.Text, StringComparer.OrdinalIgnoreCase);
                if (context.ResumeData != null)
                {
                    ResumeData[stepId] = context.ResumeData;
                }
            }
        }
    }

    /// <summary>Agent stand-in: records what it saw and emits "{stepId}-out".</summary>
    private sealed class EchoAgentNode : IWorkflowNode
    {
        private readonly NodeRecorder _recorder;
        public EchoAgentNode(NodeRecorder recorder) => _recorder = recorder;
        public string NodeType => WorkflowNodeTypes.Agent;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            _recorder.Record(context);
            return Task.FromResult(new WorkflowNodeResult { Output = $"{context.Step.StepId}-out", IsSuccess = true });
        }
    }

    /// <summary>Custom node that asks for human input and echoes the answer once resumed.</summary>
    private sealed class HumanInputNode : IWorkflowNode
    {
        private readonly NodeRecorder _recorder;
        public HumanInputNode(NodeRecorder recorder) => _recorder = recorder;
        public string NodeType => "human_input";

        public Task<WorkflowInterrupt?> CheckInterruptAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowInterrupt?>(new WorkflowInterrupt
            {
                StepId = context.Step.StepId!,
                Reason = "Need an answer",
                Type = InterruptType.HumanInput,
                RequestedInput = new Dictionary<string, object> { ["answer"] = "string" }
            });

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            _recorder.Record(context);
            var answer = context.ResumeData?.GetValueOrDefault("answer")?.ToString() ?? "<none>";
            return Task.FromResult(new WorkflowNodeResult { Output = $"input:{answer}", IsSuccess = true });
        }
    }
}
