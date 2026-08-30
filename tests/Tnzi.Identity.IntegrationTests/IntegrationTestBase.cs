using Microsoft.Extensions.Options;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests;

// ★ 必须写在命名空间体内：本项目的命名空间落在 Tnzi.Identity 之下，而组织包让
// Tnzi.Identity 多了一个名叫 `Organization` 的**命名空间**成员，裸写会报 CS0118。
// global using 属于编译单元层，在这条查找路径上永远抢不到。
using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 测试用的具体 DbContext 实现
/// </summary>
public class TestIdentityDbContext : IdentityDbContext<TestIdentityDbContext>
{
    public TestIdentityDbContext(
        DbContextOptions<TestIdentityDbContext> options,
        ICurrentUser currentUser,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, multiTenancyOptions: multiTenancyOptions)
    {
    }

    // 显式定义 DbSet 以便测试使用。
    // ★ Organizations 这个 DbSet 名**刻意保留**：EF 的默认表名取自 DbSet 属性名，
    //   于是这里的表一直叫 Identity_Organizations（复数）。改掉它会让快照产生一次
    //   rename，而这个项目正是用来证明拆分**不产生迁移**的。
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
    public DbSet<UserDetail> UserDetails => Set<UserDetail>();
    public DbSet<PasswordHistory> PasswordHistories => Set<PasswordHistory>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
}

/// <summary>
/// 集成测试基类
/// 提供内存数据库和服务容器配置
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected ServiceProvider ServiceProvider { get; }
    protected TestIdentityDbContext DbContext { get; }

    protected IntegrationTestBase()
    {
        var services = new ServiceCollection();

        // Mock ICurrentUser
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(x => x.Id).Returns(Guid.NewGuid());
        currentUserMock.Setup(x => x.UserName).Returns("testuser");
        services.AddSingleton(currentUserMock.Object);

        // 配置内存数据库
        services.AddDbContext<TestIdentityDbContext>(options =>
        {
            options.UseInMemoryDatabase($"IdentityTestDb_{Guid.NewGuid()}");
            options.EnableSensitiveDataLogging();
        });

        // 注册其他必要的服务
        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();
        DbContext = ServiceProvider.GetRequiredService<TestIdentityDbContext>();

        // 确保数据库已创建
        DbContext.Database.EnsureCreated();
    }

    /// <summary>
    /// 子类可以重写此方法来注册额外的服务
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // 默认实现为空，子类可以重写
    }

    /// <summary>
    /// 获取服务实例
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// 保存数据库更改
    /// </summary>
    protected async Task SaveChangesAsync()
    {
        await DbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        DbContext?.Dispose();
        ServiceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
