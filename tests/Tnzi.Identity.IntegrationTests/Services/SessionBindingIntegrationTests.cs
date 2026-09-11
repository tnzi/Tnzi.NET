using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 每请求会话校验：设备特征绑定、闲置超时、绝对生命周期。
/// </summary>
/// <remarks>
/// ★ 跑<b>真实关系型 provider</b> 而不是 mock 仓储：这一组断言的核心是查询谓词与
/// <c>ExecuteUpdate</c> 真的按预期过滤/写入。mock 能证明「服务调用了仓储」，
/// 证明不了「那条 where 真的把过期会话挡在外面」，而后者才是这几条要守的东西。
/// </remarks>
public class SessionBindingIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private const string ChromeWin = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";
    private const string ChromeWinNewer = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36";
    private const string FirefoxLinux = "Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0";

    private DatabaseSessionService CreateService() => new(CreateRepository<UserSession>(), ServiceProvider);

    private async Task<UserSession> SeedSessionAsync(
        Guid userId,
        string? userAgent = ChromeWin,
        string? ip = "203.0.113.10",
        DateTime? lastActivity = null,
        DateTime? expiresAt = null,
        DateTime? absoluteExpiresAt = null)
    {
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserAgent = userAgent,
            IpAddress = ip,
            CreationTime = DateTime.UtcNow.AddHours(-1),
            LastActivityTime = lastActivity ?? DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            AbsoluteExpiresAt = absoluteExpiresAt,
            IsRevoked = false,
        };
        DbContext.UserSessions.Add(session);
        await SaveChangesAsync();
        return session;
    }

    /// <summary>同一浏览器 → 放行。</summary>
    [Fact]
    public async Task Validate_WithSameUserAgent_IsValid()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWin, "203.0.113.10"));

        Assert.Equal(SessionValidationResult.Valid, result);
    }

    /// <summary>
    /// ★★ 令牌被搬到另一个浏览器/系统上使用 → 判定为绑定不符。
    /// 这是这一整轮针对「令牌被偷走后在别处复用」的那一条最直接的防线。
    /// </summary>
    [Fact]
    public async Task Validate_WithDifferentBrowser_IsBindingMismatch()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(FirefoxLinux, "203.0.113.10"));

        Assert.Equal(SessionValidationResult.BindingMismatch, result);
    }

    /// <summary>
    /// ★ 对照组：浏览器自动升级（只有版本号变了）<b>不</b>算变。
    /// 少了这条，Chrome 每四周升一个大版本就会把用户成批踢下线，
    /// 而部署方的第一反应是把整个绑定关掉 —— 那才是真正的失效。
    /// </summary>
    [Fact]
    public async Task Validate_AfterBrowserVersionBump_IsStillValid()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWinNewer, "203.0.113.10"));

        Assert.Equal(SessionValidationResult.Valid, result);
    }

    /// <summary>会话建立时没采集到 UA（非浏览器客户端 / 存量会话）→ 无从比对，放行。</summary>
    [Fact]
    public async Task Validate_WhenStoredUserAgentMissing_IsValid()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id, userAgent: null);

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWin, "203.0.113.10"));

        Assert.Equal(SessionValidationResult.Valid, result);
    }

    /// <summary>
    /// ★ 地址变化默认只记录不拦：换 Wi-Fi、切蜂窝、VPN 都会触发，硬拦的误报率不可接受。
    /// 记录下来意味着会话上的地址被更新（同一次变化只会产生一条事件）。
    /// </summary>
    [Fact]
    public async Task Validate_WhenIpChanges_IsValidAndRecordsNewAddress()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWin, "198.51.100.7"));

        Assert.Equal(SessionValidationResult.Valid, result);

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.UserSessions.FindAsync(session.Id);
        Assert.Equal("198.51.100.7", stored!.IpAddress);
    }

    /// <summary>
    /// ★★ 绝对生命周期到点即失效，与活跃程度无关（ASVS 7.3.2）。
    /// 没有它，一条会话只要按期刷新就可以永远活着 —— 一次窃取换来永久访问。
    /// </summary>
    [Fact]
    public async Task Validate_AfterAbsoluteLifetime_IsInvalid()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(
            user.Id,
            lastActivity: DateTime.UtcNow,                       // 刚刚还活跃
            expiresAt: DateTime.UtcNow.AddDays(7),               // 滑动窗口也还没到
            absoluteExpiresAt: DateTime.UtcNow.AddMinutes(-1));  // 但绝对上限过了

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWin, "203.0.113.10"));

        Assert.Equal(SessionValidationResult.Invalid, result);
    }

    /// <summary>续期不得越过绝对上限。</summary>
    [Fact]
    public async Task Renew_DoesNotPushPastAbsoluteExpiry()
    {
        var user = await CreateUserAsync();
        var absolute = DateTime.UtcNow.AddHours(2);
        var session = await SeedSessionAsync(user.Id, absoluteExpiresAt: absolute);

        await CreateService().RenewSessionAsync(session.Id, DateTime.UtcNow.AddDays(7));

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.UserSessions.FindAsync(session.Id);
        Assert.NotNull(stored!.ExpiresAt);
        Assert.True(stored.ExpiresAt <= absolute,
            $"ExpiresAt {stored.ExpiresAt:o} must not exceed the absolute cap {absolute:o}");
    }

    /// <summary>活动时间按节流窗口续期，使闲置超时有数据可依。</summary>
    [Fact]
    public async Task Validate_RefreshesLastActivityTime()
    {
        var user = await CreateUserAsync();
        var stale = DateTime.UtcNow.AddMinutes(-10);
        var session = await SeedSessionAsync(user.Id, lastActivity: stale);

        await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWin, "203.0.113.10"));

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.UserSessions.FindAsync(session.Id);
        Assert.True(stored!.LastActivityTime > stale,
            "普通请求必须推进 LastActivityTime，否则闲置超时只会把正常使用的人按时踢掉");
    }

    /// <summary>已撤销的会话直接判失效。</summary>
    [Fact]
    public async Task Validate_WhenRevoked_IsInvalid()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);
        session.IsRevoked = true;
        await SaveChangesAsync();

        var result = await CreateService().ValidateAsync(
            session.Id, new SessionValidationContext(ChromeWin, "203.0.113.10"));

        Assert.Equal(SessionValidationResult.Invalid, result);
    }
}
