using Microsoft.Data.Sqlite;
using Tnzi.EFCore;

namespace Tnzi.Identity.IntegrationTests;

/// <summary>
/// 多租户开启时，用户名与邮箱<b>仍然全局唯一</b>：模型、数据库约束、`UserManager` 的查重三者一个口径。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前 <c>UserConfiguration</c> 在多租户分支把 <c>UserNameIndex</c> 改成非唯一、另建
/// <c>(TenantId, NormalizedUserName)</c> 复合唯一索引，文档据此宣称「允许不同租户下存在相同用户名」。
/// 但 <c>User</c> 刻意不受租户过滤器管（登录时租户上下文尚未建立、全局账号 <c>TenantId = null</c>），
/// 于是 ASP.NET Identity 默认的 <c>UserValidator</c> 经无过滤的 <c>FindByNameAsync</c> 全局查重，
/// 第二个 <c>admin</c> 无论在哪个租户都被拒 —— 复合索引与那句文档都是死的，而登录按用户名找人本就不带租户。
/// </para>
/// <para>
/// 三者里只有索引一处能被「改回租户内唯一」而不惊动任何测试：模型不报错、业务测试全绿、
/// 直到某个绕过 <c>UserManager</c> 的写入造出重名，登录再也分不清找的是谁。本类把口径钉在模型与数据库上。
/// </para>
/// </remarks>
public class MultiTenantUserNameUniquenessTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly TestIdentityDbContext _dbContext;

    public MultiTenantUserNameUniquenessTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.Id).Returns(Guid.NewGuid());
        currentUser.Setup(x => x.IsAuthenticated).Returns(true);
        currentUser.Setup(x => x.TenantId).Returns(TenantA);
        services.AddSingleton(currentUser.Object);

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
    public void TheModel_KeepsTheGlobalUserNameIndexUnique_AndHasNoPerTenantOne()
    {
        Assert.True(_dbContext.IsMultiTenancyEnabled);
        var indexes = _dbContext.Model.FindEntityType(typeof(User))!.GetIndexes().ToList();

        var userName = Assert.Single(indexes, i => i.GetDatabaseName() == "UserNameIndex");
        Assert.True(userName.IsUnique);
        Assert.Equal([nameof(User.NormalizedUserName)], userName.Properties.Select(p => p.Name).ToArray());
        Assert.DoesNotContain(indexes, i => i.GetDatabaseName() == "UserTenantNameIndex");
        // 租户列的普通索引仍在：按租户裁剪的查询靠它。
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(User.TenantId)]));
    }

    [Fact]
    public async Task TheSameUserName_InAnotherTenant_IsRejectedByTheDatabase()
    {
        _dbContext.Users.Add(NewUser("admin", TenantA));
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        _dbContext.Users.Add(NewUser("admin", TenantB));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    private static User NewUser(string userName, Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = $"{Guid.NewGuid():N}@example.com",
        TenantId = tenantId,
    };

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
