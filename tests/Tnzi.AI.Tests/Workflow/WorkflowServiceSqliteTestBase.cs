namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Test DbContext exposing the three workflow tables (definition, version snapshot,
/// execution) so <see cref="WorkflowService"/> can be exercised end-to-end against a
/// real SQLite database and the real <see cref="DatabaseWorkflowCheckpointStore"/>.
/// </summary>
public class WorkflowServiceDbContext : TnziDbContext<WorkflowServiceDbContext>
{
    public WorkflowServiceDbContext(
        DbContextOptions<WorkflowServiceDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionVersionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowExecutionConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// Base fixture for <see cref="WorkflowService"/> tests that must run the real service,
/// the real <see cref="WorkflowEngine"/> and the real database-backed checkpoint store on
/// SQLite. Only the AI-side collaborators the workflow paths never persist through
/// (agent runs, agents, usage log, quota) are mocked.
/// </summary>
/// <remarks>
/// The Moq-backed fixtures in <see cref="WorkflowServiceExecutionTests"/> cannot see the
/// defects this fixture is for: the version double-encoding is invisible until a real
/// row is read back, and the checkpoint union-merge only exists in the database store.
/// </remarks>
public abstract class WorkflowServiceSqliteTestBase : IntegratedTestBase<WorkflowServiceDbContext>
{
    protected Mock<IRepository<AgentRun, Guid>> RunRepository { get; } = new();
    protected Mock<IRepository<Agent, Guid>> AgentRepository { get; } = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<WorkflowDefinition, Guid>,
            EFCoreRepository<WorkflowServiceDbContext, WorkflowDefinition, Guid>>();
        services.AddScoped<IRepository<WorkflowDefinitionVersion, Guid>,
            EFCoreRepository<WorkflowServiceDbContext, WorkflowDefinitionVersion, Guid>>();
        services.AddScoped<IRepository<WorkflowExecution, Guid>,
            EFCoreRepository<WorkflowServiceDbContext, WorkflowExecution, Guid>>();

        services.AddScoped(_ => RunRepository.Object);
        services.AddScoped(_ => AgentRepository.Object);
        services.AddScoped(_ => Mock.Of<IUsageLogService>());
        services.AddScoped(_ => Mock.Of<IQuotaService>());

        services.AddScoped<IWorkflowCheckpointStore, DatabaseWorkflowCheckpointStore>();
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowService>();

        ConfigureWorkflowNodes(services);
    }

    /// <summary>
    /// Register the <see cref="IWorkflowNode"/> implementations a test needs. The default
    /// registers nothing; a test that runs a graph must register at least an agent node.
    /// </summary>
    protected virtual void ConfigureWorkflowNodes(IServiceCollection services)
    {
    }

    protected WorkflowService CreateService() => ServiceProvider.GetRequiredService<WorkflowService>();

    protected async Task<WorkflowDefinition> InsertDefinitionAsync(
        string stepsJson,
        WorkflowExecutionMode mode = WorkflowExecutionMode.Dag,
        string name = "wf",
        string? description = null,
        string? configuration = null)
    {
        var definition = new WorkflowDefinition
        {
            Name = name,
            Description = description,
            ExecutionMode = mode,
            IsEnabled = true,
            Steps = stepsJson,
            Configuration = configuration
        };

        DbContext.Set<WorkflowDefinition>().Add(definition);
        await DbContext.SaveChangesAsync();
        return definition;
    }

    protected Task<WorkflowExecution?> LoadExecutionAsync(string executionId)
        => DbContext.Set<WorkflowExecution>().AsNoTracking().FirstOrDefaultAsync(e => e.ExecutionId == executionId);
}
