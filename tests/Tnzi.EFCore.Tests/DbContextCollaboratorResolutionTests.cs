using Tnzi.Options;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 一个只声明 <c>(DbContextOptions, ICurrentUser)</c> 的消费方 DbContext 也必须读到
/// <see cref="ICurrentTenant"/>、<see cref="IDataFilterManager"/> 与 <see cref="TimeProvider"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 与多租户开关同一个根因、同一批漏网：两个 DbContext 基类把这三个协作者放在<b>可选</b>构造形参上，
/// 而脚手架 / 文档 / 消费方 DbContext 一律只声明 <c>(options, currentUser)</c>，DI 填不了没声明的形参。
/// 开关搬到 options 之后 <c>MultiTenancy:Enabled=true</c> 真的开了过滤器，可它过滤的租户只剩 JWT claim：
/// 凡经 <c>ICurrentTenant.Change()</c> 建立的租户（请求头 / 后台按租户轮转 / 注册登录流程）对查询过滤器与
/// <c>TenantId</c> 赋值一律不可见 —— 后台作业读到零行或写出 <c>TenantId = NULL</c>；
/// <c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;()</c> 在这种上下文上是空操作，过滤器照开；
/// 测试里换掉的 <c>TimeProvider</c> 对审计时间戳也不起作用。全程接口 200、零日志。
/// </para>
/// <para>
/// 修法与开关同一条路：注入值缺席时从 options 携带的应用容器（<c>CoreOptionsExtension.ApplicationServiceProvider</c>，
/// 即 <c>AddDbContext</c> 记下的请求作用域容器）解析；手工 <c>new</c> 出来的上下文（设计期）没有容器，仍是 null。
/// 这组用例用<b>真实</b>的 <see cref="CurrentTenant"/> 与 <see cref="DataFilterManager"/>，不 Mock 被测对象。
/// </para>
/// </remarks>
public class DbContextCollaboratorResolutionTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly DateTimeOffset FakeNow = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;

    public DbContextCollaboratorResolutionTests()
    {
        _connection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MultiTenancy:Enabled"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // 用户带着租户 A 的 claim：没有 Change() 覆盖时过滤器按它过滤。
        var user = new MockCurrentUser();
        user.SetUser(Guid.NewGuid(), "user-a", TenantA);
        services.AddSingleton<ICurrentUser>(user);
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FakeNow));
        services.AddTnziOptions<MultiTenancyOptions>(configuration);
        services.AddTnziDbContext<MultiTenancyProbeDbContext>(o => o.UseSqlite(_connection), isPrimary: true);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();
        context.Database.EnsureCreated();
        context.Rows.AddRange(
            new ProbeTenantRow { Name = "row-a", TenantId = TenantA },
            new ProbeTenantRow { Name = "row-b", TenantId = TenantB });
        context.SaveChangesAsync().GetAwaiter().GetResult();
    }

    /// <summary>前提：夹具确实是两参上下文且多租户开着，否则下面测的是别的东西。</summary>
    [Fact]
    public void Precondition_TwoArgContext_WithMultiTenancyOn()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

        Assert.True(context.IsMultiTenancyEnabled);
        Assert.Equal(["row-a"], context.Rows.Select(r => r.Name).ToList());
    }

    /// <summary>
    /// <c>ICurrentTenant.Change(B)</c> 之后同一个作用域里的查询看到的是 B 的行。
    /// 修复前：上下文根本没拿到 ICurrentTenant，仍按 claim 里的 A 过滤。
    /// </summary>
    [Fact]
    public async Task CurrentTenantChange_ChangesTheRowsTheFilterSees()
    {
        using var scope = _provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

        using (currentTenant.Change(TenantB))
        {
            Assert.Equal(["row-b"], await context.Rows.Select(r => r.Name).ToListAsync());
        }

        Assert.Equal(["row-a"], await context.Rows.Select(r => r.Name).ToListAsync());
    }

    /// <summary>
    /// <c>ICurrentTenant.Change(B)</c> 之内新增的行落库时 <c>TenantId = B</c>。
    /// 修复前：审计填充只看得到 claim，写成 A（后台作业无 claim 时写成 NULL）。
    /// </summary>
    [Fact]
    public async Task CurrentTenantChange_IsTheTenantPersistedOnNewRows()
    {
        Guid id;
        using (var scope = _provider.CreateScope())
        {
            var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

            using (currentTenant.Change(TenantB))
            {
                var row = new ProbeTenantRow { Name = "written-under-b" };
                context.Rows.Add(row);
                await context.SaveChangesAsync();
                id = row.Id;
            }
        }

        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();
            var persisted = await context.Rows.IgnoreQueryFilters().SingleAsync(r => r.Id == id);

            Assert.Equal(TenantB, persisted.TenantId);
        }
    }

    /// <summary>
    /// <c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;()</c> 让查询跨租户。
    /// 修复前：上下文没拿到过滤器管理器，<c>IsEnabled</c> 恒按 true 处理，禁用是空操作。
    /// </summary>
    [Fact]
    public async Task DisablingTheTenantFilter_WidensTheQuery()
    {
        using var scope = _provider.CreateScope();
        var filters = scope.ServiceProvider.GetRequiredService<IDataFilterManager>();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

        using (filters.Disable<IMultiTenantFilter>())
        {
            Assert.Equal(["row-a", "row-b"], await context.Rows.Select(r => r.Name).OrderBy(n => n).ToListAsync());
        }

        Assert.Equal(["row-a"], await context.Rows.Select(r => r.Name).ToListAsync());
    }

    /// <summary>容器里换掉的 <see cref="TimeProvider"/> 决定审计时间戳。</summary>
    [Fact]
    public async Task RegisteredTimeProvider_StampsAuditTimes()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

        var row = new ProbeTenantRow { Name = "timed" };
        context.Rows.Add(row);
        await context.SaveChangesAsync();

        Assert.Equal(FakeNow.UtcDateTime, row.CreationTime);
    }

    /// <summary>
    /// 手工构造（设计期 / 工厂）没有应用容器：三个协作者保持 null，不抛，不改既往行为。
    /// </summary>
    [Fact]
    public void ManuallyConstructedContext_WithoutApplicationContainer_KeepsNulls()
    {
        var options = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>()
            .UseSqlite(_connection)
            .UseTnziMultiTenancy(true)
            .Options;

        using var context = new MultiTenancyProbeDbContext(options, new MockCurrentUser());

        Assert.Null(context.ExposedCurrentTenant);
        Assert.Null(context.ExposedDataFilterManager);
        Assert.Null(context.ExposedTimeProvider);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
