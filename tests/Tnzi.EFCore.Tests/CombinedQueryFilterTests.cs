namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="TnziDbContext{TDbContext}"/> 对同时是软删与多租户的实体：两个过滤条件必须同时存在，
/// 且必须按**当前**上下文实例求值。
/// </summary>
/// <remarks>
/// <para>
/// 组合过滤器此前只有 <c>TnziDbContext</c> 一份、<c>IdentityDbContext</c> 另一套没有组合，
/// 而两边都没有测试直接覆盖。现在两个基类共用一处配置，这组用例守住共用实现在本类上的行为；
/// Identity 那侧的同形用例在 <c>Tnzi.Identity.IntegrationTests</c>。
/// </para>
/// <para>
/// ★ 「按当前实例求值」那条值得单独钉：EF 只会把过滤器表达式里<b>直接</b>引用的 DbContext 实例
/// 替换成执行查询的那个实例；若把开关或租户 Id 捕获进闭包，模型缓存后所有实例都会读到
/// <b>第一个</b>建模实例的值 —— 换个租户查，看到的还是上一个租户的行。
/// </para>
/// </remarks>
public class CombinedQueryFilterTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<TestDbContext> _options;

    public CombinedQueryFilterTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .UseTnziMultiTenancy(true)
            .EnableSensitiveDataLogging()
            .Options;

        using var seed = CreateContext(TenantA);
        seed.Database.EnsureCreated();
        seed.Users.AddRange(
            new TestUser { UserName = "live-a", Email = "a@t.test", TenantId = TenantA },
            new TestUser { UserName = "deleted-a", Email = "da@t.test", TenantId = TenantA, IsDeleted = true },
            new TestUser { UserName = "live-b", Email = "b@t.test", TenantId = TenantB },
            new TestUser { UserName = "deleted-b", Email = "db@t.test", TenantId = TenantB, IsDeleted = true });
        seed.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private TestDbContext CreateContext(Guid tenantId, IDataFilterManager? dataFilterManager = null)
    {
        var tenant = new MockCurrentTenant();
        tenant.SetTenant(tenantId, "tenant");
        return new TestDbContext(_options, new MockCurrentUser(), tenant, dataFilterManager);
    }

    [Fact]
    public async Task BothConditions_ApplyAtOnce()
    {
        using var context = CreateContext(TenantA);

        var names = await context.Users.Select(u => u.UserName).ToListAsync();
        var raw = await context.Users.IgnoreQueryFilters().CountAsync();

        Assert.Equal(["live-a"], names);
        Assert.Equal(4, raw);
    }

    /// <summary>模型已被第一个实例建好并缓存后，第二个实例按自己的租户过滤。</summary>
    [Fact]
    public async Task Filter_EvaluatesAgainstTheQueryingInstance()
    {
        using (var first = CreateContext(TenantA))
        {
            Assert.Equal(["live-a"], await first.Users.Select(u => u.UserName).ToListAsync());
        }

        using var second = CreateContext(TenantB);

        Assert.Equal(["live-b"], await second.Users.Select(u => u.UserName).ToListAsync());
    }

    /// <summary>关掉软删过滤器：已删行回来，租户条件仍在。</summary>
    [Fact]
    public async Task DisablingSoftDeleteFilter_KeepsTheTenantCondition()
    {
        var filterManager = new Mock<IDataFilterManager>();
        filterManager.Setup(m => m.IsEnabled<ISoftDeleteFilter>()).Returns(false);
        filterManager.Setup(m => m.IsEnabled<IMultiTenantFilter>()).Returns(true);
        using var context = CreateContext(TenantA, filterManager.Object);

        var names = await context.Users.Select(u => u.UserName).OrderBy(n => n).ToListAsync();

        Assert.Equal(["deleted-a", "live-a"], names);
    }

    /// <summary>关掉租户过滤器：别的租户回来，软删条件仍在。</summary>
    [Fact]
    public async Task DisablingTenantFilter_KeepsTheSoftDeleteCondition()
    {
        var filterManager = new Mock<IDataFilterManager>();
        filterManager.Setup(m => m.IsEnabled<ISoftDeleteFilter>()).Returns(true);
        filterManager.Setup(m => m.IsEnabled<IMultiTenantFilter>()).Returns(false);
        using var context = CreateContext(TenantA, filterManager.Object);

        var names = await context.Users.Select(u => u.UserName).OrderBy(n => n).ToListAsync();

        Assert.Equal(["live-a", "live-b"], names);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
