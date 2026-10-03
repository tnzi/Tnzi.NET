using Tnzi.Security.Authorization;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 管理端对另一个账号的二次验证控制：一层租户范围检查，一条「不能替人登记身份验证器」的硬规则，
/// 其余交给 <see cref="ITwoFactorService"/>。
/// </summary>
public class UserTwoFactorAdminServiceTests
{
    private readonly Mock<ITwoFactorService> _twoFactor = new();
    private readonly Mock<IUserTenantScopeProvider> _scope = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Mock<IFunctionAuthorizationService> _functionAuthorization = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    public UserTwoFactorAdminServiceTests()
    {
        _scope.Setup(s => s.ContainsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _twoFactor.Setup(t => t.SuspendTwoFactorAsync(It.IsAny<Guid>())).ReturnsAsync(Result.Success());
        _twoFactor.Setup(t => t.ResumeTwoFactorAsync(It.IsAny<Guid>())).ReturnsAsync(Result.Success());
        _twoFactor.Setup(t => t.DisableTwoFactorAsync(It.IsAny<Guid>())).ReturnsAsync(Result.Success());
        _twoFactor.Setup(t => t.DisableTwoFactorMethodAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>())).ReturnsAsync(Result.Success());
        _twoFactor.Setup(t => t.SetPreferredTwoFactorAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>())).ReturnsAsync(Result.Success());
        _twoFactor.Setup(t => t.EnableTwoFactorAsync(It.IsAny<Guid>(), It.IsAny<EnableTwoFactorDto>())).ReturnsAsync(Result<string>.Success("ok"));
        _twoFactor.Setup(t => t.GetTwoFactorStatusAsync(It.IsAny<Guid>())).ReturnsAsync(Result<TwoFactorStatusDto>.Success(new TwoFactorStatusDto { IsEnabled = true }));
    }

    private UserTwoFactorAdminService CreateService()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        provider.Setup(p => p.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);
        _currentUser.SetupGet(c => c.Id).Returns(_actorId);
        return new UserTwoFactorAdminService(provider.Object, _twoFactor.Object, _scope.Object, _functionAuthorization.Object);
    }

    /// <summary>
    /// 持 <c>user.security</c> 的普通管理员摘掉超管的第二因子，再配一次重置密码就拿下了超管账号。
    /// </summary>
    [Fact]
    public async Task EveryWrite_OnASuperAdmin_ByANonSuperAdmin_IsForbiddenAndNeverReachesTheTwoFactorService()
    {
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(_userId)).ReturnsAsync(true);
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(_actorId)).ReturnsAsync(false);
        var service = CreateService();

        var results = new List<Result>
        {
            await service.SuspendAsync(_userId),
            await service.ResumeAsync(_userId),
            await service.EnableMethodAsync(_userId, TwoFactorType.Email),
            await service.DisableMethodAsync(_userId, TwoFactorType.Email),
            await service.SetPreferredAsync(_userId, TwoFactorType.Email),
            await service.ResetAsync(_userId),
        };

        Assert.All(results, r => Assert.Equal(403, r.Code));
        _twoFactor.VerifyNoOtherCalls();
    }

    /// <summary>防锈：超管对超管、任何管理员对普通账号照常放行。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Reset_IsAllowed_WhenTheTargetIsNotASuperAdminOrTheActorIsOne(bool targetIsSuperAdmin, bool actorIsSuperAdmin)
    {
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(_userId)).ReturnsAsync(targetIsSuperAdmin);
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(_actorId)).ReturnsAsync(actorIsSuperAdmin);

        var result = await CreateService().ResetAsync(_userId);

        Assert.True(result.Succeeded);
        _twoFactor.Verify(t => t.DisableTwoFactorAsync(_userId), Times.Once);
    }

    [Fact]
    public async Task GetStatus_DelegatesToTheTwoFactorService()
    {
        var result = await CreateService().GetStatusAsync(_userId);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsEnabled);
        _twoFactor.Verify(t => t.GetTwoFactorStatusAsync(_userId), Times.Once);
    }

    [Fact]
    public async Task EveryOperation_OutOfTenantScope_AnswersNotFoundAndNeverReachesTheTwoFactorService()
    {
        _scope.Setup(s => s.ContainsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = CreateService();

        var results = new List<Result>
        {
            await service.GetStatusAsync(_userId),
            await service.SuspendAsync(_userId),
            await service.ResumeAsync(_userId),
            await service.EnableMethodAsync(_userId, TwoFactorType.Email),
            await service.DisableMethodAsync(_userId, TwoFactorType.Email),
            await service.SetPreferredAsync(_userId, TwoFactorType.Email),
            await service.ResetAsync(_userId),
        };

        Assert.All(results, r => Assert.Equal(404, r.Code));
        _twoFactor.VerifyNoOtherCalls();
    }

    /// <summary>
    /// 别人能替你登记的第二因子不是第二因子。这一条在管理服务这一层就拒绝，
    /// 而不是等 <see cref="ITwoFactorService"/> 用一句说给自助用户听的话拒绝。
    /// </summary>
    [Fact]
    public async Task EnableMethod_Authenticator_IsRefusedBeforeReachingTheTwoFactorService()
    {
        var result = await CreateService().EnableMethodAsync(_userId, TwoFactorType.Totp);

        Assert.Equal(400, result.Code);
        Assert.Contains("account holder", result.Message);
        _twoFactor.Verify(t => t.EnableTwoFactorAsync(It.IsAny<Guid>(), It.IsAny<EnableTwoFactorDto>()), Times.Never);
    }

    /// <summary>
    /// Passkey 与验证码方式同一待遇：管理端可以把账号<strong>已经登记</strong>的安全密钥设成第二因子
    /// （没登记的由 ITwoFactorService 拒绝）；登记本身仍只能持有人自己做，与验证器 App 同理。
    /// </summary>
    [Theory]
    [InlineData(TwoFactorType.Sms)]
    [InlineData(TwoFactorType.Email)]
    [InlineData(TwoFactorType.Passkey)]
    public async Task EnableMethod_CodeBasedMethods_AreDelegated(TwoFactorType type)
    {
        var result = await CreateService().EnableMethodAsync(_userId, type);

        Assert.True(result.Succeeded);
        _twoFactor.Verify(t => t.EnableTwoFactorAsync(_userId, It.Is<EnableTwoFactorDto>(d => d.Type == type)), Times.Once);
    }

    [Fact]
    public async Task Suspend_Resume_Disable_Preferred_Reset_MapOntoTheMatchingTwoFactorOperations()
    {
        var service = CreateService();

        Assert.True((await service.SuspendAsync(_userId)).Succeeded);
        Assert.True((await service.ResumeAsync(_userId)).Succeeded);
        Assert.True((await service.DisableMethodAsync(_userId, TwoFactorType.Totp)).Succeeded);
        Assert.True((await service.SetPreferredAsync(_userId, TwoFactorType.Sms)).Succeeded);
        Assert.True((await service.ResetAsync(_userId)).Succeeded);

        _twoFactor.Verify(t => t.SuspendTwoFactorAsync(_userId), Times.Once);
        _twoFactor.Verify(t => t.ResumeTwoFactorAsync(_userId), Times.Once);
        _twoFactor.Verify(t => t.DisableTwoFactorMethodAsync(_userId, TwoFactorType.Totp), Times.Once);
        _twoFactor.Verify(t => t.SetPreferredTwoFactorAsync(_userId, TwoFactorType.Sms), Times.Once);
        _twoFactor.Verify(t => t.DisableTwoFactorAsync(_userId), Times.Once);
    }

    [Fact]
    public async Task Failures_FromTheTwoFactorService_ArePassedThroughUnchanged()
    {
        _twoFactor.Setup(t => t.ResumeTwoFactorAsync(_userId))
            .ReturnsAsync(Result.Failure("No two-factor method is configured", 400, ErrorCodes.VALIDATION_ERROR));

        var result = await CreateService().ResumeAsync(_userId);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal("No two-factor method is configured", result.Message);
    }
}
