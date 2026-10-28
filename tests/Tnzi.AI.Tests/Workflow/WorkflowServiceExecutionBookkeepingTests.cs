using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// What the execution row records about a run and what is read back from it: wait reasons, durations of
/// resumed executions, the agent run projection of executions that leave their wait, the history page and the
/// editor's execution stats.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The terminal update cleared the wait reason the checkpoint store had just written, so a paused run showed
/// no reason. A resume never ran the terminal update at all: every execution that went through an approval had no
/// duration and fell out of the stats.</item>
/// <item>Cancelling a waiting execution or rejecting its approval through the workflow endpoints left its agent
/// run waiting for good, so the agent-runs list offered an approval the workflow service then refused.</item>
/// <item>The history page loaded whole rows, every node's model output included, to count two arrays; the stats
/// loaded every execution of the workflow to compute four numbers.</item>
/// </list>
/// </remarks>
public class WorkflowServiceExecutionBookkeepingTests : WorkflowServiceSqliteTestBase
{
    private readonly List<string> _commands = [];

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
        base.ConfigureDbContextOptions(options);
        options.LogTo(command => { lock (_commands) _commands.Add(command); }, [RelationalEventId.CommandExecuted]);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IWorkflowExecutionMailbox, WorkflowExecutionMailboxService>();
    }

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddScoped<IWorkflowNode, EchoAgentNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
        services.AddScoped<IWorkflowNode, AskNode>();
    }

    private const string ApprovalThenPublish = """
        [
          { "stepId": "gate", "configuration": { "nodeType": "approval" } },
          { "stepId": "publish", "dependsOn": ["gate"], "configuration": { "nodeType": "agent" } }
        ]
        """;

    // -----------------------------------------------------------------------
    // Wait reason and duration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Run_PausedForApproval_KeepsTheApprovalWaitReason()
    {
        var definition = await InsertDefinitionAsync(ApprovalThenPublish);

        var run = await CreateService().RunAsync(definition.Id, "start");

        run.Data!.Status.ShouldBe("AwaitingApproval");
        var row = await LoadExecutionAsync(run.Data.ExecutionId!);
        row!.CurrentWaitReason.ShouldBe("approval");
    }

    [Fact]
    public async Task Run_PausedForInput_KeepsTheInputWaitReason()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "ask", "configuration": { "nodeType": "ask" } },
              { "stepId": "publish", "dependsOn": ["ask"], "configuration": { "nodeType": "agent" } }
            ]
            """);

        var run = await CreateService().RunAsync(definition.Id, "start");

        run.Data!.Status.ShouldBe("AwaitingInput");
        var row = await LoadExecutionAsync(run.Data.ExecutionId!);
        row!.CurrentWaitReason.ShouldBe("input");
    }

    [Fact]
    public async Task Resume_ToCompletion_RecordsTheDuration()
    {
        var definition = await InsertDefinitionAsync(ApprovalThenPublish);
        var service = CreateService();
        var run = await service.RunAsync(definition.Id, "start");
        var executionId = run.Data!.ExecutionId!;
        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();

        var resumed = await service.ResumeAsync(executionId);

        resumed.Data!.Status.ShouldBe("Completed");
        DbContext.ChangeTracker.Clear();
        var row = await LoadExecutionAsync(executionId);
        row!.Status.ShouldBe(WorkflowExecutionStatus.Completed);
        row.DurationMs.ShouldNotBeNull("an execution that went through an approval is counted in the stats like any other");
        row.CurrentWaitReason.ShouldBeNull();
    }

    // -----------------------------------------------------------------------
    // Agent run projection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cancel_WaitingExecution_CancelsItsWaitingAgentRun()
    {
        var definition = await InsertDefinitionAsync(ApprovalThenPublish);
        var service = CreateService();
        var executionId = (await service.RunAsync(definition.Id, "start")).Data!.ExecutionId!;
        var agentRun = new AgentRun { Id = Guid.NewGuid(), WorkflowExecutionId = executionId, Status = AgentRunStatus.AwaitingApproval };
        SetupRunLookup(agentRun);

        var result = await service.CancelAsync(executionId, "no longer needed");

        result.Succeeded.ShouldBeTrue(result.Message);
        RunRepository.Verify(r => r.UpdateAsync(It.Is<AgentRun>(a => a.Id == agentRun.Id && a.Status == AgentRunStatus.Cancelled), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_ThroughTheWorkflowService_FailsTheWaitingAgentRun()
    {
        var definition = await InsertDefinitionAsync(ApprovalThenPublish);
        var service = CreateService();
        var executionId = (await service.RunAsync(definition.Id, "start")).Data!.ExecutionId!;
        var agentRun = new AgentRun { Id = Guid.NewGuid(), WorkflowExecutionId = executionId, Status = AgentRunStatus.AwaitingApproval };
        SetupRunLookup(agentRun);

        var result = await service.RejectStepAsync(executionId, "gate", "not good enough");

        result.Succeeded.ShouldBeTrue(result.Message);
        RunRepository.Verify(r => r.UpdateAsync(
            It.Is<AgentRun>(a => a.Id == agentRun.Id && a.Status == AgentRunStatus.Failed && a.Error == "not good enough"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_RunWhoseAgentRunAlreadyEnded_LeavesItAlone()
    {
        var definition = await InsertDefinitionAsync(ApprovalThenPublish);
        var service = CreateService();
        var executionId = (await service.RunAsync(definition.Id, "start")).Data!.ExecutionId!;
        SetupRunLookup(new AgentRun { Id = Guid.NewGuid(), WorkflowExecutionId = executionId, Status = AgentRunStatus.Failed });

        (await service.CancelAsync(executionId)).Succeeded.ShouldBeTrue();

        RunRepository.Verify(r => r.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // History page
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetExecutions_DoesNotLoadStepOutputsOrInput()
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");
        await InsertExecutionAsync(definition.Id, WorkflowExecutionStatus.Completed, durationMs: 10, completedSteps: """["a","b"]""");

        lock (_commands) _commands.Clear();
        var page = await CreateService().GetExecutionsAsync(new WorkflowExecutionQueryDto { WorkflowDefinitionId = definition.Id });

        page.Data!.Items.ShouldHaveSingleItem().CompletedStepCount.ShouldBe(2);
        string[] commands;
        lock (_commands) commands = [.. _commands];
        commands.ShouldNotBeEmpty();
        commands.ShouldAllBe(c => !c.Contains("StepOutputs") && !c.Contains("InitialInput") && !c.Contains("PendingSignalsJson"));
    }

    // -----------------------------------------------------------------------
    // Execution stats
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetExecutionStats_ComputesTheSameNumbersInTheDatabase()
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");
        var other = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""", name: "other");

        // 20 durations 10..200: P95 index = ceil(20 * 0.95) - 1 = 18 -> 190.
        for (var i = 1; i <= 20; i++)
            await InsertExecutionAsync(definition.Id, i <= 15 ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Failed, durationMs: i * 10);
        // Running, no duration yet: counts as an execution, not as a duration.
        await InsertExecutionAsync(definition.Id, WorkflowExecutionStatus.Running, durationMs: null);
        await InsertExecutionAsync(other.Id, WorkflowExecutionStatus.Completed, durationMs: 99_999);

        var stats = (await CreateService().GetExecutionStatsAsync(definition.Id)).Data!;

        stats.TotalExecutions.ShouldBe(21);
        stats.SuccessRate.ShouldBe(15d / 21, 1e-9);
        stats.AvgDurationMs!.Value.ShouldBe(105d, 1e-9);
        stats.MinDurationMs.ShouldBe(10);
        stats.MaxDurationMs.ShouldBe(200);
        stats.P95DurationMs.ShouldBe(190);
    }

    [Fact]
    public async Task GetExecutionStats_SingleDuration_IsItsOwnPercentile()
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");
        await InsertExecutionAsync(definition.Id, WorkflowExecutionStatus.Completed, durationMs: 42);

        var stats = (await CreateService().GetExecutionStatsAsync(definition.Id)).Data!;

        stats.TotalExecutions.ShouldBe(1);
        stats.SuccessRate.ShouldBe(1d);
        stats.AvgDurationMs.ShouldBe((double?)42d);
        stats.MinDurationMs.ShouldBe(42);
        stats.MaxDurationMs.ShouldBe(42);
        stats.P95DurationMs.ShouldBe(42);
    }

    [Fact]
    public async Task GetExecutionStats_NoDurations_LeavesTheDurationsEmpty()
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");
        await InsertExecutionAsync(definition.Id, WorkflowExecutionStatus.Running, durationMs: null);

        var stats = (await CreateService().GetExecutionStatsAsync(definition.Id)).Data!;

        stats.TotalExecutions.ShouldBe(1);
        stats.SuccessRate.ShouldBe(0d);
        stats.AvgDurationMs.ShouldBeNull();
        stats.MinDurationMs.ShouldBeNull();
        stats.MaxDurationMs.ShouldBeNull();
        stats.P95DurationMs.ShouldBeNull();
    }

    [Fact]
    public async Task GetExecutionStats_NoExecutions_ReturnsZeroes()
    {
        var definition = await InsertDefinitionAsync("""[ { "stepId": "a", "configuration": { "nodeType": "agent" } } ]""");

        var stats = (await CreateService().GetExecutionStatsAsync(definition.Id)).Data!;

        stats.TotalExecutions.ShouldBe(0);
        stats.SuccessRate.ShouldBe(0d);
        stats.P95DurationMs.ShouldBeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupRunLookup(AgentRun agentRun)
    {
        RunRepository
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<AgentRun, bool>> predicate, CancellationToken _) =>
                predicate.Compile()(agentRun) ? agentRun : null);
    }

    private async Task InsertExecutionAsync(Guid definitionId, WorkflowExecutionStatus status, long? durationMs, string completedSteps = "[]")
    {
        DbContext.Set<WorkflowExecution>().Add(new WorkflowExecution
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            WorkflowDefinitionId = definitionId,
            InitialInput = "start",
            Status = status,
            CompletedSteps = completedSteps,
            StepOutputs = """{"a":"a long model output"}""",
            StartedAt = DateTime.UtcNow,
            DurationMs = durationMs
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private sealed class EchoAgentNode : IWorkflowNode
    {
        public string NodeType => WorkflowNodeTypes.Agent;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult { Output = $"{context.Step.StepId}-out", IsSuccess = true });
    }

    private sealed class AskNode : IWorkflowNode
    {
        public string NodeType => "ask";

        public Task<WorkflowInterrupt?> CheckInterruptAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowInterrupt?>(new WorkflowInterrupt
            {
                StepId = context.Step.StepId!,
                Reason = "Need an answer",
                Type = InterruptType.HumanInput
            });

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult { Output = "answered", IsSuccess = true });
    }
}
