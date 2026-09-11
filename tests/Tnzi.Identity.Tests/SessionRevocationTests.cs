using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 会话撤销出口，以及「哪些动作必须触发撤销」。
/// </summary>
/// <remarks>
/// 这一组守的是同一条缺陷的两半：
/// <list type="number">
/// <item>撤销会话时<b>必须连带删掉刷新令牌</b>。此前只改了会话行的 <c>IsRevoked</c>，
/// 令牌原样留在库里 —— 一旦 <c>EnforceSessionValidation</c> 这个逃生开关被关掉，
/// 所有撤销动作一起变成装饰，而外观、日志、接口返回没有任何区别。</item>
/// <item>停用账号、改密码、重置密码<b>必须触发撤销</b>。此前一个都不触发：
/// 管理员点了停用、用户改了密码，攻击者手里的令牌照常有效。</item>
/// </list>
/// </remarks>
public class SessionRevocationTests
{
    private readonly Mock<ISessionService> _sessionServiceMock = new();
    private readonly Mock<IAuthTokenService> _authTokenServiceMock = new();
    private readonly Mock<IEventBus> _eventBusMock = new();
    private readonly SessionRevocationService _service;

    public SessionRevocationTests()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessionServiceMock.Setup(x => x.RevokeSessionAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success());
        _sessionServiceMock.Setup(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result.Success());
        _sessionServiceMock.Setup(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Success([]));
        _sessionServiceMock.Setup(x => x.GetSessionAsync(It.IsAny<Guid>()))
            .ReturnsAsync((UserSessionDto?)null);

