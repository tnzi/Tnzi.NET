using Microsoft.Data.Sqlite;
using Tnzi.AI.Workflow.Options;
using Tnzi.EFCore.Data;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// The watchdog runs from a background scope that has no current tenant. With multi-tenancy
/// enabled the global filter on <see cref="WorkflowExecution"/> (an <c>IMultiTenant</c> entity)
/// becomes <c>TenantId IS NULL</c> in that scope, so every tenant's stale execution was invisible
/// to the scan and stayed <c>Running</c> forever while the log reported a healthy "0/0".
/// </summary>
/// <remarks>
/// <see cref="WorkflowWatchdogTests"/> use a Moq repository, so the query filter never runs
/// there. This fixture uses a real SQLite database, a real <see cref="DataFilterManager"/>
/// and a DbContext with <c>MultiTenancy:Enabled=true</c> and no current tenant, which is
/// exactly what <c>WorkflowWatchdogHostedService.RunScanAsync</c> hands the service.
/// </remarks>
public sealed class WorkflowWatchdogMultiTenantTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public WorkflowWatchdogMultiTenantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var seed = CreateContext(dataFilterManager: null);
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task ScanAsync_ReapsStaleExecutionsOfEveryTenant_FromATenantlessScope()
    {
        var stale = DateTime.UtcNow.AddHours(-2);
        await SeedAsync(
            Execution("tenant-a", WorkflowExecutionStatus.Running, stale, _tenantA),
            Execution("tenant-b", WorkflowExecutionStatus.AwaitingApproval, DateTime.UtcNow.AddDays(-10), _tenantB),
            Execution("host", WorkflowExecutionStatus.Running, stale, tenantId: null),
            Execution("tenant-a-fresh", WorkflowExecutionStatus.Running, DateTime.UtcNow, _tenantA));

        using var scope = CreateScopeFactory().CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WorkflowWatchdogService>();

        var marked = await service.ScanAsync();

        marked.ShouldBe(3, "the tenant rows must be reaped too, not only the host (TenantId IS NULL) row");

        var rows = await LoadAllAsync();
        rows["tenant-a"].Status.ShouldBe(WorkflowExecutionStatus.Failed);
        rows["tenant-a"].CurrentWaitReason.ShouldBe("timed_out");
        rows["tenant-a"].TenantId.ShouldBe(_tenantA, "reaping must not rewrite the row's tenant");
        rows["tenant-b"].Status.ShouldBe(WorkflowExecutionStatus.Failed);
        rows["tenant-b"].TenantId.ShouldBe(_tenantB);
        rows["host"].Status.ShouldBe(WorkflowExecutionStatus.Failed);
        rows["tenant-a-fresh"].Status.ShouldBe(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public async Task ScanAsync_DoesNotLeaveTheTenantFilterDisabledForTheScope()
    {
        await SeedAsync(Execution("tenant-a", WorkflowExecutionStatus.Running, DateTime.UtcNow.AddHours(-2), _tenantA));

        using var scope = CreateScopeFactory().CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WorkflowWatchdogService>();
        var filterManager = scope.ServiceProvider.GetRequiredService<IDataFilterManager>();

        await service.ScanAsync();

        filterManager.IsEnabled<IMultiTenantFilter>().ShouldBeTrue(
            "the cross-tenant read is a scoped exception, not a change to the scope's filter state");
    }

    // -----------------------------------------------------------------------
    // Fixture plumbing
    // -----------------------------------------------------------------------

    private static WorkflowExecution Execution(string executionId, WorkflowExecutionStatus status, DateTime updatedAt, Guid? tenantId) => new()
    {
        ExecutionId = executionId,
        Status = status,
        UpdatedTime = updatedAt,
        TenantId = tenantId
    };

    private async Task SeedAsync(params WorkflowExecution[] rows)
    {
        // Seed through a tenant-less context: the audit fill does not overwrite an explicitly
        // assigned TenantId when there is no current tenant.
        using var seed = CreateContext(dataFilterManager: null);
        seed.Set<WorkflowExecution>().AddRange(rows);
        await seed.SaveChangesAsync();
    }

    private async Task<Dictionary<string, WorkflowExecution>> LoadAllAsync()
    {
        var filterManager = new DataFilterManager();
        using var ctx = CreateContext(filterManager);
        using (filterManager.Disable<IMultiTenantFilter>())
        {
            return await ctx.Set<WorkflowExecution>().AsNoTracking()
                .ToDictionaryAsync(e => e.ExecutionId, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A scope shaped like the one <c>WorkflowWatchdogHostedService</c> creates: no current
    /// tenant, multi-tenancy on, real repository and real <see cref="DataFilterManager"/>.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_connection);
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddSingleton<ICurrentTenant>(new StubCurrentTenant(null));
        services.AddScoped(sp => CreateContext(sp.GetService<IDataFilterManager>()));
        services.AddScoped<IRepository<WorkflowExecution, Guid>>(sp =>
            new EFCoreRepository<WatchdogMtDbContext, WorkflowExecution, Guid>(
                sp.GetRequiredService<WatchdogMtDbContext>()));
        services.AddSingleton<IOptionsSnapshot<WorkflowWatchdogOptions>>(
            new StaticOptionsMonitor<WorkflowWatchdogOptions>(new WorkflowWatchdogOptions()));
        services.AddSingleton<ILogger<WorkflowWatchdogService>>(NullLogger<WorkflowWatchdogService>.Instance);
        services.AddScoped<WorkflowWatchdogService>();

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private WatchdogMtDbContext CreateContext(IDataFilterManager? dataFilterManager)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<WatchdogMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new WatchdogMtDbContext(
            options,
            currentUser.Object,
            new StubCurrentTenant(null),
            dataFilterManager,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}

/// <summary>
/// Test DbContext exposing only <see cref="WorkflowExecution"/> (IMultiTenant) with multi-tenancy on.
/// </summary>
internal sealed class WatchdogMtDbContext : TnziDbContext<WatchdogMtDbContext>
{
    public WatchdogMtDbContext(
        DbContextOptions<WatchdogMtDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        IDataFilterManager? dataFilterManager,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, dataFilterManager, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowExecutionConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
