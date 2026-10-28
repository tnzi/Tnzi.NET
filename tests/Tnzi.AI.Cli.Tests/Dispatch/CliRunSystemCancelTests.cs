using Tnzi.Data;
using Tnzi.EFCore.Data;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 系统取消：服务端自己的流程（访问权到期、账号停用）要停下一条运行，而那一刻没有「看得见这条运行的用户」。
/// </summary>
/// <remarks>
/// <para>
/// 用户取消（<see cref="CliAgentDispatcher.CancelAsync"/>）只为看得见运行的人取消，其余一律 404，这是对的，
/// 但它让后台任务停不掉任何运行：一张到期的临时通行证不能再操作任何设备，它派出的运行却一直跑到自己结束。
/// </para>
/// <para>
/// 每条「系统路径能停」都配一条「用户路径停不了」的对照：否则把系统路径写成「直接调 CancelAsync」也一样绿。
/// </para>
/// </remarks>
public class CliRunSystemCancelTests : IntegratedTestBase<CliVisibilityDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111c1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c2");
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-9999999999c9");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliVisibilityDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliVisibilityDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliVisibilityDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliVisibilityDbContext, CliRuntime, Guid>>();

        // 调用者没有管理端查看码：用户路径对别人的运行答 404，正是系统路径要绕开的那道判定。
        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        services.AddScoped(_ => permissionChecker.Object);
    }

    [Fact]
    public async Task ById_SomeoneElsesRunningRun_IsAskedToStop_WhereTheCallerPathIs404()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Running);

        // 对照：用户路径看不见它
        var callerResult = await CreateDispatcher().CancelAsync(runId);
        callerResult.Code.ShouldBe(404);
        (await ReloadRunAsync(runId)).CancelRequested.ShouldBeFalse();

        var result = await CreateDispatcher().CancelAsSystemAsync(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await ReloadRunAsync(runId);
        stored.CancelRequested.ShouldBeTrue();
        stored.Status.ShouldBe(CliRunStatus.Running, "the host executing it writes the terminal state");
    }

    [Fact]
    public async Task ById_QueuedRun_EndsCancelled_CarryingTheReason()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Queued);

        var result = await CreateDispatcher().CancelAsSystemAsync(runId, "  Temporary access ended.  ");

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await ReloadRunAsync(runId);
        stored.Status.ShouldBe(CliRunStatus.Cancelled);
        stored.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        stored.Error.ShouldBe("Temporary access ended.", "the reason is trimmed and recorded so the run reads as stopped by the system");
    }

    [Fact]
    public async Task ById_RunningRun_KeepsAnErrorItAlreadyHas()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Running, error: "the original cause");

        await CreateDispatcher().CancelAsSystemAsync(runId, "Temporary access ended.");

        (await ReloadRunAsync(runId)).Error.ShouldBe("the original cause");
    }

    [Fact]
    public async Task ById_BlankReason_IsNotRecorded()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Running);

        await CreateDispatcher().CancelAsSystemAsync(runId, "   ");

        var stored = await ReloadRunAsync(runId);
        stored.CancelRequested.ShouldBeTrue();
        stored.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ById_FinishedRun_Is409_AndUnchanged()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Completed);

        var result = await CreateDispatcher().CancelAsSystemAsync(runId, "late");

        result.Code.ShouldBe(409);
        result.ErrorCode.ShouldBe(ErrorCodes.CliRunInvalidState);
        var stored = await ReloadRunAsync(runId);
        stored.CancelRequested.ShouldBeFalse();
        stored.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ById_UnknownRun_Is404()
    {
        var result = await CreateDispatcher().CancelAsSystemAsync(Guid.NewGuid());

        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.CliRunNotFound);
    }

    [Fact]
    public async Task ByThread_StopsEveryActiveRunOfThatThread_AndNothingElse()
    {
        var thread = Guid.NewGuid();
        var otherThread = Guid.NewGuid();
        var queued = await SeedRunAsync(SomeoneElse, CliRunStatus.Queued, thread);
        var dispatched = await SeedRunAsync(SomeoneElse, CliRunStatus.Dispatched, thread);
        var running = await SeedRunAsync(creatorId: null, CliRunStatus.Running, thread);
        var completed = await SeedRunAsync(SomeoneElse, CliRunStatus.Completed, thread);
        var elsewhere = await SeedRunAsync(SomeoneElse, CliRunStatus.Running, otherThread);

        var result = await CreateDispatcher().CancelThreadRunsAsSystemAsync(thread, "Temporary access ended.");

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(3);

        (await ReloadRunAsync(queued)).Status.ShouldBe(CliRunStatus.Cancelled);
        (await ReloadRunAsync(dispatched)).CancelRequested.ShouldBeTrue();
        (await ReloadRunAsync(running)).CancelRequested.ShouldBeTrue("a run nobody owns is still a run on this thread");

        var finished = await ReloadRunAsync(completed);
        finished.Status.ShouldBe(CliRunStatus.Completed);
        finished.CancelRequested.ShouldBeFalse();
        finished.Error.ShouldBeNull();

        (await ReloadRunAsync(elsewhere)).CancelRequested.ShouldBeFalse("another thread's run must not be touched");
    }

    [Fact]
    public async Task ByThread_NothingRunning_ReportsZero()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(SomeoneElse, CliRunStatus.Completed, thread);

        var result = await CreateDispatcher().CancelThreadRunsAsSystemAsync(thread);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(0);
    }

    [Fact]
    public async Task NoOpDispatcher_Answers501()
    {
        var noOp = new NoOpCliAgentDispatcher();

        (await noOp.CancelAsSystemAsync(Guid.NewGuid())).Code.ShouldBe(501);
        (await noOp.CancelThreadRunsAsSystemAsync(Guid.NewGuid())).Code.ShouldBe(501);
    }

    /// <summary>没有覆写系统取消的自定义调度器答 501，而不是假装停下了什么。</summary>
    [Fact]
    public async Task ACustomDispatcherWithoutTheOverride_Answers501_NotSuccess()
    {
        ICliAgentDispatcher custom = new MinimalDispatcher();

        var byId = await custom.CancelAsSystemAsync(Guid.NewGuid());
        byId.Code.ShouldBe(501);
        byId.ErrorCode.ShouldBe(ErrorCodes.CliSystemCancelNotSupported);

        var byThread = await custom.CancelThreadRunsAsSystemAsync(Guid.NewGuid());
        byThread.Succeeded.ShouldBeFalse();
        byThread.Code.ShouldBe(501);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private CliAgentDispatcher CreateDispatcher()
        => new(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            ServiceProvider);

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>直接插行，<c>CreatorId</c> 在审计戳落笔之后显式覆写（见 <c>CliRunVisibilityTests</c>）。</summary>
    private async Task<Guid> SeedRunAsync(Guid? creatorId, CliRunStatus status, Guid? threadId = null, string? error = null)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = threadId,
            Status = status,
            Prompt = "work started by someone else",
            Error = error
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatorId, creatorId));
        DbContext.ChangeTracker.Clear();

        return run.Id;
    }

    private async Task<CliRun> ReloadRunAsync(Guid runId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<CliRun>().AsNoTracking().FirstAsync(r => r.Id == runId);
    }

    /// <summary>只实现必需成员的调度器：系统取消走接口的默认实现。</summary>
    private sealed class MinimalDispatcher : ICliAgentDispatcher
    {
        public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<CliAgentEvent> StreamAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IPagedList<CliRunDto>>> GetListAsync(CliRunQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

/// <summary>
/// 多租户开着时，系统取消从<b>没有租户的后台作用域</b>里也要停得下租户的运行。
/// </summary>
/// <remarks>
/// 多租户的全局过滤器是 <c>TenantId == 当前租户</c>（严格等值），后台任务的当前租户恒为 null。
/// 系统路径若沿用用户路径那条带过滤的查询，定位会答 404；更糟的是写入也带过滤：匹配 0 行，调用方却得到「已取消」。
/// </remarks>
public class CliRunSystemCancelMultiTenancyTests : IntegratedTestBase<CliMultiTenantDbContext>
{
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-3333333333c3");
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111c4");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c5");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRuntime, Guid>>();
    }

    /// <summary>前提本身要成立：租户的行对无租户作用域里的普通查询确实不可见，用户路径因此答 404。</summary>
    [Fact]
    public async Task Precondition_TheCallerPath_CannotSeeATenantRun_FromATenantlessScope()
    {
        DbContext.IsMultiTenancyEnabled.ShouldBeTrue();
        var runId = await SeedRunAsync(CliRunStatus.Running);

        using var scope = ServiceProvider.CreateScope();
        var result = await CreateDispatcher(scope.ServiceProvider).CancelAsync(runId);

        result.Code.ShouldBe(404, "if the caller path can see it, this class proves nothing about tenants");
    }

    [Fact]
    public async Task ById_TenantRunningRun_IsAskedToStop_FromATenantlessScope()
    {
        var runId = await SeedRunAsync(CliRunStatus.Running);

        using var scope = ServiceProvider.CreateScope();
        var result = await CreateDispatcher(scope.ServiceProvider).CancelAsSystemAsync(runId, "Temporary access ended.");

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await ReloadAsync(runId);
        stored.CancelRequested.ShouldBeTrue("the write must reach the tenant's row, not silently match nothing");
        stored.Error.ShouldBe("Temporary access ended.");
        stored.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public async Task ById_TenantQueuedRun_EndsCancelled_FromATenantlessScope()
    {
        var runId = await SeedRunAsync(CliRunStatus.Queued);

        using var scope = ServiceProvider.CreateScope();
        var result = await CreateDispatcher(scope.ServiceProvider).CancelAsSystemAsync(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReloadAsync(runId)).Status.ShouldBe(CliRunStatus.Cancelled);
    }

    [Fact]
    public async Task ByThread_TenantRuns_AreStopped_FromATenantlessScope()
    {
        var thread = Guid.NewGuid();
        var queued = await SeedRunAsync(CliRunStatus.Queued, thread);
        var running = await SeedRunAsync(CliRunStatus.Running, thread);

        using var scope = ServiceProvider.CreateScope();
        var result = await CreateDispatcher(scope.ServiceProvider).CancelThreadRunsAsSystemAsync(thread);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2);
        (await ReloadAsync(queued)).Status.ShouldBe(CliRunStatus.Cancelled);
        (await ReloadAsync(running)).CancelRequested.ShouldBeTrue();
    }

    private static CliAgentDispatcher CreateDispatcher(IServiceProvider scoped)
        => new(
            scoped.GetRequiredService<IRepository<CliRun, Guid>>(),
            scoped.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            scoped.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            scoped.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            scoped);

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>租户的行：TenantId 显式写上，种子上下文本身没有租户。</summary>
    private async Task<Guid> SeedRunAsync(CliRunStatus status, Guid? threadId = null)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = threadId,
            Status = status,
            Prompt = "tenant work",
            TenantId = TenantId
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run.Id;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<CliRun>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == runId);
    }
}
