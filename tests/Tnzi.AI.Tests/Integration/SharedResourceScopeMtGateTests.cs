using System.Net.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// P1 多租户集成门禁 — 证明 IScopedResource 实体（Provider / AgentPersona）在
/// <b>多租户开启（MultiTenancyOptions.Enabled = true）</b>且存在当前租户上下文时，
/// 其 System 行（TenantId=null）<b>不会</b>被 EF Core 全局多租户查询过滤器隐藏。
/// <para>
/// 这是整个"共享资源 Scope 模式"的核心证明：因为 Provider/AgentPersona 有意<b>不</b>实现
/// <see cref="IMultiTenant"/>，全局过滤器 <c>e.TenantId == currentTenant</c> 根本不会附加到它们的查询，
/// 所以 System 行对所有真实租户都可见。可见性改由服务层（ProviderService / AgentPersonaService）
/// 通过 Scope + TenantId 联合过滤强制（System ∪ 当前租户）。
/// </para>
/// <para>
/// 反证（对照组）：<see cref="Agent"/> 是真正的 <see cref="IMultiTenant"/> 实体。
/// 一条 TenantId=null 的 Agent 在当前租户 A 下被全局过滤器隐藏，证明过滤器确实在生效，
/// 反衬出 Provider/Persona 正确地"退出"了多租户过滤。
/// </para>
/// </summary>
public class SharedResourceScopeMtGateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public SharedResourceScopeMtGateTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Mapster 默认配置（同名属性自动映射，AgentPersonaService.MapTo/ProjectTo 需要）
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        // 用"无当前租户"的种子上下文创建表结构 + 写入精确的 TenantId 行：
        // 无租户上下文 → AuditPropertyHelper 不会把 IMultiTenant 的 TenantId 从 null 改写，
        // 让我们可以精确地造出 System(TenantId=null) 与 Tenant-B(TenantId=B) 两类行。
        using var seedContext = CreateContext(currentTenantId: null);
        seedContext.Database.EnsureCreated();
        SeedData(seedContext);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // =====================================================================
    // 核心证明 A — 直查 DbContext：System 行在 MT 开启 + 当前租户 A 下存活
    // =====================================================================

    [Fact]
    public async Task MtEnabled_TenantA_SystemProvider_NotHiddenByGlobalFilter()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantA);

        // 直查 Provider 集合（不经服务层）。Provider 不是 IMultiTenant，
        // 全局过滤器不附加 → System 行必然返回。
        var allNames = await ctx.Set<Provider>().Select(p => p.Name).ToListAsync();

        allNames.ShouldContain("system-provider",
            "System Provider (TenantId=null) MUST survive the global multi-tenant filter — " +
            "Provider opts out of IMultiTenant, so no e.TenantId==current clause is applied.");
        // Tenant-B 行也在底层返回（无全局过滤器）；服务层才负责把它挡掉（见证明 C）。
        allNames.ShouldContain("tenant-b-provider");
    }

    [Fact]
    public async Task MtEnabled_TenantA_SystemPersona_NotHiddenByGlobalFilter()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantA);

        var allSlugs = await ctx.Set<AgentPersona>().Select(p => p.Slug).ToListAsync();

        allSlugs.ShouldContain("system-persona",
            "System AgentPersona (TenantId=null) MUST survive the global multi-tenant filter — " +
            "AgentPersona opts out of IMultiTenant.");
        allSlugs.ShouldContain("tenant-b-persona");
    }

    // =====================================================================
    // 核心证明 B — 反证对照：真正的 IMultiTenant 实体(Agent) 的 System 行 *会* 被隐藏
    // 这证明全局过滤器确实在生效，Provider/Persona 是有意退出了它。
    // =====================================================================

    [Fact]
    public async Task MtEnabled_TenantA_IMultiTenantAgent_SystemRowIsHidden_ProvingFilterIsActive()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantA);

        // 先用 IgnoreQueryFilters 证明 system-agent 行确实持久化了（TenantId=null），
        // 排除"行根本没插进去"导致 ShouldNotContain 假阳性的可能。
        var rawNames = await ctx.Set<Agent>().IgnoreQueryFilters().Select(a => a.Name).ToListAsync();
        rawNames.ShouldContain("system-agent", "sanity: the IMultiTenant Agent row must actually exist in the DB");
        rawNames.ShouldContain("tenant-b-agent");

        // 再用带全局过滤器的查询：Agent 是 MultiTenantAuditedEntity → IMultiTenant → 受全局过滤器约束。
        var names = await ctx.Set<Agent>().Select(a => a.Name).ToListAsync();

        // 当前租户 A 下：TenantId=null 的 "system-agent" 被过滤器隐藏（反衬过滤器在生效），
        names.ShouldNotContain("system-agent",
            "A genuinely IMultiTenant Agent with TenantId=null is HIDDEN under tenant A — " +
            "this proves the global filter IS active, which is exactly what Provider/Persona opted out of.");
        // 同理 Tenant-B 的 Agent 对租户 A 不可见。
        names.ShouldNotContain("tenant-b-agent");
    }

    // =====================================================================
    // 核心证明 C — 经服务层：租户 A 看到 System ∪ 自己，绝不看到 Tenant-B
    // =====================================================================

    [Fact]
    public async Task ProviderService_TenantA_SeesSystemAndOwn_NeverTenantB()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantA);
        var service = CreateProviderService(ctx, _tenantA);

        var result = await service.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        var names = result.Data!.Items.Select(p => p.Name).ToList();
        names.ShouldContain("system-provider");   // System 共享行可见
        names.ShouldContain("tenant-a-provider");  // 自己的租户行可见
        names.ShouldNotContain("tenant-b-provider"); // 其他租户行不可见
    }

    [Fact]
    public async Task AgentPersonaService_TenantA_SeesSystemAndOwn_NeverTenantB()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantA);
        var service = CreateAgentPersonaService(ctx, _tenantA);

        var result = await service.GetListAsync(new AgentPersonaQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        var slugs = result.Data!.Items.Select(p => p.Slug).ToList();
        slugs.ShouldContain("system-persona");
        slugs.ShouldContain("tenant-a-persona");
        slugs.ShouldNotContain("tenant-b-persona");
    }

    // =====================================================================
    // 种子 + 工厂
    // =====================================================================

    private void SeedData(SharedResourceMtDbContext ctx)
    {
        // Provider：System + Tenant-A + Tenant-B（Provider 不是 IMultiTenant，TenantId 不会被自动改写）
        ctx.Set<Provider>().AddRange(
            new Provider { Name = "system-provider", ProviderType = "OpenAI", Scope = ResourceScope.System, TenantId = null },
            new Provider { Name = "tenant-a-provider", ProviderType = "OpenAI", Scope = ResourceScope.Tenant, TenantId = _tenantA },
            new Provider { Name = "tenant-b-provider", ProviderType = "OpenAI", Scope = ResourceScope.Tenant, TenantId = _tenantB });

        // AgentPersona：System + Tenant-A + Tenant-B
        ctx.Set<AgentPersona>().AddRange(
            new AgentPersona { Name = "System", Slug = "system-persona", Content = "x", Scope = ResourceScope.System, TenantId = null },
            new AgentPersona { Name = "Tenant A", Slug = "tenant-a-persona", Content = "x", Scope = ResourceScope.Tenant, TenantId = _tenantA },
            new AgentPersona { Name = "Tenant B", Slug = "tenant-b-persona", Content = "x", Scope = ResourceScope.Tenant, TenantId = _tenantB });

        // 对照组 Agent（真正的 IMultiTenant）：在无租户上下文下种子，TenantId 保持显式赋值不被改写。
        ctx.Set<Agent>().AddRange(
            new Agent { Name = "system-agent", Provider = "system-provider", TenantId = null },
            new Agent { Name = "tenant-b-agent", Provider = "tenant-b-provider", TenantId = _tenantB });

        ctx.SaveChanges();
    }

    /// <summary>
    /// 构建一个 <b>多租户开启</b> 的测试上下文；currentTenantId 决定全局过滤器与 ApplyVisibility 的当前租户。
    /// 共享同一 SQLite 连接，保证种子上下文与查询上下文看到同一份内存库。
    /// </summary>
    private SharedResourceMtDbContext CreateContext(Guid? currentTenantId)
    {
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);
        currentUserMock.Setup(m => m.TenantId).Returns((Guid?)null); // 不提供 fallback 租户

        var currentTenant = new StubCurrentTenant(currentTenantId);

        var options = new DbContextOptionsBuilder<SharedResourceMtDbContext>()
            .UseSqlite(_connection)
            // 模型缓存键随 MT 开关变化，避免与其他测试的单租户模型串用
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new SharedResourceMtDbContext(
            options,
            currentUserMock.Object,
            currentTenant,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    private ProviderService CreateProviderService(SharedResourceMtDbContext ctx, Guid tenantId)
    {
        var repo = new EFCoreRepository<SharedResourceMtDbContext, Provider, Guid>(ctx);
        var httpFactory = new Mock<IHttpClientFactory>();
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new ProviderService(repo, new StubDataProtectionProvider(), httpFactory.Object, sp, new StubCurrentTenant(tenantId));
    }

    private AgentPersonaService CreateAgentPersonaService(SharedResourceMtDbContext ctx, Guid tenantId)
    {
        var repo = new EFCoreRepository<SharedResourceMtDbContext, AgentPersona, Guid>(ctx);
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new AgentPersonaService(sp, repo, new StubCurrentTenant(tenantId));
    }

    // =====================================================================
    // Stubs
    // =====================================================================

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    /// <summary>明文加上前缀标记的最小 DataProtection 桩（Provider 可见性测试不实际加解密）。</summary>
    private sealed class StubDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new StubProtector();
        private sealed class StubProtector : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => this;
            public byte[] Protect(byte[] plaintext) => plaintext;
            public byte[] Unprotect(byte[] protectedData) => protectedData;
        }
    }
}

/// <summary>
/// 测试专用 DbContext — 注册 Provider(IScopedResource) / AgentPersona(IScopedResource) /
/// Agent(IMultiTenant 对照组)。通过显式传入 <see cref="MultiTenancyOptions"/> 控制 MT 开关。
/// </summary>
internal sealed class SharedResourceMtDbContext : TnziDbContext<SharedResourceMtDbContext>
{
    public SharedResourceMtDbContext(
        DbContextOptions<SharedResourceMtDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentPersonaConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