        _service = new SessionRevocationService(
            serviceProvider.Object,
            _sessionServiceMock.Object,
            _authTokenServiceMock.Object,
            _eventBusMock.Object);
    }

    /// <summary>★ 撤单条会话 = 撤会话 + 删该会话绑定的刷新令牌。</summary>
    [Fact]
    public async Task RevokeSession_AlsoRemovesTheBoundRefreshTokens()
    {
        var sessionId = Guid.NewGuid();

        await _service.RevokeSessionAsync(sessionId, SessionRevocationReason.Logout);

        _sessionServiceMock.Verify(x => x.RevokeSessionAsync(sessionId), Times.Once);
        _authTokenServiceMock.Verify(
            x => x.RemoveSessionTokensAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(sessionId))),
            Times.Once);
    }

    /// <summary>
    /// ★ 会话此前已被撤销（<c>RevokeSessionAsync</c> 返回失败）时，<b>令牌照删不误</b>。
    /// 那正是最需要清理的情形之一：上一次撤销只改了会话行，令牌还留着。
    /// </summary>
    [Fact]
    public async Task RevokeSession_WhenAlreadyRevoked_StillRemovesTokens()
    {
        var sessionId = Guid.NewGuid();
        _sessionServiceMock.Setup(x => x.RevokeSessionAsync(sessionId))
            .ReturnsAsync(Result.Failure("Session already revoked", 400));

        await _service.RevokeSessionAsync(sessionId, SessionRevocationReason.AdminRevoked);

        _authTokenServiceMock.Verify(
            x => x.RemoveSessionTokensAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(sessionId))),
            Times.Once);
    }

    /// <summary>撤全部时按用户删令牌，并把要保留的会话排除在外。</summary>
    [Fact]
    public async Task RevokeUserSessions_PassesExclusionToBothSides()
    {
        var userId = Guid.NewGuid();
        var keep = Guid.NewGuid();

        await _service.RevokeUserSessionsAsync(userId, SessionRevocationReason.PasswordChanged, keep);

        _sessionServiceMock.Verify(x => x.RevokeAllSessionsAsync(userId, keep), Times.Once);
        _authTokenServiceMock.Verify(x => x.RemoveUserSessionTokensAsync(userId, keep), Times.Once);
    }

    /// <summary>
    /// ★ 后台清扫也要连带删令牌。此前它只调 <c>CleanExpiredSessionsAsync</c>（只改会话行），
    /// 阈值收窄到闲置超时之后，那样会造出一批「会话已撤销、刷新令牌还活着」的记录。
    /// </summary>
    [Fact]
    public async Task RevokeInactiveSessions_RemovesTheBoundTokensToo()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _sessionServiceMock
            .Setup(x => x.CleanInactiveSessionsAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(Result<IReadOnlyCollection<Guid>>.Success(ids));

        var count = await _service.RevokeInactiveSessionsAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(2, count);
        _authTokenServiceMock.Verify(
            x => x.RemoveSessionTokensAsync(It.Is<IReadOnlyCollection<Guid>>(c => c.Count == 2)),
            Times.Once);
    }

    /// <summary>没有会话可清扫时不做任何事（也不发一条空的删除）。</summary>
    [Fact]
    public async Task RevokeInactiveSessions_WithNothingToClean_DoesNothing()
    {
        _sessionServiceMock
            .Setup(x => x.CleanInactiveSessionsAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(Result<IReadOnlyCollection<Guid>>.Success(Array.Empty<Guid>()));

        var count = await _service.RevokeInactiveSessionsAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(0, count);
        _authTokenServiceMock.Verify(
            x => x.RemoveSessionTokensAsync(It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
    }

    /// <summary>Guid.Empty 不是一条会话（那是「未绑定会话」的标记），直接返回 0 且不做任何事。</summary>
    [Fact]
    public async Task RevokeSession_WithEmptyId_DoesNothing()
    {
        var count = await _service.RevokeSessionAsync(Guid.Empty, SessionRevocationReason.Logout);

        Assert.Equal(0, count);
        _sessionServiceMock.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>()), Times.Never);
        _authTokenServiceMock.Verify(
            x => x.RemoveSessionTokensAsync(It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
    }
}

/// <summary>
/// 「哪些动作必须触发撤销」的路径级用例。
/// </summary>
public class PasswordChangeRevocationTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<ISessionRevocationService> _revocationMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly PasswordService _passwordService;

    public PasswordChangeRevocationTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var identityOptions = new Mock<IOptionsSnapshot<IdentityOptions>>();
        identityOptions.Setup(x => x.Value).Returns(new IdentityOptions());

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _passwordService = new PasswordService(
            _userManagerMock.Object,
            identityOptions.Object,
            serviceProvider.Object,
            eventBus: new Mock<IEventBus>().Object,
            currentUser: _currentUserMock.Object,
            sessionRevocation: _revocationMock.Object);
    }

    /// <summary>
    /// ★★ 本人改密：撤销<b>其它</b>会话，保留当前这一条（ASVS 7.4.3）。
    /// 连自己一起踢掉的按钮用户不会点，那条自救路径就等于不存在。
    /// </summary>
    [Fact]
    public async Task ChangePassword_RevokesOtherSessionsButKeepsCurrent()
    {
        var userId = Guid.NewGuid();
        var currentSession = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", PasswordHash = "hash" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ChangePasswordAsync(user, "old", "new"))
            .ReturnsAsync(IdentityResult.Success);
        _currentUserMock.Setup(x => x.FindClaim(IdentityConstants.ClaimTypeNames.SessionId))
            .Returns(currentSession.ToString());

        var result = await _passwordService.ChangePasswordAsync(userId, "old", "new");

        Assert.True(result.Succeeded);
        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.PasswordChanged, currentSession),
            Times.Once);
    }

    /// <summary>
    /// ★★ 管理员重置密码：全撤，不保留任何会话 —— 被重置的那个人不是当前会话的主人。
    /// </summary>
    [Fact]
    public async Task AdminResetPassword_RevokesEverySession()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", PasswordHash = "hash" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, "reset-token", "new"))
            .ReturnsAsync(IdentityResult.Success);
        // 管理员重置默认把密码标成临时的（下次登录必须改），那一步要写实体。
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _passwordService.ResetPasswordByAdminAsync(userId, "new");

        Assert.True(result.Succeeded);
        // ★ 默认把密码标成临时的：管理员设的密码是经带外通道递过去的，那条通道上谁都可能看见。
        Assert.True(user.HasPendingAction(PendingUserActions.ChangePassword));
        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.PasswordReset, null),
            Times.Once);
    }

    /// <summary>
    /// ★★ 共享出口默认全撤：走到这条路的密码要么是别人给的、要么是被找回 / 到期的。
    /// </summary>
    [Fact]
    public async Task ForceSetPassword_RevokesEverySessionByDefault()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "u", PasswordHash = "hash" };

        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, "reset-token", "new"))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _passwordService.ForceSetPasswordAsync(user, "new");

        Assert.True(result.Succeeded);
        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(user.Id, SessionRevocationReason.PasswordReset, null),
            Times.Once);
    }

    /// <summary>
    /// ★ 唯一的例外：账号此前没有任何密码（快速注册后设第一个密码）。
    /// 没有旧凭据要作废，而唯一在线的会话正是本人刚凭验证码换来的那一条。
    /// </summary>
    [Fact]
    public async Task ForceSetPassword_WhenAskedNotTo_LeavesSessionsAlone()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "u" };

        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, "reset-token", "new"))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _passwordService.ForceSetPasswordAsync(user, "new", revokeExistingSessions: false);

        Assert.True(result.Succeeded);
        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>对照组：改密失败时不撤销任何会话。</summary>
    [Fact]
    public async Task ChangePassword_WhenItFails_RevokesNothing()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ChangePasswordAsync(user, "wrong", "new"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "bad password" }));

        var result = await _passwordService.ChangePasswordAsync(userId, "wrong", "new");

        Assert.False(result.Succeeded);
        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }
}
