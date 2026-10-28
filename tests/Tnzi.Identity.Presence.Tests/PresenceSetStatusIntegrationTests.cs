using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Identity.Presence.Entities.Configs;
using Tnzi.Identity.Presence.Events;
using Tnzi.Identity.Services;
using Tnzi.Security.Claims;
using Tnzi.TestBase;

namespace Tnzi.Identity.Presence.Tests;

/// <summary>
/// 手动切换状态（<see cref="IPresenceService.SetStatusAsync"/>）的写路径：真实 SQLite 仓储。
/// </summary>
/// <remarks>
/// <para>
/// ★ 隐身的泄露从来不是从状态字段漏出去的，而是从广播时机漏出去的（见 <see cref="PresenceDisclosure"/>）。
/// 连接生灭只在旁观者看到的状态真的变了时广播；手动切换此前无条件广播：已经隐身的人再点一次「隐身」，
/// 租户里的每个人都会收到一条「此人 Offline」的变更推送，恰好告诉他们这个人此刻就在。
/// </para>
/// <para>
/// ★ 状态是枚举，序列化器接受任意整数：0 或 99 此前原样落库，所有消费方都把它渲染成一个不认识的状态。
/// </para>
/// </remarks>
public class PresenceSetStatusIntegrationTests : IntegratedTestBase<PresenceTestDbContext>
{
    private static readonly Guid Me = TestHelper.DefaultTestUserId;

    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<IConnectionManager> _connections = new();

    public PresenceSetStatusIntegrationTests()
    {
        // 此刻连着：隐身之外的意图对旁观者都是看得见的。
        _connections.Setup(c => c.IsUserOnlineAsync(It.IsAny<Guid>())).ReturnsAsync(true);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<UserPresence, Guid>>(sp =>
            new EFCoreRepository<PresenceTestDbContext, UserPresence, Guid>(sp.GetRequiredService<PresenceTestDbContext>(), serviceProvider: sp));

        var options = new Mock<IOptionsSnapshot<PresenceOptions>>();
        options.Setup(o => o.Value).Returns(new PresenceOptions { AllowInvisible = true, AutoAwayEnabled = true });
        services.AddScoped(_ => options.Object);

        var scope = new Mock<IUserTenantScopeProvider>();
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Distinct().ToList());
        services.AddScoped(_ => scope.Object);

        services.AddSingleton(_eventBus.Object);
        services.AddSingleton(_connections.Object);
        services.AddScoped<IPresenceService>(sp => new PresenceService(
            sp,
            sp.GetRequiredService<IRepository<UserPresence, Guid>>(),
            sp.GetRequiredService<IOptionsSnapshot<PresenceOptions>>(),
            sp.GetRequiredService<IUserTenantScopeProvider>(),
            sp.GetRequiredService<IConnectionManager>()));
    }

    private IPresenceService Service => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IPresenceService>();

    private List<UserPresenceChangedEvent> Broadcasts()
        => _eventBus.Invocations
            .Where(i => i.Method.Name == nameof(IEventBus.PublishAsync))
            .Select(i => i.Arguments[0])
            .OfType<UserPresenceChangedEvent>()
            .ToList();

    private async Task<UserPresence> StoredAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<UserPresence>().AsNoTracking().SingleAsync(p => p.UserId == Me);
    }

    [Fact]
    public async Task Choosing_invisible_again_while_invisible_broadcasts_nothing()
    {
        (await Service.SetStatusAsync(UserPresenceStatus.Invisible)).Succeeded.ShouldBeTrue();
        var lastSeen = (await StoredAsync()).LastSeenAt;
        _eventBus.Invocations.Clear();

        (await Service.SetStatusAsync(UserPresenceStatus.Invisible)).Succeeded.ShouldBeTrue();

        Broadcasts().ShouldBeEmpty();
        (await StoredAsync()).LastSeenAt.ShouldBe(lastSeen);
    }

    [Fact]
    public async Task Going_invisible_from_online_is_broadcast_as_going_offline()
    {
        (await Service.SetStatusAsync(UserPresenceStatus.Online)).Succeeded.ShouldBeTrue();
        _eventBus.Invocations.Clear();

        (await Service.SetStatusAsync(UserPresenceStatus.Invisible)).Succeeded.ShouldBeTrue();

        Broadcasts().ShouldHaveSingleItem().Status.ShouldBe(UserPresenceStatus.Offline);
    }

    [Fact]
    public async Task Changing_a_visible_status_is_broadcast()
    {
        (await Service.SetStatusAsync(UserPresenceStatus.Online)).Succeeded.ShouldBeTrue();
        _eventBus.Invocations.Clear();

        (await Service.SetStatusAsync(UserPresenceStatus.Busy)).Succeeded.ShouldBeTrue();

        Broadcasts().ShouldHaveSingleItem().Status.ShouldBe(UserPresenceStatus.Busy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task An_undefined_status_is_refused_and_nothing_is_stored(int status)
    {
        var result = await Service.SetStatusAsync((UserPresenceStatus)status);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.Set<UserPresence>().AsNoTracking().AnyAsync()).ShouldBeFalse();
        Broadcasts().ShouldBeEmpty();
    }

    [Fact]
    public async Task Offline_is_not_a_manual_intent_and_reads_as_online()
    {
        (await Service.SetStatusAsync(UserPresenceStatus.Offline)).Succeeded.ShouldBeTrue();

        (await StoredAsync()).Status.ShouldBe(UserPresenceStatus.Online);
    }
}

/// <summary>只装 presence 一张表的测试上下文。</summary>
public class PresenceTestDbContext : TnziDbContext<PresenceTestDbContext>
{
    public PresenceTestDbContext(DbContextOptions<PresenceTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserPresenceConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
