using Microsoft.Data.Sqlite;
using Tnzi.Data;
using Tnzi.EFCore;
using Tnzi.EFCore.Data;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests;

/// <summary>
/// <see cref="IdentityDbContext{TDbContext}"/> 的消费方形状（不声明 <see cref="ICurrentTenant"/> /
/// <see cref="IDataFilterManager"/> 形参）也必须读到容器里的这两个协作者。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 与多租户开关同一个根因、同一批漏网（EFCore 侧的同形用例在 <c>Tnzi.EFCore.Tests.DbContextCollaboratorResolutionTests</c>）：
/// 基类把两个协作者放在可选构造形参上，消费方不声明，DI 填不了。于是开关开了、过滤器加了，
/// 它过滤的租户却只剩 JWT claim —— <c>ICurrentTenant.Change()</c> 建立的租户对过滤器与 <c>TenantId</c> 赋值不可见，
/// <c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;()</c> 是空操作。
/// </para>
/// <para>
/// 用真实的 <see cref="CurrentTenant"/> 与 <see cref="DataFilterManager"/>（两者的状态都在静态 AsyncLocal 上），
/// 真实 SQLite，实体用 <see cref="UserDetail"/>（<c>MultiTenantAuditedEntity</c>）。
/// </para>
/// </remarks>
public class MultiTenantCollaboratorResolutionTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public MultiTenantCollaboratorResolutionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(x => x.Id).Returns(Guid.NewGuid());
        currentUserMock.Setup(x => x.UserName).Returns("tenant-a-user");
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.TenantId).Returns(TenantA);
        services.AddSingleton(currentUserMock.Object);

        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<IDataFilterManager, DataFilterManager>();

        services.AddDbContext<TestIdentityDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
            options.EnableSensitiveDataLogging();
        });

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        dbContext.Database.EnsureCreated();

        // UserDetail 对 UserId 唯一（一对一）：每个租户各一个用户各一条明细。
        dbContext.UserDetails.AddRange(
            new UserDetail { Id = Guid.NewGuid(), UserId = SeedUser(dbContext, TenantA), TenantId = TenantA, Nickname = "in-a" },
            new UserDetail { Id = Guid.NewGuid(), UserId = SeedUser(dbContext, TenantB), TenantId = TenantB, Nickname = "in-b" });
        dbContext.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private static Guid SeedUser(TestIdentityDbContext dbContext, Guid tenantId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"user_{Guid.NewGuid():N}",
            NormalizedUserName = $"USER_{Guid.NewGuid():N}",
            TenantId = tenantId,
        };
        dbContext.Users.Add(user);
        return user.Id;
    }

    /// <summary>修复前：上下文没拿到 ICurrentTenant，Change(B) 之内仍按 claim 里的 A 过滤。</summary>
    [Fact]
    public async Task CurrentTenantChange_ChangesTheRowsTheFilterSees()
    {
        using var scope = _serviceProvider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();

        using (currentTenant.Change(TenantB))
        {
            Assert.Equal(["in-b"], await dbContext.UserDetails.Select(d => d.Nickname).ToListAsync());
        }

        Assert.Equal(["in-a"], await dbContext.UserDetails.Select(d => d.Nickname).ToListAsync());
    }

    /// <summary>修复前：审计填充只看得到 claim，Change(B) 之内新增的行写成 A。</summary>
    [Fact]
    public async Task CurrentTenantChange_IsTheTenantPersistedOnNewRows()
    {
        var id = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var dbContext = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();

            using (currentTenant.Change(TenantB))
            {
                // 新用户 + 新明细：TenantId 刻意不填，由审计填充决定它是 B 还是 claim 里的 A。
                var userId = SeedUser(dbContext, TenantB);
                dbContext.UserDetails.Add(new UserDetail { Id = id, UserId = userId, Nickname = "written-under-b" });
                await dbContext.SaveChangesAsync();
            }
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
            var persisted = await dbContext.UserDetails.IgnoreQueryFilters().SingleAsync(d => d.Id == id);

            Assert.Equal(TenantB, persisted.TenantId);
        }
    }

    /// <summary>修复前：上下文没拿到过滤器管理器，禁用租户过滤器是空操作。</summary>
    [Fact]
    public async Task DisablingTheTenantFilter_WidensTheQuery()
    {
        using var scope = _serviceProvider.CreateScope();
        var filters = scope.ServiceProvider.GetRequiredService<IDataFilterManager>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();

        using (filters.Disable<IMultiTenantFilter>())
        {
            Assert.Equal(["in-a", "in-b"], await dbContext.UserDetails.Select(d => d.Nickname).OrderBy(n => n).ToListAsync());
        }

        Assert.Equal(["in-a"], await dbContext.UserDetails.Select(d => d.Nickname).ToListAsync());
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
