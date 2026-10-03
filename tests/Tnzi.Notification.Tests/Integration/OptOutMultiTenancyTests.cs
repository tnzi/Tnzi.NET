using Microsoft.Data.Sqlite;
using Tnzi.Security.Claims;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 多租户开启时退订名单的唯一性按租户划分：同一个地址可以分别在两个租户里退订。
/// </summary>
/// <remarks>
/// ★ 判重与发送前的判定都被租户过滤器限定在当前租户。唯一索引若是全局的，租户 B 看不见租户 A 的那一行，
/// 判重读到「还没有」，插入撞约束 —— 收件人在 B 里怎么都退订不掉。
/// </remarks>
public class OptOutMultiTenancyTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteConnection _connection;

    public OptOutMultiTenancyTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var provider = BuildProvider(tenantId: null);
        provider.GetRequiredService<NotificationTestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public void TheModel_ScopesBothUniqueIndexesToTheTenant()
    {
        using var provider = BuildProvider(TenantA);
        var dbContext = provider.GetRequiredService<NotificationTestDbContext>();
        dbContext.IsMultiTenancyEnabled.ShouldBeTrue();

        var unique = dbContext.Model.FindEntityType(typeof(OptOut))!.GetIndexes().Where(i => i.IsUnique).ToList();
        unique.Where(i => i.Properties[0].Name == nameof(OptOut.TenantId)).Select(i => i.Properties.Count).OrderBy(c => c).ToList()
            .ShouldBe(new List<int> { 3, 4 }, "the per-category and the channel-wide index both lead with TenantId");
    }

    [Fact]
    public async Task TheSameAddress_OptsOutInTwoTenants()
    {
        (await RegisterAsync(TenantA, "shared@example.com")).Succeeded.ShouldBeTrue();

        var inB = await RegisterAsync(TenantB, "shared@example.com");

        inB.Succeeded.ShouldBeTrue(inB.Message);
        using var provider = BuildProvider(TenantB);
        (await provider.GetRequiredService<INotificationOptOutService>()
            .IsOptedOutAsync("shared@example.com", NotificationType.Email)).ShouldBeTrue();
    }

    [Fact]
    public async Task WithinOneTenant_TheDatabaseStillRejectsADuplicate()
    {
        (await RegisterAsync(TenantA, "once@example.com")).Succeeded.ShouldBeTrue();

        using var provider = BuildProvider(TenantA);
        var dbContext = provider.GetRequiredService<NotificationTestDbContext>();
        dbContext.OptOuts.Add(new OptOut { Id = Guid.NewGuid(), TenantId = TenantA, Address = "once@example.com", Channel = NotificationType.Email });

        await Should.ThrowAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    /// <summary>宿主上下文登记的行（TenantId 为 NULL）照样一行一件事：NULL 在唯一索引里互不相等，靠单独那一对索引兜住。</summary>
    [Fact]
    public async Task HostRows_StillRejectADuplicate()
    {
        using var provider = BuildProvider(tenantId: null);
        var dbContext = provider.GetRequiredService<NotificationTestDbContext>();
        dbContext.OptOuts.Add(new OptOut { Id = Guid.NewGuid(), Address = "host@example.com", Channel = NotificationType.Email });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        dbContext.OptOuts.Add(new OptOut { Id = Guid.NewGuid(), Address = "host@example.com", Channel = NotificationType.Email });

        await Should.ThrowAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private async Task<Result<OptOutDto>> RegisterAsync(Guid tenantId, string address)
    {
        using var provider = BuildProvider(tenantId);
        return await provider.GetRequiredService<INotificationOptOutService>()
            .RegisterAsync(new CreateOptOutDto { Address = address, Channel = NotificationType.Email });
    }

    private ServiceProvider BuildProvider(Guid? tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(TestHelper.DefaultTestUserId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.TenantId).Returns(tenantId);
        services.AddScoped(_ => currentUser.Object);

        services.AddDbContext<NotificationTestDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
        });
        services.AddScoped<IRepository<OptOut, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, OptOut, Guid>(sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions());
        services.AddSingleton(options.Object);
        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
