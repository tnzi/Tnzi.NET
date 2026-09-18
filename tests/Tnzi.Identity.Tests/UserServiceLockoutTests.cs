namespace Tnzi.Identity.Tests;

/// <summary>
/// 「停用 / 锁定 / 自助停用 / 自助注销」四条写 <c>LockoutEnd</c> 的路径。
/// </summary>
/// <remarks>
/// ★ Identity 的 <c>Set*Async</c> 系列失败是静默的 <c>IdentityResult</c>（UserValidator 不过、并发戳冲突），
/// 什么都不写。此前这四处都不看返回值：锁定没落库，却照样踢掉在线会话、发出「已停用」事件、答 200 ——
/// 管理员看到的是「已停用」，账号继续登录。与 <c>EnableAsync</c> 修过的那条是同一个原语的两面：
/// 那边是「报成功、账号仍锁着」，这边是「报成功、账号仍活着」。
/// </remarks>
public partial class UserServiceTests
{
    private static IdentityResult LockoutWriteFailure() =>
        IdentityResult.Failed(new IdentityError { Code = "InvalidUserName", Description = "User name is invalid." });

    private void SetupLockoutWriteFailure(User user)
    {
        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(LockoutWriteFailure());
    }

    private void SetupLockoutWriteSuccess(User user)
    {
        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(IdentityResult.Success);
    }

    private void VerifyNothingRevokedOrPublished<TEvent>() where TEvent : class, IEvent
    {
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisableAsync_WhenLockoutWriteFails_ReturnsFailure_AndRevokesAndPublishesNothing()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "legacy" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        SetupLockoutWriteFailure(user);

        var result = await _userService.DisableAsync(user.Id, "reason");

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_UPDATE_FAILED, result.ErrorCode);
        VerifyNothingRevokedOrPublished<UserDisabledEvent>();
    }

    /// <summary>武装失败时连 <c>LockoutEnd</c> 都不该去写：后者在未武装时必然失败，且顺序是契约。</summary>
    [Fact]
    public async Task DisableAsync_WhenArmingFails_StopsBeforeWritingLockoutEnd()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "legacy" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true)).ReturnsAsync(LockoutWriteFailure());

        var result = await _userService.DisableAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(It.IsAny<User>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        VerifyNothingRevokedOrPublished<UserDisabledEvent>();
    }

    [Fact]
    public async Task LockAsync_WhenLockoutWriteFails_ReturnsFailure_AndRevokesAndPublishesNothing()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "legacy" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        SetupLockoutWriteFailure(user);

        var result = await _userService.LockAsync(user.Id, DateTimeOffset.UtcNow.AddHours(1), "reason");

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_UPDATE_FAILED, result.ErrorCode);
        VerifyNothingRevokedOrPublished<UserLockedEvent>();
    }

    [Fact]
    public async Task DeactivateAccountAsync_WhenLockoutWriteFails_ReturnsFailure_AndRevokesAndPublishesNothing()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "legacy" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        SetupLockoutWriteFailure(user);

        var result = await _userService.DeactivateAccountAsync(user.Id, "leaving");

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_UPDATE_FAILED, result.ErrorCode);
        VerifyNothingRevokedOrPublished<UserAccountDeactivatedEvent>();
    }

    [Fact]
    public async Task DeactivateAccountAsync_WhenLockoutWriteSucceeds_RevokesSessions_AndPublishes()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "leaver" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        SetupLockoutWriteSuccess(user);

        var result = await _userService.DeactivateAccountAsync(user.Id, "leaving");

        Assert.True(result.Succeeded, result.Message);
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(user, true), Times.Once);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>()), Times.Once);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(user.Id, SessionRevocationReason.AccountDisabled, null),
            Times.Once);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<UserAccountDeactivatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// 锁定写不进去时账号不能被标成已删除：软删标记是在锁定之后才落的，
    /// 否则「注销失败」的回答背后是一条已经软删、却没锁的行。
    /// </summary>
    [Fact]
    public async Task DeleteAccountAsync_WhenLockoutWriteFails_ReturnsFailure_AndDoesNotSoftDelete()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "legacy" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        SetupLockoutWriteFailure(user);

        var result = await _userService.DeleteAccountAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_UPDATE_FAILED, result.ErrorCode);
        Assert.False(user.IsDeleted);
        _userManagerMock.Verify(x => x.UpdateAsync(It.IsAny<User>()), Times.Never);
        VerifyNothingRevokedOrPublished<UserAccountDeletedEvent>();
    }

    [Fact]
    public async Task DeleteAccountAsync_WhenLockoutWriteSucceeds_SoftDeletes_Revokes_AndPublishes()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "leaver" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        SetupLockoutWriteSuccess(user);
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _userService.DeleteAccountAsync(user.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(user.IsDeleted);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(user.Id, SessionRevocationReason.AccountDeleted, null),
            Times.Once);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<UserAccountDeletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
