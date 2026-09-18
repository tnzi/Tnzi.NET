using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 会话维护一次扫描要做齐三件事：清过期令牌、撤失活会话、清过期验证码。
/// </summary>
/// <remarks>
/// ★ 第三步此前不存在：<c>Identity_TwoFactorCode</c> 是全模块唯一没有任何清理路径的表，
/// 明文验证码 + 收件地址 + 用途无限期留存，为清理而建的 <c>ExpiresAt</c> 索引零消费者。
/// 这里断言的是<b>接线</b>（后台任务确实调了它），删对不对由集成测试负责。
/// </remarks>
public class SessionMaintenanceTests
{
    private readonly Mock<IAuthTokenService> _authTokens = new();
    private readonly Mock<ISessionRevocationService> _revocation = new();
    private readonly Mock<ITwoFactorService> _twoFactor = new();

    private SessionMaintenanceBackgroundService Build()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _authTokens.Object);
        services.AddScoped(_ => _revocation.Object);
        services.AddScoped(_ => _twoFactor.Object);
        var provider = services.BuildServiceProvider();

        var sessionOptions = new Mock<IOptionsMonitor<SessionOptions>>();
        sessionOptions.Setup(x => x.CurrentValue).Returns(new SessionOptions());
        var identityOptions = new Mock<IOptionsMonitor<IdentityOptions>>();
        identityOptions.Setup(x => x.CurrentValue).Returns(new IdentityOptions());

        return new SessionMaintenanceBackgroundService(
            provider, sessionOptions.Object, identityOptions.Object,
            new Mock<ILogger<SessionMaintenanceBackgroundService>>().Object);
    }

    [Fact]
    public async Task OneMaintenancePass_CleansExpiredVerificationCodes()
    {
        _twoFactor.Setup(x => x.CleanExpiredCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);

        await Build().RunMaintenanceAsync(CancellationToken.None);

        _twoFactor.Verify(x => x.CleanExpiredCodesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OneMaintenancePass_StillDoesTheTwoOriginalSteps()
    {
        await Build().RunMaintenanceAsync(CancellationToken.None);

        _authTokens.Verify(x => x.CleanExpiredTokensAsync(), Times.Once);
        _revocation.Verify(x => x.RevokeInactiveSessionsAsync(It.IsAny<TimeSpan>()), Times.Once);
    }
}
