using Tnzi.Data;
using Tnzi.EFCore.Data;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 多租户<b>开启</b>的最小 DbContext：与运行期一样通过完整构造函数拿到当前租户与过滤器管理器。
/// </summary>
public class CliMultiTenantDbContext : TnziDbContext<CliMultiTenantDbContext>
{
    public CliMultiTenantDbContext(
        DbContextOptions<CliMultiTenantDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IDataFilterManager dataFilterManager,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, dataFilterManager, multiTenancyOptions: multiTenancyOptions)
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
/// 多租户开启时，租户派出的运行必须能被<b>没有租户的后台作用域</b>认领、执行、校验。
/// </summary>
/// <remarks>
/// <para>
/// <c>CliRun</c> / <c>CliAgentBinding</c> / <c>CliRuntime</c> 都是 <c>MultiTenantAuditedEntity</c>，
/// 多租户一开，全局过滤器就是 <c>TenantId == 当前租户</c>（严格等值）。认领、续租、回收、执行器加载、
/// MCP 回写凭据校验全部跑在 <c>IServiceScopeFactory.CreateScope()</c> 出来的作用域里 ——
/// 没有 HTTP 请求，没有 <c>ICurrentTenant</c>，当前租户恒为 null。
/// 在补上这一条之前：租户用户入队得到 200 和一个 runId，行上带着 TenantId，然后**永远停在 Queued**；
/// 没有任何错误日志，SSE 一条事件都不发。
/// </para>
/// <para>
/// 既有测试从未开过多租户（没有 <c>UseTnziMultiTenancy(true)</c> 时 <c>MultiTenancySwitch</c> 解析为 false），所以整套套件对此视而不见。
/// 本组用例用真实的 <c>DataFilterManager</c> + 真实的 <c>CurrentTenant</c> + 真实的过滤器，
/// 调用<b>真实的</b>认领 / 回收 / 执行 / 校验代码。
/// </para>
/// </remarks>
public class CliRunClaimMultiTenancyTests : IntegratedTestBase<CliMultiTenantDbContext>
{
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected override void ConfigureServices(IServiceCollection services)
    {
        // 后台作用域的形状：真实的过滤器管理器 + 真实的 CurrentTenant（读 AsyncLocal 覆盖，
        // 没有覆盖时回落到当前用户的 TenantId —— 这里的用户没有租户，所以是 null）。
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRuntime, Guid>>();
    }

    /// <summary>前提本身要成立：这个上下文确实开着多租户，租户行对无租户作用域确实不可见。</summary>
    [Fact]
    public async Task Precondition_TenantOwnedRun_IsInvisibleToAPlainTenantlessQuery()
    {
        DbContext.IsMultiTenancyEnabled.ShouldBeTrue();
        var run = await SeedRunAsync(CliRunStatus.Queued);

        using var scope = ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        (await repository.AsQueryable().AnyAsync(r => r.Id == run.Id)).ShouldBeFalse(
            "the ordinary query is tenant-filtered; if this passes the whole test class proves nothing");
    }

    [Fact]
    public async Task TryClaim_TenantOwnedQueuedRun_IsClaimed()
    {
        var run = await SeedRunAsync(CliRunStatus.Queued);

        var claimed = await CreateProcessor().TryClaimAsync(CancellationToken.None);

        claimed.ShouldBe(run.Id);
        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Dispatched);
        stored.ClaimedByHostId.ShouldNotBeNullOrWhiteSpace();
        stored.TenantId.ShouldBe(TenantId, "claiming must not rewrite the row's tenant");
    }

