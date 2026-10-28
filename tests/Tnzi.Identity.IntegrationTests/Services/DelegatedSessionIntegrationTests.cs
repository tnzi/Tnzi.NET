using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 委派会话落库：分组键、显示名、声明与绝对到期时间真的写进了会话表，并且读得回来。
/// </summary>
/// <remarks>
/// ★ 跑真实关系型 provider：刷新时声明全凭从这张表读回，mock 仓储证明不了那三列真的被写入、
/// 也证明不了「普通会话读回 null」那条谓词真的按 <c>GroupKey</c> 过滤。
/// </remarks>
public class DelegatedSessionIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private const string UserAgent = "PortableClient/1.0 (Windows NT 10.0)";

    public DelegatedSessionIntegrationTests()
        : base(configureServices: services => services.Configure<SessionOptions>(o => o.AbsoluteLifetimeHours = 24))
    {
    }

    private DatabaseSessionService CreateService()
        => new(CreateRepository<UserSession>(), ServiceProvider, new UserTenantScopeProvider(CreateRepository<User>()));

    private static DelegatedSessionCreation Creation(Guid userId, DateTime absoluteExpiresAt) => new(
        userId,
        "PortableClient",
        "198.51.100.20",
        UserAgent,
        absoluteExpiresAt,
        new DelegatedSessionInfo(
            "pass:p1:d1",
            "Library computer",
            new Dictionary<string, string> { ["app_pass"] = "p1", ["app_pass_device"] = "d1" },
            absoluteExpiresAt));

    [Fact]
    public async Task ADelegatedSession_RoundTripsItsDelegation()
    {
        var user = await CreateUserAsync();
        var expiresAt = DateTime.UtcNow.AddHours(2);
        var service = CreateService();

        var sessionId = await service.CreateDelegatedSessionAsync(Creation(user.Id, expiresAt));
        DbContext.ChangeTracker.Clear();
        var delegation = await CreateService().GetDelegationAsync(sessionId);

        Assert.NotNull(delegation);
        Assert.Equal("pass:p1:d1", delegation.GroupKey);
        Assert.Equal("Library computer", delegation.Label);
        Assert.Equal(new Dictionary<string, string> { ["app_pass"] = "p1", ["app_pass_device"] = "d1" }, delegation.Claims);
        Assert.Equal(expiresAt, delegation.AbsoluteExpiresAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>部署配置的绝对上限更早时以它为准：请求 3 天、配置 24 小时，落库的是 24 小时。</summary>
    [Fact]
    public async Task TheConfiguredAbsoluteLifetime_WinsWhenItIsEarlier()
    {
        var user = await CreateUserAsync();

        var sessionId = await CreateService().CreateDelegatedSessionAsync(Creation(user.Id, DateTime.UtcNow.AddDays(3)));
        DbContext.ChangeTracker.Clear();
        var delegation = await CreateService().GetDelegationAsync(sessionId);

        Assert.NotNull(delegation);
        Assert.Equal(DateTime.UtcNow.AddHours(24), delegation.AbsoluteExpiresAt, TimeSpan.FromMinutes(1));
        var row = await DbContext.Set<UserSession>().AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.True(row.ExpiresAt <= row.AbsoluteExpiresAt);
        Assert.Equal(UserAgent, row.UserAgent);
    }

    [Fact]
    public async Task AnOrdinarySession_HasNoDelegation()
    {
        var user = await CreateUserAsync();
        var sessionId = await CreateService().CreateSessionAsync(user.Id, "Chrome", "203.0.113.10", UserAgent, DateTime.UtcNow.AddDays(7));

        Assert.Null(await CreateService().GetDelegationAsync(sessionId));
    }

    /// <summary>用户自己的会话列表里能认出委派会话：分组键与显示名都在列表项上。</summary>
    [Fact]
    public async Task TheSessionList_MarksDelegatedSessions()
    {
        var user = await CreateUserAsync();
        var service = CreateService();
        var signInId = await service.CreateSessionAsync(user.Id, "Chrome", "203.0.113.10", UserAgent, DateTime.UtcNow.AddDays(7));
        var delegatedId = await service.CreateDelegatedSessionAsync(Creation(user.Id, DateTime.UtcNow.AddHours(2)));

        var list = (await CreateService().GetUserSessionsAsync(user.Id)).Data!.ToList();

        var delegated = list.Single(s => s.Id == delegatedId);
        Assert.Equal("pass:p1:d1", delegated.GroupKey);
        Assert.Equal("Library computer", delegated.Label);
        var signIn = list.Single(s => s.Id == signInId);
        Assert.Null(signIn.GroupKey);
        Assert.Null(signIn.Label);
    }
}
