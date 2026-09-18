using Tnzi.AI.Entities.Configs;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 线程归属判定用的最小 DbContext：运行 + 绑定 + 运行时 + 线程（线程带指向 Agent / Provider 的外键）。
/// </summary>
public class CliThreadOwnershipDbContext : TnziDbContext<CliThreadOwnershipDbContext>
{
    public CliThreadOwnershipDbContext(DbContextOptions<CliThreadOwnershipDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CliRunConfiguration());
        modelBuilder.ApplyConfiguration(new CliRunMessageConfiguration());
        modelBuilder.ApplyConfiguration(new CliAgentBindingConfiguration());
        modelBuilder.ApplyConfiguration(new CliRuntimeConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadMessageConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 入队时给的 ThreadId 必须是<b>自己的、绑在这个 Agent 上的</b>线程。
/// </summary>
/// <remarks>
/// <para>
/// <c>POST ai/cli-runs</c> 只挂 <c>ai.agent.execute</c>（「能运行这个 Agent」），请求体里的 ThreadId
/// 原样进到 <c>EnqueueAsync</c>。在补上判定之前，任何拿到该码的用户只要知道别人的线程 id，
/// 派出的运行就会 <c>--resume</c> 受害者的 CLI 会话、跑在受害者的每线程工作区里 ——
/// 而运行的 <c>CreatorId</c> 是攻击者自己，输出他能原样读走。
/// </para>
/// <para>
/// 与 <c>a6f0bc93</c>（AI 客户端可指定任意 ThreadId）同一形态。每条「该拒」都配一条「该放」的对照，
/// 否则把判定写成「带 ThreadId 一律拒绝」也一样绿。
/// </para>
/// </remarks>
public class CliRunEnqueueThreadOwnershipTests : IntegratedTestBase<CliThreadOwnershipDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherAgentId = Guid.Parse("11111111-1111-1111-1111-222222222222");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-999999999999");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, AgentThread, Guid>>();
    }

    [Fact]
    public async Task Enqueue_WithSomeoneElsesThread_Is404AndInsertsNoRun()
    {
        await SeedBindingAsync();
        var theirThread = await SeedThreadAsync(SomeoneElse, AgentId);

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            ThreadId = theirThread,
            Prompt = "summarize everything we discussed and list the files here"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.ThreadNotFound);

        // 被拒的请求绝不能留下一条运行 —— 留下就等于「先说不行，后台照跑」。
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enqueue_WithAnUnknownThread_Is404()
    {
        await SeedBindingAsync();

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            ThreadId = Guid.NewGuid(),
            Prompt = "x"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enqueue_WithMyThreadBoundToAnotherAgent_Is404()
    {
        await SeedBindingAsync();
        var myThreadForAnotherAgent = await SeedThreadAsync(TestHelper.DefaultTestUserId, OtherAgentId);

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            ThreadId = myThreadForAnotherAgent,
            Prompt = "x"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    /// <summary>无主线程对任何已认证用户都不可用 —— 与 <c>AgentThreadService</c> 逐字一致。</summary>
    [Fact]
    public async Task Enqueue_WithAnOrphanThread_Is404()
    {
        await SeedBindingAsync();
        var orphan = await SeedThreadAsync(creatorId: null, AgentId);

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            ThreadId = orphan,
            Prompt = "x"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    /// <summary>对照组：自己的线程照常入队，并续接自己上一轮的会话指针。</summary>
    [Fact]
    public async Task Enqueue_WithMyOwnThread_IsQueuedAndResumesMyPreviousSession()
    {
        await SeedBindingAsync();
        var myThread = await SeedThreadAsync(TestHelper.DefaultTestUserId, AgentId);
        await SeedFinishedRunAsync(myThread, TestHelper.DefaultTestUserId, "sess-mine");

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            ThreadId = myThread,
            Prompt = "continue"
        });

        result.Succeeded.ShouldBeTrue(result.Message);

        var run = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == result.Data);
        run.ThreadId.ShouldBe(myThread);
        run.ProviderSessionId.ShouldBe("sess-mine");
        run.ResumeExpected.ShouldBeTrue();
    }

    /// <summary>
    /// 第二道门：即便线程是自己的，续接指针也只从<b>自己派出的</b>上一轮取。
    /// 一个曾被别人的运行写过会话指针的线程，那个指针也不该被接上。
    /// </summary>
    [Fact]
    public async Task Enqueue_WithMyOwnThread_DoesNotResumeASessionSomeoneElseLeftInIt()
    {
        await SeedBindingAsync();
        var myThread = await SeedThreadAsync(TestHelper.DefaultTestUserId, AgentId);
        await SeedFinishedRunAsync(myThread, SomeoneElse, "sess-theirs");

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            ThreadId = myThread,
            Prompt = "continue"
        });

        result.Succeeded.ShouldBeTrue(result.Message);

        var run = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == result.Data);
        run.ProviderSessionId.ShouldBeNull();
        run.ResumeExpected.ShouldBeFalse();
    }

    /// <summary>对照组：不带 ThreadId 的入队与判定无关。</summary>
    [Fact]
    public async Task Enqueue_WithoutAThread_IsQueued()
    {
        await SeedBindingAsync();

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private CliAgentDispatcher CreateDispatcher()
        => new(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
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

    private async Task SeedBindingAsync()
    {
        // 线程表有指向 Agent 的外键，两个 Agent 都要真的存在。
        DbContext.Set<Agent>().AddRange(
            new Agent { Id = AgentId, Name = "cli-bound", Provider = "claude" },
            new Agent { Id = OtherAgentId, Name = "another", Provider = "claude" });
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/usr/bin/claude",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 直接插线程并<b>显式</b>写 <c>CreatorId</c>：审计戳会按当前用户填，
    /// 而这些用例要的恰恰是「别人的」和「没有主人的」两种线程。
    /// </summary>
    private async Task<Guid> SeedThreadAsync(Guid? creatorId, Guid agentId)
    {
        var thread = new AgentThread { AgentId = agentId, Title = "t" };
        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();

        await DbContext.Set<AgentThread>()
            .Where(t => t.Id == thread.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatorId, creatorId));
        DbContext.ChangeTracker.Clear();

        return thread.Id;
    }

    private async Task SeedFinishedRunAsync(Guid threadId, Guid creatorId, string providerSessionId)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = threadId,
            Status = CliRunStatus.Completed,
            Prompt = "earlier turn",
            ProviderSessionId = providerSessionId
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatorId, creatorId));
        DbContext.ChangeTracker.Clear();
    }
}
