using Microsoft.Data.Sqlite;
using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Entities.Configs;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;
using Tnzi.EFCore.Data;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// A3 多租户隔离门禁 - 证明 <see cref="DefaultSessionBinder"/> 把数据库
/// <see cref="SessionBindingRule"/> 行<b>按 TenantId 分区</b>，杜绝跨租户绑定泄漏：
/// 租户 A 的入站流量绝不会命中租户 B 的规则，反之亦然。
/// <para>
/// 关键证明点：<see cref="SessionBindingRule"/> 现在是真正的 <see cref="Tnzi.Domain.Entities.IMultiTenant"/> 实体。
/// 绑定器的后台缓存在一个<b>无当前租户</b>的全新作用域里加载所有规则，因此必须在加载时
/// 临时禁用 <see cref="IMultiTenantFilter"/>，否则全局过滤器 <c>e.TenantId == null</c> 会把
/// 所有带租户的规则隐藏掉、缓存为空。隔离改由<b>匹配时</b>的 <c>rule.TenantId == context.TenantId</c>
/// 强制（带 null TenantId 的部署级规则对任意上下文都匹配）。
/// </para>
/// </summary>
public sealed class SessionBinderMultiTenantTests : IDisposable
{
    private static readonly Guid AgentA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgentB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid DefaultAgent = Guid.Parse("dddddddd-0000-0000-0000-000000000003");

    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public SessionBinderMultiTenantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // 用"无当前租户"的种子上下文建表 + 精确写入两类带 TenantId 的规则行。
        // 无租户上下文 → 审计填充不会把显式赋的 TenantId 改写。
        using var seed = CreateContext(currentTenantId: null);
        seed.Database.EnsureCreated();

        seed.Set<SessionBindingRule>().AddRange(
            new SessionBindingRule
            {
                Channel = "telegram", PeerId = "u1", AgentId = AgentA,
                Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true, TenantId = _tenantA
            },
            new SessionBindingRule
            {
                Channel = "telegram", PeerId = "u1", AgentId = AgentB,
                Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true, TenantId = _tenantB
            });
        seed.SaveChangesAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    // =====================================================================
    // 核心证明 - 跨租户隔离：A 的流量 → AgentA；B 的流量 → AgentB；null → 兜底
    // =====================================================================

    [Fact]
    public void Resolve_TenantA_GetsTenantARule_NeverTenantB()
    {
        var binder = CreateBinder();

        var binding = binder.Resolve(Ctx(tenantId: _tenantA));

        binding.AgentId.ShouldBe(AgentA,
            "Tenant A inbound traffic MUST bind to Tenant A's rule, never Tenant B's.");
    }

    [Fact]
    public void Resolve_TenantB_GetsTenantBRule_NeverTenantA()
    {
        var binder = CreateBinder();

        var binding = binder.Resolve(Ctx(tenantId: _tenantB));

        binding.AgentId.ShouldBe(AgentB,
            "Tenant B inbound traffic MUST bind to Tenant B's rule, never Tenant A's.");
    }

    [Fact]
    public void Resolve_NullTenant_MatchesNoTenantRule_FallsToDefault()
    {
        var binder = CreateBinder();

        // 上下文无租户（单租户/全局默认）。两条 DB 规则都带 TenantId，
        // 故都不匹配 → 落到 GatewayOptions 默认 Agent。
        var binding = binder.Resolve(Ctx(tenantId: null));

        binding.AgentId.ShouldBe(DefaultAgent,
            "A tenant-scoped DB rule MUST NOT match a null-tenant context; it falls to the deployment default.");
        binding.AgentId.ShouldNotBe(AgentA);
        binding.AgentId.ShouldNotBe(AgentB);
    }

    // =====================================================================
    // 单租户回归 - MT 关闭 + null-tenant 全局规则仍照常匹配（无回归）
    // =====================================================================

    [Fact]
    public void Resolve_ConfigRule_IsDeploymentGlobal_MatchesAnyTenant()
    {
        // 配置规则（TenantId=null）是部署级全局规则，应匹配任意租户上下文。
        var configRules = new List<SessionBindingRule>
        {
            new() { Channel = "slack", AgentId = AgentA, Scope = SessionScope.Global, Priority = 5, IsEnabled = true }
        };
        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = DefaultAgent });
        var binder = new DefaultSessionBinder(configRules, options, CreateScopeFactory());

        var forTenantB = binder.Resolve(new SessionBindingContext
        {
            Channel = "slack", ChatId = "c1", UserId = "anyone", TenantId = _tenantB
        });

        forTenantB.AgentId.ShouldBe(AgentA,
            "A config (deployment-level, null-tenant) rule must match ANY tenant context.");
    }

    // =====================================================================
    // 工厂 / 辅助
    // =====================================================================

    private DefaultSessionBinder CreateBinder()
    {
        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = DefaultAgent });
        // 空配置规则 → 只测 DB 规则的租户分区行为。
        return new DefaultSessionBinder([], options, CreateScopeFactory());
    }

    private static SessionBindingContext Ctx(Guid? tenantId)
        => new() { Channel = "telegram", ChatId = "c1", UserId = "u1", TenantId = tenantId };

    /// <summary>
    /// 构造一个真实作用域工厂：每次 CreateScope 产出一个<b>无当前租户</b>的 MT-on DbContext +
    /// 真实仓储 + 真实 <see cref="DataFilterManager"/>。绑定器必须能从该作用域取到 IDataFilterManager
    /// 并临时禁用多租户过滤器，否则缓存里一条租户规则都看不到。
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_connection);
        services.AddScoped<IDataFilterManager, DataFilterManager>();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);
        services.AddSingleton(currentUser.Object);

        // 全新作用域无当前租户 → GetCurrentTenantId() 返回 null。
        services.AddSingleton<ICurrentTenant>(new StubCurrentTenant(null));

        services.AddScoped(sp => CreateContext(
            currentTenantId: null,
            dataFilterManager: sp.GetService<IDataFilterManager>()));
        services.AddScoped<IRepository<SessionBindingRule, Guid>>(sp =>
            new EFCoreRepository<SessionBinderMtDbContext, SessionBindingRule, Guid>(
                sp.GetRequiredService<SessionBinderMtDbContext>()));

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private SessionBinderMtDbContext CreateContext(Guid? currentTenantId, IDataFilterManager? dataFilterManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<SessionBinderMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new SessionBinderMtDbContext(
            options,
            currentUser.Object,
            new StubCurrentTenant(currentTenantId),
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
/// 测试专用 DbContext，注册 <see cref="SessionBindingRule"/>（IMultiTenant），MT 开启。
/// </summary>
internal sealed class SessionBinderMtDbContext : TnziDbContext<SessionBinderMtDbContext>
{
    public SessionBinderMtDbContext(
        DbContextOptions<SessionBinderMtDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        IDataFilterManager? dataFilterManager,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, dataFilterManager, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SessionBindingRuleConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
