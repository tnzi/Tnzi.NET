using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mapster;
using MapsterMapper;
using Tnzi.Caching;
using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Identity.Services;
using Tnzi.Mapster;

namespace Tnzi.Identity.IntegrationTests;

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

    protected RelationalIdentityIntegrationTestBase(Action<Tnzi.Identity.Options.IdentityOptions>? configureIdentity = null)
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
