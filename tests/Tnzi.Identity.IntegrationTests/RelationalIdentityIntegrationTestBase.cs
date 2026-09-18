using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mapster;
using MapsterMapper;
using Tnzi.Caching;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tnzi.EFCore;
using Tnzi.EFCore.Internal;
using Tnzi.EventBus;
using Tnzi.Identity.Services;
using Tnzi.Mapster;

namespace Tnzi.Identity.IntegrationTests;

// 见 IntegrationTestBase.cs 顶部：`Organization` 的 using 必须在命名空间体内。
using Tnzi.Identity.Organization.Entities;

public abstract class RelationalIdentityIntegrationTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDisposable _mapperScope;
    protected ServiceProvider ServiceProvider { get; }
    protected TestIdentityDbContext DbContext { get; }
    protected UserManager<User> UserManager { get; }
    protected Mock<IEventBus> EventBusMock { get; } = new();
    protected Mock<ILoginLogSender> LoginLogSenderMock { get; } = new();
    protected ICache Cache { get; }

    /// <param name="configureIdentity">追加的身份选项。</param>
    /// <param name="configureServices">
    /// 在容器构建之前追加注册（例如把多租户打开：DbContext 的模型按 <c>MultiTenancyOptions</c> 决定
    /// 要不要映射 <c>User.TenantId</c>，所以必须在这里、不能在测试里事后改）。
    /// </param>
    protected RelationalIdentityIntegrationTestBase(
        Action<Tnzi.Identity.Options.IdentityOptions>? configureIdentity = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(x => x.Id).Returns(Guid.NewGuid());
        currentUserMock.Setup(x => x.UserName).Returns("testuser");
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        services.AddSingleton(currentUserMock.Object);

        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddDbContext<TestIdentityDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.EnableSensitiveDataLogging();
            // EF 按 DbContext 类型缓存模型；单租户与多租户的两组测试类共用同一个类型，
            // 并行跑时谁先建模谁赢。生产装配走的正是这个键工厂，测试里也要同一把钥匙。
            options.ReplaceService<IModelCacheKeyFactory, MultiTenancyModelCacheKeyFactory>();
        });
        services.AddDataProtection();

        services
            .AddIdentityCore<User>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<TestIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<CachingOptions>(options =>
        {
            options.DefaultExpirationMinutes = 30;
        });
        services.Configure<Tnzi.Identity.Options.IdentityOptions>(options =>
        {
            options.Otp.EnableEmail = true;
            options.Otp.EnableSms = true;
            options.Otp.CodeLength = 6;
            options.Otp.ExpirationMinutes = 10;
            options.Otp.ResendIntervalSeconds = 1;
            options.PasswordPolicy.PasswordHistoryCount = 3;
            options.PasswordPolicy.PasswordExpirationDays = 90;
            options.AccountSecurity.EnableAbnormalLoginDetection = true;
            options.AccountSecurity.NewIpRiskLevel = 40;
            options.AccountSecurity.NewDeviceRiskLevel = 35;
            options.AccountSecurity.ImpossibleTravelRiskLevel = 70;
            options.AccountSecurity.FrequentAttemptsRiskLevel = 80;
            options.AccountSecurity.MediumRiskThreshold = 30;
            options.AccountSecurity.HighRiskThreshold = 60;
            configureIdentity?.Invoke(options);
        });

        services.AddMemoryCache();
        services.AddSingleton<ICache, MemoryCacheService>();
        services.AddSingleton(EventBusMock.Object);
        services.AddSingleton(LoginLogSenderMock.Object);
        configureServices?.Invoke(services);

        ServiceProvider = services.BuildServiceProvider();
        DbContext = ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        DbContext.Database.EnsureCreated();
        UserManager = ServiceProvider.GetRequiredService<UserManager<User>>();
        Cache = ServiceProvider.GetRequiredService<ICache>();

        var config = new TypeAdapterConfig();
        config.NewConfig<Organization, OrganizationDto>()
            .MaxDepth(1);
        config.NewConfig<Organization, OrganizationTreeItemDto>();
        _mapperScope = MapperExtensions.PushMapper(new Mapper(config), config);
    }

    protected EFCoreRepository<TestIdentityDbContext, TEntity, Guid> CreateRepository<TEntity>()
        where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        return new EFCoreRepository<TestIdentityDbContext, TEntity, Guid>(DbContext, serviceProvider: ServiceProvider);
    }

    protected async Task<User> CreateUserAsync(
        string? email = null,
        string? phoneNumber = null,
        bool emailConfirmed = true,
        bool phoneConfirmed = true,
        DateTime? creationTime = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"user_{Guid.NewGuid():N}",
            Email = email,
            PhoneNumber = phoneNumber,
            EmailConfirmed = emailConfirmed,
            PhoneNumberConfirmed = phoneConfirmed,
            CreationTime = creationTime ?? DateTime.UtcNow,
            NormalizedUserName = $"USER_{Guid.NewGuid():N}",
            NormalizedEmail = email?.ToUpperInvariant()
        };

        var result = await UserManager.CreateAsync(user, "Password123!");
        Assert.True(result.Succeeded, result.Errors.FirstOrDefault()?.Description);
        return user;
    }

    protected async Task SaveChangesAsync()
    {
        await DbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        _mapperScope.Dispose();
        DbContext.Dispose();
        ServiceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
