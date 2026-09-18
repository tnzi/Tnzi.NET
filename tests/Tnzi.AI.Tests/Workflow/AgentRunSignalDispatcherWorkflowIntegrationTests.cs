namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Workflow tables plus the AI run tables the engine writes when it tracks a run
/// (<c>AgentRun</c> carries FKs to <c>Agent</c> and <c>AgentThread</c>, and <c>Agent</c> to
/// <c>Provider</c>, so those are mapped too).
/// </summary>
public class WorkflowRunSignalDbContext : TnziDbContext<WorkflowRunSignalDbContext>
{
    public WorkflowRunSignalDbContext(
        DbContextOptions<WorkflowRunSignalDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionVersionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunNodeConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// <c>send_agent_input</c> against a workflow run paused at its <b>first</b> HumanInput
/// interrupt, through the real service, the real engine, the real checkpoint store and the
/// real <see cref="RunStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// ★ The 2026-09-12 fix made <see cref="AgentRunSignalDispatcher"/> refuse input with 409
/// unless <c>AgentRun.Status == RequiresClarification</c>. But the engine persisted every
/// interrupt, approval or not, as <c>AwaitingApproval</c>, and nothing else touched the row
/// until a resume: the state endpoint (which reads the execution) said
/// <c>canSendInput = true</c> while the send-input endpoint answered 409 for the same run.
/// The documented RequiresClarification → ResumeWithInputAsync path was unreachable for
/// exactly the population it exists for.
/// </para>
/// <para>
/// Two guarantees are pinned here: the engine writes <c>RequiresClarification</c> for a
/// non-approval interrupt, and the dispatcher decides on the execution's real state, so a
/// row persisted before this fix (still <c>AwaitingApproval</c>) is not refused either.
/// </para>
/// </remarks>
public class AgentRunSignalDispatcherWorkflowIntegrationTests : IntegratedTestBase<WorkflowRunSignalDbContext>
{
    private readonly NodeRecorder _recorder = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<WorkflowDefinition, Guid>, EFCoreRepository<WorkflowRunSignalDbContext, WorkflowDefinition, Guid>>();
        services.AddScoped<IRepository<WorkflowDefinitionVersion, Guid>, EFCoreRepository<WorkflowRunSignalDbContext, WorkflowDefinitionVersion, Guid>>();
        services.AddScoped<IRepository<WorkflowExecution, Guid>, EFCoreRepository<WorkflowRunSignalDbContext, WorkflowExecution, Guid>>();
        services.AddScoped<IRepository<AgentRun, Guid>, EFCoreRepository<WorkflowRunSignalDbContext, AgentRun, Guid>>();
        services.AddScoped<IRepository<AgentRunNode, Guid>, EFCoreRepository<WorkflowRunSignalDbContext, AgentRunNode, Guid>>();

        services.AddScoped(_ => Mock.Of<IRepository<Agent, Guid>>());
        services.AddScoped(_ => Mock.Of<IUsageLogService>());
        services.AddScoped(_ => Mock.Of<IQuotaService>());

        services.AddScoped<IRunStore, RunStore>();
        services.AddScoped<IWorkflowCheckpointStore, DatabaseWorkflowCheckpointStore>();
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowService>();

        services.AddSingleton(_recorder);
        services.AddScoped<IWorkflowNode, EchoAgentNode>();
        services.AddScoped<IWorkflowNode, HumanInputNode>();
    }

    [Fact]
    public async Task FirstHumanInputInterrupt_PersistsRequiresClarification_OnTheRunRow()
    {
        var (_, runId) = await RunUntilFirstInterruptAsync();

        var stored = await LoadRunAsync(runId);
        stored.Status.ShouldBe(AgentRunStatus.RequiresClarification,
            "a HumanInput interrupt is not an approval; AwaitingApproval on the row is what made send_agent_input answer 409");
    }

    [Fact]
    public async Task DispatchInput_AtFirstHumanInputInterrupt_ReachesTheNode()
    {
        var (executionId, runId) = await RunUntilFirstInterruptAsync();

        var result = await CreateDispatcher().DispatchInputAsync(runId, new SendAgentRunInput
        {
            WorkflowInput = new Dictionary<string, object> { ["answer"] = "42" }
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        _recorder.ResumeData.ShouldContainKey("x");
        _recorder.ResumeData["x"]["answer"].ToString().ShouldBe("42");
        _recorder.DependencyOutputs["b"]["x"].ShouldBe("input:42");

        var done = await DbContext.Set<WorkflowExecution>().AsNoTracking().SingleAsync(e => e.ExecutionId == executionId);
        done.Status.ShouldBe(WorkflowExecutionStatus.Completed);
        (await LoadRunAsync(runId)).Status.ShouldBe(AgentRunStatus.Completed);
    }

    /// <summary>
    /// Rows written before this fix still say <c>AwaitingApproval</c> while the execution
    /// is <c>AwaitingInput</c>; the dispatcher must ask the execution, not the stale row.
    /// </summary>
    [Fact]
    public async Task DispatchInput_RunRowStillSaysAwaitingApproval_IsRoutedByTheExecutionState()
    {
        var (_, runId) = await RunUntilFirstInterruptAsync();
        await DbContext.Set<AgentRun>()
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, AgentRunStatus.AwaitingApproval));
        DbContext.ChangeTracker.Clear();

        var result = await CreateDispatcher().DispatchInputAsync(runId, new SendAgentRunInput
        {
            WorkflowInput = new Dictionary<string, object> { ["answer"] = "legacy" }
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        _recorder.ResumeData["x"]["answer"].ToString().ShouldBe("legacy");
    }

    [Fact]
    public async Task DispatchInput_AfterTheRunCompleted_IsRefusedWith409()
    {
        var (_, runId) = await RunUntilFirstInterruptAsync();
        var dispatcher = CreateDispatcher();
        (await dispatcher.DispatchInputAsync(runId, new SendAgentRunInput
        {
            WorkflowInput = new Dictionary<string, object> { ["answer"] = "42" }
        })).Succeeded.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();

        var again = await dispatcher.DispatchInputAsync(runId, new SendAgentRunInput
        {
            WorkflowInput = new Dictionary<string, object> { ["answer"] = "late" }
        });

        again.Succeeded.ShouldBeFalse();
        again.Code.ShouldBe(409);
        again.ErrorCode.ShouldBe(ErrorCodes.RunInvalidState);
        _recorder.ResumeData["x"]["answer"].ToString().ShouldBe("42", "a completed run must not be re-entered");
    }

    // -----------------------------------------------------------------------

    private async Task<(string ExecutionId, Guid RunId)> RunUntilFirstInterruptAsync()
    {
        var definition = new WorkflowDefinition
        {
            Name = "wf",
            ExecutionMode = WorkflowExecutionMode.Dag,
            IsEnabled = true,
            Steps = """
                [
                  { "stepId": "a", "configuration": { "nodeType": "agent" } },
                  { "stepId": "x", "dependsOn": ["a"], "configuration": { "nodeType": "human_input" } },
                  { "stepId": "b", "dependsOn": ["x"], "configuration": { "nodeType": "agent" } }
                ]
                """
        };
        DbContext.Set<WorkflowDefinition>().Add(definition);
        await DbContext.SaveChangesAsync();

        var run = await ServiceProvider.GetRequiredService<WorkflowService>().RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Status.ShouldBe("AwaitingInput");
        run.Data.RunId.ShouldNotBeNull("the engine tracks a run whenever a checkpoint store is present");
        DbContext.ChangeTracker.Clear();
        return (run.Data.ExecutionId!, run.Data.RunId!.Value);
    }

    private AgentRunSignalDispatcher CreateDispatcher()
    {
        var workflow = ServiceProvider.GetRequiredService<WorkflowService>();
        return new AgentRunSignalDispatcher(
            ServiceProvider.GetRequiredService<IRunStore>(),
            Mock.Of<IAgentRunService>(),
            workflow,
            workflow);
    }

    private Task<AgentRun> LoadRunAsync(Guid runId)
    {
        DbContext.ChangeTracker.Clear();
        return DbContext.Set<AgentRun>().AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    private sealed class NodeRecorder
    {
        public Dictionary<string, Dictionary<string, object>> ResumeData { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> DependencyOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Record(WorkflowNodeContext context)
        {
            var stepId = context.Step.StepId!;
            lock (ResumeData)
            {
                DependencyOutputs[stepId] = context.DependencyOutputs.ToDictionary(kv => kv.Key, kv => kv.Value.Text, StringComparer.OrdinalIgnoreCase);
                if (context.ResumeData != null)
                {
                    ResumeData[stepId] = context.ResumeData;
                }
            }
        }
    }

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
