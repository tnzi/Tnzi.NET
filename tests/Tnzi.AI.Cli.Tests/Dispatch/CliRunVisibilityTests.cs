namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 运行记录的可见性用的最小 DbContext（运行 + 事件两张表）。
/// </summary>
public class CliVisibilityDbContext : TnziDbContext<CliVisibilityDbContext>
{
    public CliVisibilityDbContext(DbContextOptions<CliVisibilityDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CliRunConfiguration());
        modelBuilder.ApplyConfiguration(new CliRunMessageConfiguration());
        modelBuilder.ApplyConfiguration(new CliAgentBindingConfiguration());
        modelBuilder.ApplyConfiguration(new CliRuntimeConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 一次外部执行只该被<b>派出它的人</b>和管理台看到。
/// </summary>
/// <remarks>
/// <para>
/// 用户端与管理端两个控制器调用的是<b>同一批</b>调度器方法
/// （<c>Get</c> / <c>Stream</c> / <c>GetMessages</c> / <c>Cancel</c> 逐个对应），而用户端只挂了
/// <c>ai.agent.execute</c>（「能运行这个 Agent」）—— 在补上判定之前，任何拿到该码的用户
/// 只要猜到（或从别处看到）一个 runId，就能读到别人的<b>提示词</b>、
/// 别人 agent 的<b>全部工具输入输出</b>，还能把别人正在跑的任务取消掉。
/// </para>
/// <para>
/// 这与框架此前修过的两处是同一形态：AI 客户端可指定任意 ThreadId（<c>a6f0bc93</c>）、
/// Storage 打包解包零授权（<c>e27148ee</c>）。判据沿 AI 模块 house 模式用 <c>CreatorId</c>。
/// </para>
/// <para>
/// 每条「不该看到」都配一条「该看到」的对照 —— 否则把判定写成「一律拒绝」也一样绿。
/// </para>
/// </remarks>
public class CliRunVisibilityTests : IntegratedTestBase<CliVisibilityDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private Mock<IPermissionChecker>? _permissionChecker;

    public CliRunVisibilityTests()
    {
        // DTO 投影与 ProjectTo 都要 Mapster；沿 Tnzi.AI.Tests 的既有做法用默认配置。
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliVisibilityDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliVisibilityDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliVisibilityDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliVisibilityDbContext, CliRuntime, Guid>>();

        // 默认不注册 IPermissionChecker = 普通用户（没有管理端查看码）。
        // 需要管理台视角的用例自己 GrantAdminView()。
        _permissionChecker = new Mock<IPermissionChecker>();
        _permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        services.AddScoped(_ => _permissionChecker.Object);
    }

    private void GrantAdminView()
        => _permissionChecker!.Setup(p => p.IsGrantedAsync(CliPermissions.CliRunView)).ReturnsAsync(true);

    // ── Get ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_MyOwnRun_IsVisible()
    {
        var runId = await SeedRunAsync(TestHelper.DefaultTestUserId);

        var result = await CreateDispatcher().GetAsync(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Id.ShouldBe(runId);
    }

    /// <summary>别人派出的运行按 <b>404</b> 出 —— 与「不存在」不可区分。</summary>
    [Fact]
    public async Task Get_SomeoneElsesRun_Is404()
    {
        var runId = await SeedRunAsync(SomeoneElse);

        var result = await CreateDispatcher().GetAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Data.ShouldBeNull();
    }

    [Fact]
    public async Task Get_SomeoneElsesRun_IsVisibleToTheAdminConsole()
    {
        var runId = await SeedRunAsync(SomeoneElse);
        GrantAdminView();

        var result = await CreateDispatcher().GetAsync(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>
    /// 后台派出的运行（<c>CreatorId</c> 为空）对<b>任何</b>已认证用户都不可见。
    /// </summary>
    /// <remarks>
    /// 与 <c>AgentThreadService</c> 对无主线程的处理逐字一致：无主 ≠ 公开。
    /// 少了这一条，「谁都不是它的主人」会被读成「谁都是」。
    /// </remarks>
    [Fact]
    public async Task Get_OrphanRun_IsNotVisibleToAnyUser()
    {
        var runId = await SeedRunAsync(creatorId: null);

        var result = await CreateDispatcher().GetAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Get_OrphanRun_IsStillVisibleToTheAdminConsole()
    {
        var runId = await SeedRunAsync(creatorId: null);
        GrantAdminView();

        (await CreateDispatcher().GetAsync(runId)).Succeeded.ShouldBeTrue();
    }

    // ── Messages ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 事件流里是 agent 的全部工具输入输出，泄漏面比一条状态记录大得多。
    /// </summary>
    [Fact]
    public async Task GetMessages_SomeoneElsesRun_Is404()
    {
        var runId = await SeedRunAsync(SomeoneElse);
        await SeedMessageAsync(runId);

        var result = await CreateDispatcher().GetMessagesAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetMessages_MyOwnRun_ReturnsTheEvents()
    {
        var runId = await SeedRunAsync(TestHelper.DefaultTestUserId);
        await SeedMessageAsync(runId);

        var result = await CreateDispatcher().GetMessagesAsync(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Count.ShouldBe(1);
    }

    // ── Stream ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 订阅只能 <c>yield break</c>（SSE 响应头已经写出去了），所以断言「一条都收不到」。
    /// </summary>
    [Fact]
    public async Task Stream_SomeoneElsesRun_YieldsNothing()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Completed);
        await SeedMessageAsync(runId);

        var events = new List<CliAgentEvent>();
        await foreach (var evt in CreateDispatcher().StreamAsync(runId))
        {
            events.Add(evt);
        }

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stream_MyOwnRun_YieldsTheEvents()
    {
        var runId = await SeedRunAsync(TestHelper.DefaultTestUserId, CliRunStatus.Completed);
        await SeedMessageAsync(runId);

        var events = new List<CliAgentEvent>();
        await foreach (var evt in CreateDispatcher().StreamAsync(runId))
        {
            events.Add(evt);
        }

        events.Count.ShouldBe(1);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 取消别人的运行不只是读泄漏 —— 它会毁掉别人已经跑了一段时间（且已经付过钱）的工作。
    /// </summary>
    [Fact]
    public async Task Cancel_SomeoneElsesRun_Is404_AndLeavesItRunning()
    {
        var runId = await SeedRunAsync(SomeoneElse, CliRunStatus.Dispatched);

        var result = await CreateDispatcher().CancelAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);

        // 拒绝路径零副作用
        var stored = await ReloadRunAsync(runId);
        stored.CancelRequested.ShouldBeFalse();
        stored.Status.ShouldBe(CliRunStatus.Dispatched);
    }

    [Fact]
    public async Task Cancel_MyOwnRun_Works()
    {
        var runId = await SeedRunAsync(TestHelper.DefaultTestUserId, CliRunStatus.Dispatched);

        var result = await CreateDispatcher().CancelAsync(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReloadRunAsync(runId)).CancelRequested.ShouldBeTrue();
    }

    // ── List ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 无管理码时列表只出自己的（含：无主的那条也不出）。
    /// </summary>
    [Fact]
    public async Task GetList_WithoutTheAdminCode_OnlyReturnsMyOwnRuns()
    {
        var mine = await SeedRunAsync(TestHelper.DefaultTestUserId);
        await SeedRunAsync(SomeoneElse);
        await SeedRunAsync(creatorId: null);

        var result = await CreateDispatcher().GetListAsync(new CliRunQueryDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(r => r.Id).ShouldBe([mine]);
    }

    [Fact]
    public async Task GetList_WithTheAdminCode_ReturnsEveryRun()
    {
        await SeedRunAsync(TestHelper.DefaultTestUserId);
        await SeedRunAsync(SomeoneElse);
        await SeedRunAsync(creatorId: null);
        GrantAdminView();

        var result = await CreateDispatcher().GetListAsync(new CliRunQueryDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Count.ShouldBe(3);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private CliAgentDispatcher CreateDispatcher()
        => new(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            ServiceProvider);

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true, PollInterval = TimeSpan.FromMilliseconds(10) });
        return monitor.Object;
    }

    /// <summary>
    /// 直接插行并<b>显式</b>写 <c>CreatorId</c>：审计戳会按当前用户填，
    /// 而这些用例要的恰恰是「别人的」和「没有主人的」两种行。
    /// </summary>
    private async Task<Guid> SeedRunAsync(Guid? creatorId, CliRunStatus status = CliRunStatus.Queued)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = status,
            Prompt = "the prompt only its author should see"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        // 审计字段由框架在 SaveChanges 里落笔，所以覆写要在那之后，且要绕过跟踪器
        // 免得下一次 SaveChanges 又把它改回来。
        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatorId, creatorId));
        DbContext.ChangeTracker.Clear();

        return run.Id;
    }

    private async Task SeedMessageAsync(Guid runId)
    {
        DbContext.Set<CliRunMessage>().Add(new CliRunMessage
        {
            RunId = runId,
            Sequence = 1,
            Type = CliAgentEventType.ToolResult,
            Content = "tool output that belongs to somebody else"
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CliRun> ReloadRunAsync(Guid runId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<CliRun>().AsNoTracking().FirstAsync(r => r.Id == runId);
    }
}
