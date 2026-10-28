namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Every workflow segment that runs LLM nodes is reserved, logged and settled for what it
/// actually consumed: interrupted runs and streams included, and resumed segments too.
/// </summary>
/// <remarks>
/// Runs through the real <see cref="WorkflowService"/>, engine and database checkpoint store;
/// only the usage-limit and usage-log collaborators are mocks (and the cost calculator prices a token at
/// one dollar, so the settled amount reads back as the token count).
/// </remarks>
public class WorkflowServiceUsageAccountingTests : WorkflowServiceSqliteTestBase
{
    private const string MeteredType = "metered-test";
    private const string AbortType = "abort-test";
    private const string AskType = "ask-test";

    private readonly AbortSwitch _abort = new();
    private readonly List<LoggedUsage> _logged = [];
    private readonly List<decimal> _settled = [];
    private readonly List<Guid> _reservedFor = [];

    private Mock<IUsageLogService> UsageLog { get; } = new();
    private Mock<IUsageLimitService> UsageLimits { get; } = new();

    private static readonly Guid Caller = Guid.NewGuid();

    public WorkflowServiceUsageAccountingTests()
    {
        UsageLog.Setup(u => u.LogUsageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((string op, string _, string _, int input, int output, long _, bool success, string? _, Guid? _, Guid? _, CancellationToken _)
                => _logged.Add(new LoggedUsage(op, input, output, success)))
            .Returns(Task.CompletedTask);

        UsageLimits.Setup(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback((Guid userId, CancellationToken _) => _reservedFor.Add(userId))
            .ReturnsAsync((Guid userId, CancellationToken _) => Result<UsageLimitReservation>.Success(new UsageLimitReservation(userId, DateTime.UtcNow)));
        UsageLimits.Setup(q => q.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback((UsageLimitReservation _, decimal cost, CancellationToken _) => _settled.Add(cost))
            .ReturnsAsync(Result.Success());
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped(_ => UsageLog.Object);
        services.AddScoped(_ => UsageLimits.Object);
        services.AddSingleton<ICostCalculator>(new DollarPerTokenCalculator());
    }

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton(_abort);
        services.AddScoped<IWorkflowNode, MeteredNode>();
        services.AddScoped<IWorkflowNode, AbortNode>();
        services.AddScoped<IWorkflowNode, AskNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
    }

    private const string TwoNodesThenAbort = """
        [
          { "stepId": "a", "configuration": { "nodeType": "metered-test" } },
          { "stepId": "b", "dependsOn": ["a"], "configuration": { "nodeType": "metered-test" } },
          { "stepId": "c", "dependsOn": ["b"], "configuration": { "nodeType": "abort-test" } }
        ]
        """;

    // -----------------------------------------------------------------------
    // Interrupted runs are billed for what the finished nodes consumed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_RequestAbortedAfterTwoNodes_BillsTheTokensAlreadyConsumed()
    {
        var definition = await InsertDefinitionAsync(TwoNodesThenAbort);

        var result = await CreateService().RunAsync(definition.Id, "start", Caller, _abort.Token);

        result.Succeeded.ShouldBeFalse();
        _logged.ShouldHaveSingleItem();
        _logged[0].ShouldBe(new LoggedUsage(AIOperationType.WorkflowRun, 200, 100, false));
        _settled.ShouldBe([300m], "nodes a and b were billed by the provider; settling 0 refunded them");
    }

    [Fact]
    public async Task RunStreamingAsync_RequestAbortedAfterTwoNodes_BillsTheTokensAlreadyConsumed()
    {
        var definition = await InsertDefinitionAsync(TwoNodesThenAbort);

        await Should.ThrowAsync<Exception>(async () =>
        {
            await foreach (var _ in CreateService().RunStreamingAsync(definition.Id, "start", Caller, _abort.Token)) { }
        });

        _logged.ShouldHaveSingleItem();
        _logged[0].ShouldBe(new LoggedUsage(AIOperationType.WorkflowRunStreaming, 200, 100, false));
        _settled.ShouldBe([300m]);
    }

    [Fact]
    public async Task RunStreamingAsync_ConsumerLeavesAfterTheFirstEvent_BillsInFull_AndTheRunStaysCompleted()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "nodeType": "metered-test" } },
              { "stepId": "b", "dependsOn": ["a"], "configuration": { "nodeType": "metered-test" } }
            ]
            """);

        string? executionId = null;
        await foreach (var evt in CreateService().RunStreamingAsync(definition.Id, "start", Caller))
        {
            executionId = evt.ExecutionId;
            break;
        }

        _settled.ShouldBe([300m]);
        _logged.ShouldHaveSingleItem();
        _logged[0].Success.ShouldBeTrue("the engine finished; only the delivery of its events was cut short");
        (await LoadExecutionAsync(executionId!))!.Status.ShouldBe(WorkflowExecutionStatus.Completed);
    }

    // -----------------------------------------------------------------------
    // Resumed segments are reserved, logged and settled
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResumeAsync_AfterApproval_ReservesLogsAndSettlesTheResumedSegment()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "metered-test" } },
              { "stepId": "gate", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["gate"], "configuration": { "nodeType": "metered-test" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start", Caller);
        run.Data!.Status.ShouldBe("AwaitingApproval");
        (await service.ApproveStepAsync(run.Data.ExecutionId!, "gate")).Succeeded.ShouldBeTrue();
        ClearAccounting();

        var resumed = await service.ResumeAsync(run.Data.ExecutionId!);

        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");
        _reservedFor.ShouldBe([TestHelper.DefaultTestUserId], "the resume is booked to the user who resumes it");
        _logged.ShouldBe([new LoggedUsage(AIOperationType.WorkflowRun, 100, 50, true)]);
        _settled.ShouldBe([150m]);
    }

    [Fact]
    public async Task ResumeWithInputAsync_ReservesLogsAndSettlesTheResumedSegment()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "nodeType": "metered-test" } },
              { "stepId": "ask", "dependsOn": ["a"], "configuration": { "nodeType": "ask-test" } },
              { "stepId": "b", "dependsOn": ["ask"], "configuration": { "nodeType": "metered-test" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start", Caller);
        run.Data!.Status.ShouldBe("AwaitingInput");
        ClearAccounting();

        var resumed = await service.ResumeWithInputAsync(run.Data.ExecutionId!, "ask", new Dictionary<string, object> { ["answer"] = "42" });

        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        resumed.Data!.Status.ShouldBe("Completed");
        _reservedFor.ShouldBe([TestHelper.DefaultTestUserId]);
        // The answered node (100/50) and the node after it (100/50).
        _logged.ShouldBe([new LoggedUsage(AIOperationType.WorkflowRun, 200, 100, true)]);
        _settled.ShouldBe([300m]);
    }

    [Fact]
    public async Task ResumeAsync_AbortedPartWay_BillsWhatRan_AndLeavesTheExecutionResumable()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "draft", "configuration": { "nodeType": "metered-test" } },
              { "stepId": "gate", "dependsOn": ["draft"], "configuration": { "nodeType": "approval" } },
              { "stepId": "publish", "dependsOn": ["gate"], "configuration": { "nodeType": "metered-test" } },
              { "stepId": "notify", "dependsOn": ["publish"], "configuration": { "nodeType": "abort-test" } }
            ]
            """);
        var service = CreateService();

        var run = await service.RunAsync(definition.Id, "start", Caller);
        var executionId = run.Data!.ExecutionId!;
        (await service.ApproveStepAsync(executionId, "gate")).Succeeded.ShouldBeTrue();
        ClearAccounting();

        var resumed = await service.ResumeAsync(executionId, _abort.Token);

        resumed.Succeeded.ShouldBeFalse();
        _settled.ShouldBe([150m]);
        (await LoadExecutionAsync(executionId))!.Status.ShouldBe(WorkflowExecutionStatus.Failed,
            "Failed is resumable; a row left Running waits for the watchdog");
    }

    [Fact]
    public async Task ResumeAsync_LosesTheClaimToAConcurrentResume_RecordsNoCost()
    {
        var definition = await InsertDefinitionAsync("""
            [ { "stepId": "a", "configuration": { "nodeType": "metered-test" } } ]
            """);
        const string executionId = "claim-lost";

        var executions = new Mock<IRepository<WorkflowExecution, Guid>>();
        executions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecution { ExecutionId = executionId, WorkflowDefinitionId = definition.Id, InitialInput = "start", Status = WorkflowExecutionStatus.Paused });
        // A concurrent resume already bumped the concurrency stamp.
        executions.Setup(r => r.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("stale concurrency token"));

        var checkpoints = new Mock<IWorkflowCheckpointStore>();
        checkpoints.Setup(s => s.GetCheckpointAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowCheckpoint { ExecutionId = executionId, InitialInput = "start", Status = WorkflowExecutionStatus.Paused });

        var service = new WorkflowService(
            ServiceProvider.GetRequiredService<IRepository<WorkflowDefinition, Guid>>(),
            executions.Object,
            ServiceProvider.GetRequiredService<IRepository<WorkflowDefinitionVersion, Guid>>(),
            RunRepository.Object,
            AgentRepository.Object,
            UsageLog.Object,
            UsageLimits.Object,
            checkpoints.Object,
            ServiceProvider.GetRequiredService<WorkflowEngine>(),
            ServiceProvider.GetServices<IWorkflowNode>(),
            ServiceProvider);

        var result = await service.ResumeAsync(executionId);

        result.Code.ShouldBe(409);
        _reservedFor.ShouldBe([TestHelper.DefaultTestUserId]);
        _settled.ShouldBeEmpty("the engine never ran, so there is no cost to record");
        _logged.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------

    private void ClearAccounting()
    {
        _logged.Clear();
        _settled.Clear();
        _reservedFor.Clear();
    }

    private sealed record LoggedUsage(string OperationType, int InputTokens, int OutputTokens, bool Success);

    /// <summary>Prices every token at one dollar, so a settled amount reads back as the token count.</summary>
    private sealed class DollarPerTokenCalculator : ICostCalculator
    {
        public decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens) => inputTokens + outputTokens;

        public decimal? CalculateCost(string provider, string model, long inputTokens, long outputTokens, long cachedInputTokens, long cacheWriteTokens)
            => inputTokens + outputTokens;
    }

    /// <summary>The request token, and the node that cancels it as if the client had disconnected.</summary>
    private sealed class AbortSwitch
    {
        private readonly CancellationTokenSource _cts = new();
        public CancellationToken Token => _cts.Token;
        public Task TripAsync() => _cts.CancelAsync();
    }

    private static TokenUsageDto Usage() => new() { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 };

    private sealed class MeteredNode : IWorkflowNode
    {
        public string NodeType => MeteredType;

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult { Output = $"{context.Step.StepId}-out", IsSuccess = true, Usage = Usage() });
    }

    private sealed class AbortNode(AbortSwitch abort) : IWorkflowNode
    {
        public string NodeType => AbortType;

        public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            await abort.TripAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new WorkflowNodeResult { Output = "never", IsSuccess = true };
        }
    }

    /// <summary>Asks for human input, then spends tokens on the answer once resumed.</summary>
    private sealed class AskNode : IWorkflowNode
    {
        public string NodeType => AskType;

        public Task<WorkflowInterrupt?> CheckInterruptAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowInterrupt?>(new WorkflowInterrupt
            {
                StepId = context.Step.StepId!,
                Reason = "Need an answer",
                Type = InterruptType.HumanInput,
                RequestedInput = new Dictionary<string, object> { ["answer"] = "string" }
            });

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowNodeResult { Output = "answered", IsSuccess = true, Usage = Usage() });
    }
}