    [Fact]
    public async Task ReclaimExpiredLeases_TenantOwnedExpiredRun_IsRequeued()
    {
        var run = await SeedRunAsync(CliRunStatus.Running, leaseExpiresAt: DateTime.UtcNow.AddMinutes(-5));

        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);

        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Queued);
        stored.LeaseExpiresAt.ShouldBeNull();
    }

    /// <summary>
    /// 租约到期回收是 Queued 的另一个写入者，与停机打回队列同形：用户取消了一条正在跑的运行，
    /// 宿主随即死掉（或早就死了），没人能把取消落成终态。回收若把它打回 Queued，
    /// 认领谓词 <c>Queued &amp;&amp; !CancelRequested</c> 会永远跳过它，SSE 永远等不到终态。
    /// </summary>
    [Fact]
    public async Task ReclaimExpiredLeases_CancelRequestedExpiredRun_EndsCancelled_NotQueued()
    {
        var run = await SeedRunAsync(CliRunStatus.Running, leaseExpiresAt: DateTime.UtcNow.AddMinutes(-5), cancelRequested: true);

        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);

        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Cancelled);
        stored.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        stored.CompletedAt.ShouldNotBeNull();
        stored.LeaseExpiresAt.ShouldBeNull();
        stored.CancelRequested.ShouldBeTrue();

        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBeNull(
            "a cancelled run must never be claimable again");
    }

    [Fact]
    public async Task ReclaimExpiredLeases_CancelRequestedButLeaseStillLive_IsLeftAlone()
    {
        var run = await SeedRunAsync(CliRunStatus.Running, leaseExpiresAt: DateTime.UtcNow.AddMinutes(2), cancelRequested: true);

        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);

        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Running, "the executor on the live host owns this cancellation");
    }

    /// <summary>
    /// 执行器不只要看得见运行，还要把整段执行切到<b>运行自己的租户</b>：
    /// 绑定与运行时都带 TenantId，在 null 租户下解析会得到「Agent 已无绑定」—— 那条绑定明明在。
    /// 这里让 provider 注册表答「未知」，于是失败原因恰好证明绑定与运行时都已在租户下解析成功。
    /// </summary>
    [Fact]
    public async Task Executor_LoadsTenantOwnedRun_AndResolvesItsBindingUnderTheRunsTenant()
    {
        await SeedBindingAsync();
        var run = await SeedRunAsync(CliRunStatus.Dispatched);

        using var scope = ServiceProvider.CreateScope();
        await CreateExecutor(scope.ServiceProvider).ExecuteAsync(run.Id, CancellationToken.None);

        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Failed);
        stored.Error.ShouldNotBeNull();
        stored.Error.ShouldContain("Provider 'claude' is unknown",
            customMessage: "the binding and runtime must resolve under the run's tenant; " +
                           "'no external CLI binding' means the lookup ran with tenant=null");
    }

    /// <summary>MCP 回写凭据从未认证的入站请求校验：根作用域，没有租户。</summary>
    [Fact]
    public async Task TokenService_ValidatesATenantRunsCredential_FromATenantlessScope()
    {
        var run = await SeedRunAsync(CliRunStatus.Running);
        var issuer = new CliRunTokenService(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(), NullLogger<CliRunTokenService>.Instance);
        var secret = await issuer.IssueAsync(run, TimeSpan.FromMinutes(5), CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        using var scope = ServiceProvider.CreateScope();
        var validator = new CliRunTokenService(
            scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(), NullLogger<CliRunTokenService>.Instance);

        var credential = await validator.ValidateAsync(secret);

        credential.ShouldNotBeNull();
        credential.RunId.ShouldBe(run.Id);
        credential.TenantId.ShouldBe(TenantId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private static CliRunExecutor CreateExecutor(IServiceProvider scoped)
    {
        var runs = scoped.GetRequiredService<IRepository<CliRun, Guid>>();
        return new CliRunExecutor(
            runs,
            scoped.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            scoped.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            scoped.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            Mock.Of<ICliProviderRegistry>(),
            Mock.Of<ICliProtocolAdapterFactory>(),
            Mock.Of<ICliProcessHost>(),
            Mock.Of<ICliWorkspacePreparer>(),
            Mock.Of<ICliBriefComposer>(),
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            Mock.Of<ICliExecutableResolver>(),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            EnabledOptions(),
            scoped.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance);
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>租户的行：TenantId 显式写上（审计戳只在为空时补），种子上下文本身没有租户。</summary>
    private async Task<CliRun> SeedRunAsync(CliRunStatus status, DateTime? leaseExpiresAt = null, bool cancelRequested = false)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = status,
            Prompt = "tenant work",
            TenantId = TenantId,
            LeaseExpiresAt = leaseExpiresAt,
            ClaimedByHostId = leaseExpiresAt is null ? null : "dead-host",
            CancelRequested = cancelRequested
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        return run;
    }

    private async Task SeedBindingAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            TenantId = TenantId
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/usr/bin/claude",
            Status = CliRuntimeStatus.Online,
            TenantId = TenantId
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<CliRun>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == runId);
    }
}
