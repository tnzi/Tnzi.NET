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
        services.AddScoped<IWorkflowNode, FlakyNode>();
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

    /// <summary>
    /// A run-then-gate step (<c>requiresApproval</c> on a content node) has already executed when
    /// it pauses: its checkpointed output is the real content its dependents are waiting for. The
    /// approver's comment is a remark about that content, not a replacement for it. It used to
    /// overwrite the step's output text, so <c>publish</c> received "Looks good, ship it" instead
    /// of the draft and the draft was gone from the checkpoint.
    /// </summary>
    [Fact]
    public async Task ApproveWithFeedbackThenResume_RunThenGateStep_KeepsTheGeneratedOutputForDownstream()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "publish", "dependsOn": ["draft"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;
        _recorder.Executions.ShouldNotContain("publish");

        (await service.ApproveStepAsync(executionId, "draft", "Looks good, ship it")).Succeeded.ShouldBeTrue();

        var approved = await LoadExecutionAsync(executionId);
        var draft = JsonDocument.Parse(approved!.StepOutputs).RootElement.GetProperty("draft");
        draft.GetProperty("Text").GetString().ShouldBe("draft-out", "the approved content stays in the checkpoint");
        draft.GetProperty("Metadata").GetProperty("approval_status").GetString().ShouldBe("approved");
        draft.GetProperty("Metadata").GetProperty("approval_feedback").GetString().ShouldBe("Looks good, ship it");

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");

        _recorder.Executions.Count(id => id == "draft").ShouldBe(1, "a run-then-gate step is not executed again after approval");
        _recorder.DependencyOutputs["publish"]["draft"].ShouldBe("draft-out", "downstream consumes what was approved, not the approver's remark");
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

    /// <summary>
    /// A rejected run-then-gate step has already run and sits in the checkpoint's completed set.
    /// Rejecting it left it there, so resuming the failed execution (Failed is resumable) went
    /// straight past the gate: <c>publish</c> ran on the rejection text and the run reported
    /// Completed without anyone approving anything. A rejected step must produce its content
    /// again and be approved again before its dependents run.
    /// </summary>
    [Fact]
    public async Task RejectThenResume_RunThenGateStep_RunsTheStepAgainAndAsksForApproval_NotDownstream()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "publish", "dependsOn": ["draft"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        (await service.RejectStepAsync(executionId, "draft", "not good enough")).Succeeded.ShouldBeTrue();
        var rejected = await LoadExecutionAsync(executionId);
        rejected!.Status.ShouldBe(WorkflowExecutionStatus.Failed);
        JsonSerializer.Deserialize<List<string>>(rejected.CompletedSteps)!
            .ShouldNotContain("draft", "a rejected step is not done; left completed, resume skips its gate");

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);

        resumed.Data!.Status.ShouldBe("AwaitingApproval", "the regenerated content needs a fresh approval");
        _recorder.Executions.ShouldNotContain("publish", "nothing downstream runs on rejected content or on the rejection text");
        _recorder.Executions.Count(id => id == "draft").ShouldBe(2, "the rejected step produces its content again");

        (await service.ApproveStepAsync(executionId, "draft")).Succeeded.ShouldBeTrue();
        var completed = await service.ResumeAsync(executionId);
        completed.Succeeded.ShouldBeTrue(completed.Message);
        completed.Data!.Status.ShouldBe("Completed");
        _recorder.DependencyOutputs["publish"]["draft"].ShouldBe("draft-out");
    }

    /// <summary>
    /// Two run-then-gate steps pause in one layer and one of them is rejected before the other is
    /// decided. The rejection fails the execution and drops the other step's pending approval; that
    /// step must not stay completed either, or a resume runs its dependents on content nobody
    /// approved. An approval already given to a sibling stands.
    /// </summary>
    [Fact]
    public async Task RejectThenResume_UndecidedRunThenGateSibling_IsNotTreatedAsApproved()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "legal", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "finance", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "notes", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "budget", "dependsOn": ["finance"], "configuration": { "nodeType": "agent" } },
              { "stepId": "archive", "dependsOn": ["notes"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        (await service.ApproveStepAsync(executionId, "notes")).Succeeded.ShouldBeTrue();
        (await service.RejectStepAsync(executionId, "legal", "not good enough")).Succeeded.ShouldBeTrue();

        var rejected = await LoadExecutionAsync(executionId);
        var completedSteps = JsonSerializer.Deserialize<List<string>>(rejected!.CompletedSteps)!;
        completedSteps.ShouldNotContain("finance", "an undecided step is not approved");
        completedSteps.ShouldContain("notes", "an approval already given stands");

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("AwaitingApproval");
        _recorder.Executions.ShouldNotContain("budget", "nothing runs on content nobody approved");
        _recorder.Executions.Count(id => id == "notes").ShouldBe(1, "an approved step is not produced again");
    }

    /// <summary>
    /// The checkpoint store union-merges the completed set when a save hits a concurrency conflict.
    /// If one step of a pause is approved while another is rejected, the rejection's save can lose
    /// the race and the merge puts the rejected step back into the completed set, with the
    /// rejection marker on its output and the execution failed. A checkpoint rejected before
    /// rejections removed anything has the same shape. Resuming it must not run the rejected
    /// step's dependents on the rejection text.
    /// </summary>
    [Fact]
    public async Task Resume_RejectedStepBackInTheCompletedSet_IsRunAgainNotPassedDownstream()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "legal", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "finance", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "budget", "dependsOn": ["legal"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;
        (await service.ApproveStepAsync(executionId, "finance")).Succeeded.ShouldBeTrue();
        (await service.RejectStepAsync(executionId, "legal", "not good enough")).Succeeded.ShouldBeTrue();

        // The state the conflict path's union-merge leaves behind.
        var row = await DbContext.Set<WorkflowExecution>().FirstAsync(e => e.ExecutionId == executionId);
        row.CompletedSteps = JsonSerializer.Serialize(new[] { "legal", "finance" });
        await DbContext.SaveChangesAsync();

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("AwaitingApproval", "the rejected step is produced again and needs a fresh approval");
        _recorder.Executions.ShouldNotContain("budget", "nothing runs on the rejection text");
        _recorder.Executions.Count(id => id == "legal").ShouldBe(2);
        _recorder.Executions.Count(id => id == "finance").ShouldBe(1, "the approved step stands");
    }

    /// <summary>
    /// Rejecting a run through the agent-run API rejects the whole pause. The delegator called
    /// RejectStepAsync once per awaiting node; the first call fails the execution, so the second
    /// was refused as "not awaiting approval" and the request failed, leaving the run awaiting
    /// approval and the execution failed. From then on neither approve nor reject could succeed.
    /// </summary>
    [Fact]
    public async Task RejectRun_TwoStepsAwaitingApproval_RejectsThePauseOnceAndFailsTheRun()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "legal", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "finance", "requiresApproval": true, "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        var runStore = new Mock<IRunStore>();
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);
        var delegator = new WorkflowDelegator(service, runStore.Object,
            new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>()));
        var agentRun = new AgentRun
        {
            Id = Guid.NewGuid(),
            WorkflowExecutionId = executionId,
            WorkflowDefinitionId = definition.Id,
            Status = AgentRunStatus.AwaitingApproval,
            Nodes = new List<AgentRunNode>
            {
                new() { Id = Guid.NewGuid(), NodeName = "legal", Status = AgentRunNodeStatus.AwaitingApproval },
                new() { Id = Guid.NewGuid(), NodeName = "finance", Status = AgentRunNodeStatus.AwaitingApproval }
            }
        };

        var result = await delegator.ResumeWorkflowRunAsync(agentRun,
            new ResumeRunInput { ApprovalDecision = "reject", ApprovalComment = "not good enough" }, CancellationToken.None);

        result.Status.ShouldBe(AgentRunStatus.Failed);
        agentRun.Status.ShouldBe(AgentRunStatus.Failed);
        agentRun.Nodes.ShouldAllBe(n => n.Status == AgentRunNodeStatus.Rejected);
        (await LoadExecutionAsync(executionId))!.Status.ShouldBe(WorkflowExecutionStatus.Failed);
    }

    /// <summary>
    /// A run-then-gate step shares a layer with an approval node. The approval node's interrupt
    /// ends the layer before the run-then-gate check, so the step had run, sat in the completed
    /// set and was never put to anyone: approving the approval node and resuming ran its
    /// dependents on content nobody approved.
    /// </summary>
    [Fact]
    public async Task ApproveNodeThenResume_RunThenGateStepInTheSameLayer_IsStillGated()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "gate", "configuration": { "nodeType": "approval" } },
              { "stepId": "draft", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "publish", "dependsOn": ["draft"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("AwaitingApproval");
        var executionId = run.Data.ExecutionId!;

        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();
        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("AwaitingApproval", "the run-then-gate step has not been approved yet");
        _recorder.Executions.ShouldNotContain("publish");

        (await service.ApproveStepAsync(executionId, "draft")).Succeeded.ShouldBeTrue();
        var completed = await service.ResumeAsync(executionId);
        completed.Succeeded.ShouldBeTrue(completed.Message);
        completed.Data!.Status.ShouldBe("Completed");
        _recorder.DependencyOutputs["publish"]["draft"].ShouldBe("draft-out");
    }

    /// <summary>
    /// A sibling's failure ends the layer before the run-then-gate check in the same way. Resuming
    /// the failed execution must not treat the unapproved step as approved.
    /// </summary>
    [Fact]
    public async Task ResumeAfterASiblingFailed_RunThenGateStepInTheSameLayer_IsStillGated()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "requiresApproval": true, "configuration": { "nodeType": "agent" } },
              { "stepId": "fetch", "configuration": { "nodeType": "flaky" } },
              { "stepId": "publish", "dependsOn": ["draft"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("Failed");
        var executionId = run.Data.ExecutionId!;

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("AwaitingApproval", "the run-then-gate step has not been approved yet");
        _recorder.Executions.ShouldNotContain("publish");
    }

    /// <summary>
    /// Resuming a failed execution is how a failed node is retried. The failed node used to be
    /// checkpointed as completed, so the resume skipped it and ran its dependents on the failure's
    /// output, then reported Completed.
    /// </summary>
    [Fact]
    public async Task ResumeAfterANodeFailed_RetriesTheFailedNode_NotItsDependentsOnTheFailure()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "fetch", "configuration": { "nodeType": "flaky" } },
              { "stepId": "summarize", "dependsOn": ["fetch"], "configuration": { "nodeType": "agent" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start");
        run.Data!.Status.ShouldBe("Failed");
        var executionId = run.Data.ExecutionId!;
        var failed = await LoadExecutionAsync(executionId);
        JsonSerializer.Deserialize<List<string>>(failed!.CompletedSteps)!
            .ShouldNotContain("fetch", "a failed node is not done");

        var resumed = await service.ResumeAsync(executionId);
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");
        _recorder.Executions.Count(id => id == "fetch").ShouldBe(2, "the resume retries the failed node");
        _recorder.DependencyOutputs["summarize"]["fetch"].ShouldBe("fetch-out");
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

    /// <summary>Fails the first time a step runs and emits "{stepId}-out" from then on.</summary>
    private sealed class FlakyNode : IWorkflowNode
    {
        private readonly NodeRecorder _recorder;
        public FlakyNode(NodeRecorder recorder) => _recorder = recorder;
        public string NodeType => "flaky";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            _recorder.Record(context);
            var stepId = context.Step.StepId!;
            int attempts;
            lock (_recorder.Executions)
            {
                attempts = _recorder.Executions.Count(id => id == stepId);
            }

            return Task.FromResult(attempts == 1
                ? new WorkflowNodeResult { Output = string.Empty, IsSuccess = false, Error = "transient failure" }
                : new WorkflowNodeResult { Output = $"{stepId}-out", IsSuccess = true });
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
