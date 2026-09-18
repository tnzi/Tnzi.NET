using Microsoft.Data.Sqlite;
using Tnzi.EFCore;

namespace Tnzi.Identity.IntegrationTests;

/// <summary>
/// 多租户开启时，<see cref="IdentityDbContext{TDbContext}"/> 对同时是软删与多租户的实体
/// 必须**同时**保留两个查询过滤条件。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是一条数据可见性缺陷：EF Core 的无名 <c>HasQueryFilter</c> 是单槽覆盖语义，
/// 后一次调用会顶掉前一次。本类此前对 <c>ISoftDelete + IMultiTenant</c> 的实体先设软删过滤器、
/// 再设租户过滤器，于是多租户一开，**已软删的行对所有查询重新可见** ——
/// 已收回的角色功能、已删除的用户明细全部复活，而接口 200、日志正常。
/// </para>
/// <para>
/// 用真实 SQLite 而不是 InMemory：过滤器要真的翻译成 SQL 跑一遍。
/// 实体选 <see cref="UserDetail"/>（<c>MultiTenantAuditedEntity</c>，本模块自带），
/// 它就是那 69 个会中招的实体类型之一。
/// </para>
/// </remarks>
public class MultiTenantQueryFilterTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly TestIdentityDbContext _dbContext;

    public MultiTenantQueryFilterTests()
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

        // 开关走 options 扩展 —— 与 AddTnziDbContext 在运行期、DesignTimeDbContextFactoryBase 在设计期
        // 写入的是同一个载体，构造函数不需要收到 IOptions<MultiTenancyOptions>。
        // ★ 刻意不用「转发 IOptions」的形状：那样 options 与单租户夹具的完全相同，EF 会让两边共用
        //   同一个内部容器与同一份按上下文类型缓存的模型，先建模的那个夹具决定后面所有夹具的模型。
        services.AddDbContext<TestIdentityDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
            options.EnableSensitiveDataLogging();
        });

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestIdentityDbContext>();
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public void MultiTenancyIsOn_ForThisFixture()
    {
        // 前提：没有它，下面两条只是在测单租户模型。
        Assert.True(_dbContext.IsMultiTenancyEnabled);
        Assert.NotNull(_dbContext.Model.FindEntityType(typeof(UserDetail))!.FindProperty(nameof(UserDetail.TenantId)));
    }

    /// <summary>
    /// 一条已软删、属于当前租户的行：软删条件必须还在。
    /// </summary>
    [Fact]
    public async Task SoftDeletedRow_OfCurrentTenant_StaysHidden()
    {
        var user = await SeedUserAsync();
        await SeedDetailAsync(user.Id, TenantA, isDeleted: true, nickname: "deleted-in-a");

        _dbContext.ChangeTracker.Clear();

        var visible = await _dbContext.UserDetails.ToListAsync();
        var raw = await _dbContext.UserDetails.IgnoreQueryFilters().ToListAsync();

        Assert.Empty(visible);
        Assert.Single(raw);
    }

    /// <summary>
    /// 一条未删、属于另一个租户的行：租户条件必须还在。
    /// 与上一条并排，才能证明两个条件**同时**存在，而不是其中一个恰好赢了覆盖。
    /// </summary>
    [Fact]
    public async Task LiveRow_OfAnotherTenant_StaysHidden()
    {
        var user = await SeedUserAsync();
        await SeedDetailAsync(user.Id, TenantB, isDeleted: false, nickname: "live-in-b");
        await SeedDetailAsync(user.Id, TenantA, isDeleted: false, nickname: "live-in-a");

        _dbContext.ChangeTracker.Clear();

        var visible = await _dbContext.UserDetails.Select(d => d.Nickname).ToListAsync();

        Assert.Equal(["live-in-a"], visible);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"user_{Guid.NewGuid():N}",
            NormalizedUserName = $"USER_{Guid.NewGuid():N}",
            TenantId = TenantA,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    private async Task SeedDetailAsync(Guid userId, Guid tenantId, bool isDeleted, string nickname)
    {
        // 直接落库而不走仓储：仓储的软删路径最终也只是把 IsDeleted 置 true，
        // 这里要的正是「表里有一条 IsDeleted = true 的行」这个稳态。
        _dbContext.UserDetails.Add(new UserDetail
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Nickname = nickname,
            IsDeleted = isDeleted,
        });
        await _dbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
