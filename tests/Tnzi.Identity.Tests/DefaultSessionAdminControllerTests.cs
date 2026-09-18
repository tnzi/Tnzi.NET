using Tnzi.Identity.Controllers.Admin;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 管理端会话撤销必须走 <see cref="ISessionRevocationService"/>（撤会话 + 删刷新令牌 + 发事件），
/// 不能直接调 <see cref="ISessionService"/> 只翻会话行。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是「唯一出口被整体绕过」：<c>ISessionRevocationService</c> 的注释自称撤销的唯一出口，
/// 个人中心与登录链路都改走它了，而管理端三个撤销端点还在直接调 <c>ISessionService</c> ——
/// 令牌原样留在库里、<c>SessionsRevokedEvent</c> 不发；专为它准备的
/// <see cref="SessionRevocationReason.AdminRevoked"/> 全仓零生产调用。
/// <c>EnforceSessionValidation</c> 一关，管理员踢下线的设备照常续期。
/// </para>
/// <para>
/// 与 <c>SensitiveSelfServiceEndpointTests</c> 同一种门禁：断言的是<b>接线</b>，
/// 撤销服务本身对不对由 <c>SessionRevocationTests</c> 负责。
/// </para>
/// </remarks>
public class DefaultSessionAdminControllerTests
{
    private readonly Mock<ISessionService> _sessionService = new(MockBehavior.Strict);
    private readonly Mock<ISessionRevocationService> _revocation = new();
    private readonly DefaultSessionAdminController _controller;

    public DefaultSessionAdminControllerTests()
    {
        _controller = new DefaultSessionAdminController(_sessionService.Object, _revocation.Object);
    }

    [Fact]
    public async Task RevokeSession_GoesThroughTheRevocationExit_WithAdminRevoked()
    {
        var sessionId = Guid.NewGuid();
        _revocation.Setup(x => x.RevokeSessionAsync(sessionId, SessionRevocationReason.AdminRevoked)).ReturnsAsync(1);

        var result = await _controller.RevokeSession(sessionId);

        result.Success.ShouldBeTrue();
        _revocation.Verify(x => x.RevokeSessionAsync(sessionId, SessionRevocationReason.AdminRevoked), Times.Once);
        // Strict mock：任何对 ISessionService 的直接调用都会抛。
        _sessionService.VerifyNoOtherCalls();
    }

    /// <summary>撤销出口一条都没踢到（不存在 / 不在本租户范围内 / 早已撤销）→ 404，不泄露会话是否存在。</summary>
    [Fact]
    public async Task RevokeSession_WhenNothingWasRevoked_Returns404()
    {
        var sessionId = Guid.NewGuid();
        _revocation.Setup(x => x.RevokeSessionAsync(sessionId, SessionRevocationReason.AdminRevoked)).ReturnsAsync(0);

        var result = await _controller.RevokeSession(sessionId);

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task RevokeAllSessions_GoesThroughTheRevocationExit_WithAdminRevoked_AndKeepsTheExclusion()
    {
        var userId = Guid.NewGuid();
        var keep = Guid.NewGuid();
        _sessionService.Setup(x => x.GetUserSessionsAsync(userId, false))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Success([]));
        _revocation.Setup(x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.AdminRevoked, keep)).ReturnsAsync(2);

        var result = await _controller.RevokeAllSessions(userId, keep);

        result.Success.ShouldBeTrue();
        _revocation.Verify(x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.AdminRevoked, keep), Times.Once);
        _sessionService.Verify(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
    }

    /// <summary>目标用户不在管理员的租户范围内（会话服务答 404）→ 原样 404，且撤销出口一次都不碰。</summary>
    [Fact]
    public async Task RevokeAllSessions_WhenTheUserIsOutOfScope_Returns404_WithoutTouchingTheExit()
    {
        var userId = Guid.NewGuid();
        _sessionService.Setup(x => x.GetUserSessionsAsync(userId, false))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Failure("User not found", 404));

        var result = await _controller.RevokeAllSessions(userId, null);

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe(404);
        _revocation.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanExpired_GoesThroughTheRevocationExit()
    {
        _revocation.Setup(x => x.RevokeInactiveSessionsAsync(TimeSpan.FromMinutes(45))).ReturnsAsync(3);

        var result = await _controller.CleanExpired(45);

        result.Success.ShouldBeTrue();
        result.Data.ShouldBe(3);
        _sessionService.Verify(x => x.CleanExpiredSessionsAsync(It.IsAny<TimeSpan>()), Times.Never);
    }

    /// <summary>
    /// ★ 阈值 0（或负数）让「清理失活会话」变成「撤销当前全部会话并删光刷新令牌」——
    /// 那是另一个动作，不该藏在一个查询参数里；低于一分钟一律 400，出口一次都不调。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CleanExpired_WithAThresholdBelowOneMinute_Returns400_WithoutTouchingTheExit(int inactiveMinutes)
    {
        var result = await _controller.CleanExpired(inactiveMinutes);

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _revocation.Verify(x => x.RevokeInactiveSessionsAsync(It.IsAny<TimeSpan>()), Times.Never);
        _sessionService.Verify(x => x.CleanExpiredSessionsAsync(It.IsAny<TimeSpan>()), Times.Never);
    }

    /// <summary>
    /// 约定门禁：本模块任何控制器一旦直接调 <c>ISessionService</c> 的撤销方法，
    /// 就必须同时持有 <c>ISessionRevocationService</c>（只允许作为它缺席时的退路）。
    /// 新写一个撤销端点而忘了出口，这条会红。
    /// </summary>
    [Fact]
    public void AnyIdentityControllerThatRevokesSessions_MustHoldTheRevocationExit()
    {
        var offenders = new List<string>();
        foreach (var file in RepoScan.EnumerateFiles("src/Tnzi.Identity/Controllers", "*.cs"))
        {
            var text = File.ReadAllText(file);
            var revokesDirectly = text.Contains("SessionService.RevokeSessionAsync(", StringComparison.Ordinal)
                || text.Contains("SessionService.RevokeAllSessionsAsync(", StringComparison.Ordinal)
                || text.Contains("SessionService.CleanExpiredSessionsAsync(", StringComparison.Ordinal);
            if (revokesDirectly && !text.Contains("ISessionRevocationService", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.ShouldBeEmpty(
            "these controllers revoke sessions without ISessionRevocationService (tokens stay alive, no event): "
            + string.Join(", ", offenders));
    }
}
