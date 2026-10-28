using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 多租户开启时按 Agent 的 USD 预算：宿主级共享 YAML Agent 按名称配置的上限必须对租户调用者生效，
/// 每个租户按自己的花费计量，一个租户的花费不能经缓存拦下（或放行）另一个租户。
/// </summary>
/// <remarks>
/// 真实的全局过滤器（多租户开启的模型 + 严格等值的租户过滤器）。共享 YAML Agent 行的 <c>TenantId</c> 为 null，
/// 普通查询对租户调用者不可见；按名查不到 ⇒ 按名配置的上限对所有租户静默不执行。
/// 花费聚合受 UsageLog 租户过滤，缓存键却不含租户 ⇒ 同一个共享 Agent 的 id 在各租户间串用缓存。
/// </remarks>
public class BudgetServiceMultiTenantTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2");

    private readonly SqliteConnection _connection;
    private readonly Guid _sharedAgentId;
    private readonly AIOptions _options = new()
    {
        Budget = new BudgetOptions
        {
            Enabled = true,
            DefaultMonthlyBudgetUsd = 1000m,
            WarningThreshold = 0.8,
            CacheTtlSeconds = 60
        }
    };

    public BudgetServiceMultiTenantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var host = CreateContext(tenantId: null);
        host.Database.EnsureCreated();

        // 共享 Agent 的 id 每个测试实例都不同：花费缓存是进程级静态字典，别的用例的键不会撞上
        var shared = new Agent { Name = "support-bot", Provider = "OpenAI", Source = AgentSources.Yaml };
        host.Set<Agent>().Add(shared);
        host.SaveChangesAsync().GetAwaiter().GetResult();
        _sharedAgentId = shared.Id;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Precondition_TenantQuery_CannotSeeTheSharedAgentRow()
    {
        await using var ctx = CreateContext(TenantA);
        (await ctx.Set<Agent>().AnyAsync(a => a.Id == _sharedAgentId)).ShouldBeFalse();
    }

    [Fact]
    public async Task NameKeyedBudget_OnASharedYamlAgent_IsEnforcedForATenantCaller()
    {
        _options.Budget.PerAgentBudgets["support-bot"] = 10m;
        await SeedSpendAsync(TenantA, 12m);

        await using var ctx = CreateContext(TenantA);
        var result = await CreateService(ctx, TenantA).CheckBudgetAsync(Guid.NewGuid(), TenantA, _sharedAgentId);

        result.IsAllowed.ShouldBeFalse("the per-agent cap configured by name must apply to the shared agent");
        result.Reason.ShouldNotBeNull().ShouldContain("Agent budget exceeded");
    }

    [Fact]
    public async Task AgentSpendCache_IsNotSharedAcrossTenants()
    {
        _options.Budget.PerAgentBudgets[_sharedAgentId.ToString()] = 10m;
        await SeedSpendAsync(TenantA, 12m);

        await using (var ctxA = CreateContext(TenantA))
        {
            (await CreateService(ctxA, TenantA).CheckBudgetAsync(Guid.NewGuid(), TenantA, _sharedAgentId))
                .IsAllowed.ShouldBeFalse();
        }

        await using var ctxB = CreateContext(TenantB);
        var resultB = await CreateService(ctxB, TenantB).CheckBudgetAsync(Guid.NewGuid(), TenantB, _sharedAgentId);

        resultB.IsAllowed.ShouldBeTrue("tenant B has spent nothing on this agent; tenant A's cached spend must not block it");
        resultB.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateSpend_InvalidatesTheSameTenantKeyTheCheckReads()
    {
        _options.Budget.PerAgentBudgets[_sharedAgentId.ToString()] = 10m;
        var user = Guid.NewGuid();

        await using var ctx = CreateContext(TenantB);
        var service = CreateService(ctx, TenantB);
        (await service.CheckBudgetAsync(user, TenantB, _sharedAgentId)).IsAllowed.ShouldBeTrue();

        await SeedSpendAsync(TenantB, 12m);
        await service.UpdateSpendAsync(user, TenantB, _sharedAgentId, 12m);

        (await service.CheckBudgetAsync(user, TenantB, _sharedAgentId)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Summary_ShowsTheSharedAgentsNameAndItsNameKeyedLimit()
    {
        _options.Budget.PerAgentBudgets["support-bot"] = 10m;
        await SeedSpendAsync(TenantA, 3m);

        await using var ctx = CreateContext(TenantA);
        var now = DateTime.UtcNow;
        var summary = await CreateService(ctx, TenantA).GetSummaryAsync(TenantA, now.AddDays(-1), now.AddDays(1));

        var row = summary.ByAgent.ShouldHaveSingleItem();
        row.AgentId.ShouldBe(_sharedAgentId);
        row.AgentName.ShouldBe("support-bot");
        row.AgentBudgetLimitUsd.ShouldBe(10m);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task SeedSpendAsync(Guid tenantId, decimal costUsd)
    {
        await using var host = CreateContext(tenantId: null);
        host.Set<UsageLog>().Add(new UsageLog
        {
            Provider = "OpenAI",
            Model = "gpt-4o",
            OperationType = "Chat",
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            IsSuccess = true,
            EstimatedCostUsd = costUsd,
            AgentId = _sharedAgentId,
            TenantId = tenantId,
            CreationTime = DateTime.UtcNow
        });
        await host.SaveChangesAsync();
    }

    private BudgetService CreateService(BudgetMtDbContext ctx, Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(_options));
        services.AddSingleton<ICurrentTenant>(new StubCurrentTenant(tenantId));
        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
        services.AddSingleton<IRepository<UsageLog, Guid>>(new EFCoreRepository<BudgetMtDbContext, UsageLog, Guid>(ctx));
        services.AddSingleton<IRepository<Agent, Guid>>(new EFCoreRepository<BudgetMtDbContext, Agent, Guid>(ctx));
        var provider = services.BuildServiceProvider();

        // 与 DI 容器同一条构造路径：可选的租户依赖由容器按需注入
        return ActivatorUtilities.CreateInstance<BudgetService>(provider);
    }

    private BudgetMtDbContext CreateContext(Guid? tenantId)
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(m => m.Id).Returns(Guid.Empty);
        user.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<BudgetMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new BudgetMtDbContext(options, user.Object, new StubCurrentTenant(tenantId),
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    private sealed class StubCurrentTenant(Guid? tenantId) : ICurrentTenant
    {
        public Guid? Id { get; } = tenantId;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();

        private sealed class NoOp : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

/// <summary>多租户开启的最小预算模型：Agent 与用量日志。</summary>
internal sealed class BudgetMtDbContext : TnziDbContext<BudgetMtDbContext>
{
    public BudgetMtDbContext(
        DbContextOptions<BudgetMtDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new AgentConfiguration().RegisterTo(modelBuilder, this);
        new ProviderConfiguration().RegisterTo(modelBuilder, this);
        new UsageLogConfiguration().RegisterTo(modelBuilder, this);

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
