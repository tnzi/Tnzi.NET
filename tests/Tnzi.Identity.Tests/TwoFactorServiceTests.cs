
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class TwoFactorServiceTests
{
    private readonly Mock<IRepository<TwoFactorCode, Guid>> _repositoryMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IOptionsSnapshot<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<ILogger<TwoFactorService>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly TwoFactorService _twoFactorService;

    public TwoFactorServiceTests()
    {
        _repositoryMock = new Mock<IRepository<TwoFactorCode, Guid>>();

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        // 按方式 2FA 通过 UpdateAsync(user) 持久化 flag/聚合(取代旧的 SetTwoFactorEnabledAsync)。
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock = new Mock<IEventBus>();
        _identityOptionsMock = new Mock<IOptionsSnapshot<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Otp = new OtpOptions
            {
                EnableSms = true,
                EnableEmail = true,
                CodeLength = 6,
                ExpirationMinutes = 5,
                ResendIntervalSeconds = 60
            }
        });
        _loggerMock = new Mock<ILogger<TwoFactorService>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _twoFactorService = new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _identityOptionsMock.Object
        );
    }

    /// <summary>Build a service whose OtpOptions snapshot is the supplied instance.</summary>
    private TwoFactorService CreateServiceWithOtp(OtpOptions otp)
    {
        var optionsMock = new Mock<IOptionsSnapshot<IdentityOptions>>();
        optionsMock.Setup(x => x.Value).Returns(new IdentityOptions { Otp = otp });
        return new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            optionsMock.Object);
    }

    [Fact]
    public async Task SendSmsCodeAsync_WhenSmsDisabled_ReturnsFalse()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Otp = new OtpOptions { EnableSms = false }
        });

        var service = new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _identityOptionsMock.Object
        );

        // Act
        var result = await service.SendSmsCodeAsync(Guid.NewGuid(), "13800138000");

        // Assert
        Assert.False(result.Succeeded);
    }

    #region GetTotpSetupInfoAsync

    [Fact]
    public async Task GetTotpSetupInfoAsync_WithValidUser_ReturnsSetupInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        const string rawKey = "JBSWY3DPEHPK3PXP";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(rawKey);
        _userManagerMock.Setup(x => x.GetEmailAsync(user))
            .ReturnsAsync(user.Email);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.SharedKey);
        Assert.Contains("otpauth://totp/", result.Data.AuthenticatorUri);
        Assert.Contains(rawKey, result.Data.AuthenticatorUri);

        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        _userManagerMock.Verify(x => x.GetAuthenticatorKeyAsync(user), Times.Once);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_WhenKeyGenerationFails_ReturnsInternalError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        // GetAuthenticatorKeyAsync returns null/empty to simulate generation failure
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_WhenTotpDisabled_ReturnsError()
    {
        // Arrange: deployment turned the authenticator (TOTP) channel off.
        var service = CreateServiceWithOtp(new OtpOptions { EnableTotp = false });

        // Act
        var result = await service.GetTotpSetupInfoAsync(Guid.NewGuid());

        // Assert: rejected before touching the user (mirrors SMS/Email channel-off).
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_FormatsSharedKeyWithSpaces()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        // Key longer than 4 chars so FormatKey inserts spaces
        const string rawKey = "JBSWY3DPEHPK3PXP";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(rawKey);
        _userManagerMock.Setup(x => x.GetEmailAsync(user))
            .ReturnsAsync(user.Email);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        // FormatKey inserts a space every 4 characters
        Assert.Contains(" ", result.Data!.SharedKey);
    }

    #endregion

    #region EnableTotpAsync

    [Fact]
    public async Task EnableTotpAsync_WhenTotpDisabled_ReturnsError()
    {
        // Arrange: deployment turned the authenticator (TOTP) channel off.
        var service = CreateServiceWithOtp(new OtpOptions { EnableTotp = false });

        // Act
        var result = await service.EnableTotpAsync(Guid.NewGuid(), "123456");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _userManagerMock.Verify(x => x.VerifyTwoFactorTokenAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnableTotpAsync_WithValidCode_EnablesTwoFactor()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        const string verificationCode = "123456";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
                user, It.IsAny<string>(), verificationCode))
            .ReturnsAsync(true);

        // Act
        var result = await _twoFactorService.EnableTotpAsync(userId, verificationCode);

        // Assert: per-method model sets the TOTP flag + aggregate TwoFactorEnabled
        // and persists via UpdateAsync (no longer SetTwoFactorEnabledAsync).
        Assert.True(result.Succeeded);
        Assert.True(user.AuthenticatorTwoFactorEnabled);
        Assert.True(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorType.Totp, user.PreferredTwoFactorType); // first enabled → default preferred
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.AtLeastOnce);
    }

    [Fact]
    public async Task EnableTotpAsync_WithInvalidCode_ReturnsFail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        const string verificationCode = "000000";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
                user, It.IsAny<string>(), verificationCode))
            .ReturnsAsync(false);

        // Act
        var result = await _twoFactorService.EnableTotpAsync(userId, verificationCode);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        // SetTwoFactorEnabledAsync must NOT be called when verification fails
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(It.IsAny<User>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task EnableTotpAsync_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _twoFactorService.EnableTotpAsync(userId, "123456");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region DisableTotpAsync

    [Fact]
    public async Task DisableTotpAsync_WhenOnlyMethod_TurnsOffTwoFactor()
    {
        // Arrange: TOTP is the only enabled method (per-method model).
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            AuthenticatorTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Totp,
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert: key reset, flag cleared, aggregate off, preferred cleared.
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        Assert.False(user.AuthenticatorTwoFactorEnabled);
        Assert.False(user.TwoFactorEnabled);
        Assert.Null(user.PreferredTwoFactorType);
    }

    [Fact]
    public async Task DisableTotpAsync_WithAnotherMethodEnabled_KeepsTwoFactorEnabled()
    {
        // Arrange: SMS is ALSO enabled (per-method), so disabling TOTP keeps 2FA on.
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            PhoneNumber = "13800138000",
            PhoneNumberConfirmed = true,
            SmsTwoFactorEnabled = true,
            AuthenticatorTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Totp,
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert: TOTP off, SMS still on → aggregate stays on; preferred moves off TOTP.
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        Assert.False(user.AuthenticatorTwoFactorEnabled);
        Assert.True(user.SmsTwoFactorEnabled);
        Assert.True(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorType.Sms, user.PreferredTwoFactorType);
    }

    [Fact]
    public async Task SuspendTwoFactorAsync_TurnsMasterOff_ButKeepsConfiguredMethodsAndKey()
    {
        // Arrange: TOTP + email configured, master on.
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "test@example.com",
            EmailConfirmed = true,
            AuthenticatorTwoFactorEnabled = true,
            EmailTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Totp,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        // Act
        var result = await _twoFactorService.SuspendTwoFactorAsync(userId);

        // Assert: master off, but every per-method flag + preferred preserved, and the
        // authenticator key is NOT reset (resume must not require re-scanning).
        Assert.True(result.Succeeded);
        Assert.False(user.TwoFactorEnabled);
        Assert.True(user.AuthenticatorTwoFactorEnabled);
        Assert.True(user.EmailTwoFactorEnabled);
        Assert.Equal(TwoFactorType.Totp, user.PreferredTwoFactorType);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ResumeTwoFactorAsync_WithConfiguredMethods_TurnsMasterBackOn()
    {
        // Arrange: suspended state - methods configured (flags on) but master off.
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            AuthenticatorTwoFactorEnabled = true,
            EmailTwoFactorEnabled = true,
            TwoFactorEnabled = false,
            PreferredTwoFactorType = TwoFactorType.Totp,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        // Act
        var result = await _twoFactorService.ResumeTwoFactorAsync(userId);

        // Assert: master back on, saved config intact.
        Assert.True(result.Succeeded);
        Assert.True(user.TwoFactorEnabled);
        Assert.True(user.AuthenticatorTwoFactorEnabled);
        Assert.True(user.EmailTwoFactorEnabled);
        Assert.Equal(TwoFactorType.Totp, user.PreferredTwoFactorType);
    }

    [Fact]
    public async Task ResumeTwoFactorAsync_WithNoConfiguredMethods_ReturnsFailure()
    {
        // Arrange: nothing configured → nothing to resume.
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", TwoFactorEnabled = false };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        // Act
        var result = await _twoFactorService.ResumeTwoFactorAsync(userId);

        // Assert: rejected, master stays off.
        Assert.False(result.Succeeded);
        Assert.False(user.TwoFactorEnabled);
    }

    [Fact]
    public async Task DisableTotpAsync_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(It.IsAny<User>()), Times.Never);
    }

    #endregion

    #region 按方式启用/禁用 + 首选

    [Fact]
    public async Task EnableTwoFactorAsync_Sms_WithConfirmedPhone_EnablesSmsMethod()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            PhoneNumber = "13800138000",
            PhoneNumberConfirmed = true,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        var result = await _twoFactorService.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = TwoFactorType.Sms });

        Assert.True(result.Succeeded);
        Assert.True(user.SmsTwoFactorEnabled);
        Assert.False(user.AuthenticatorTwoFactorEnabled);
        Assert.True(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorType.Sms, user.PreferredTwoFactorType);
    }

    [Fact]
    public async Task EnableTwoFactorAsync_Sms_WithoutConfirmedPhone_Fails()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", PhoneNumberConfirmed = false };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        var result = await _twoFactorService.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = TwoFactorType.Sms });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.False(user.SmsTwoFactorEnabled);
    }

    [Fact]
    public async Task EnableTwoFactorAsync_Totp_IsRejected()
    {
        // TOTP must go through the setup + verify flow, not the per-method enable.
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        var result = await _twoFactorService.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = TwoFactorType.Totp });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task DisableTwoFactorMethodAsync_Sms_KeepsOtherMethodsEnabled()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            SmsTwoFactorEnabled = true,
            EmailTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Sms,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        var result = await _twoFactorService.DisableTwoFactorMethodAsync(userId, TwoFactorType.Sms);

        Assert.True(result.Succeeded);
        Assert.False(user.SmsTwoFactorEnabled);
        Assert.True(user.EmailTwoFactorEnabled);
        Assert.True(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorType.Email, user.PreferredTwoFactorType); // moved off disabled Sms
    }

    [Fact]
    public async Task SetPreferredTwoFactorAsync_RequiresAnEnabledMethod()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            EmailTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Email,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        // Sms is not enabled → rejected.
        var reject = await _twoFactorService.SetPreferredTwoFactorAsync(userId, TwoFactorType.Sms);
        Assert.False(reject.Succeeded);
        Assert.Equal(400, reject.Code);

        // Email is enabled → accepted.
        var ok = await _twoFactorService.SetPreferredTwoFactorAsync(userId, TwoFactorType.Email);
        Assert.True(ok.Succeeded);
        Assert.Equal(TwoFactorType.Email, user.PreferredTwoFactorType);
    }

    [Fact]
    public async Task GetTwoFactorStatusAsync_ReturnsPerMethodState()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "test@example.com",
            EmailConfirmed = true,
            EmailTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Email,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        var result = await _twoFactorService.GetTwoFactorStatusAsync(userId);

        Assert.True(result.Succeeded);
        var status = result.Data!;
        Assert.True(status.IsEnabled);
        Assert.Equal(TwoFactorType.Email, status.PreferredType);
        var email = status.Methods.Single(m => m.Type == TwoFactorType.Email);
        Assert.True(email.Available);
        Assert.True(email.Enabled);
        Assert.True(email.IsPreferred);
        var totp = status.Methods.Single(m => m.Type == TwoFactorType.Totp);
        Assert.True(totp.Available);   // TOTP can be set up (EnableTotp defaults to true)
        Assert.False(totp.Enabled);
    }

    [Fact]
    public async Task GetTwoFactorStatusAsync_WhenTotpDisabled_OmitsTotpMethod()
    {
        // Deployment turned TOTP off; the user has no authenticator enrolled →
        // the status must not surface a TOTP method (so the User Center hides it).
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "test@example.com",
            EmailConfirmed = true,
            EmailTwoFactorEnabled = true,
            TwoFactorEnabled = true,
            PreferredTwoFactorType = TwoFactorType.Email,
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        var service = CreateServiceWithOtp(new OtpOptions { EnableTotp = false, EnableEmail = true });

        var result = await service.GetTwoFactorStatusAsync(userId);

        Assert.True(result.Succeeded);
        var status = result.Data!;
        Assert.DoesNotContain(status.Methods, m => m.Type == TwoFactorType.Totp);
        Assert.Contains(status.Methods, m => m.Type == TwoFactorType.Email);
    }

    [Fact]
    public async Task GetEnabledTwoFactorTypesAsync_LegacyUser_FallsBackToAvailable()
    {
        // Legacy: TwoFactorEnabled true but no per-method flags → effective = available.
        var user = new User
        {
            UserName = "legacy",
            Email = "legacy@example.com",
            EmailConfirmed = true,
            TwoFactorEnabled = true,
        };
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync((string?)null);

        var types = await _twoFactorService.GetEnabledTwoFactorTypesAsync(user);

        Assert.Contains(TwoFactorType.Email, types);
        Assert.DoesNotContain(TwoFactorType.Totp, types); // no authenticator key configured
    }

    [Fact]
    public async Task GetEnabledTwoFactorTypesAsync_ExcludesMethodWhoseChannelIsDisabled()
    {
        // Email + TOTP both enabled per-method, but the deployment disabled the TOTP
        // channel → login must no longer offer TOTP (user can still use email).
        var user = new User
        {
            UserName = "user",
            Email = "user@example.com",
            EmailConfirmed = true,
            EmailTwoFactorEnabled = true,
            AuthenticatorTwoFactorEnabled = true,
            TwoFactorEnabled = true,
        };
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync("KEY"); // key exists
        var service = CreateServiceWithOtp(new OtpOptions { EnableEmail = true, EnableTotp = false });

        var types = await service.GetEnabledTwoFactorTypesAsync(user);

        Assert.Contains(TwoFactorType.Email, types);
        Assert.DoesNotContain(TwoFactorType.Totp, types); // channel off → filtered out
    }

    [Fact]
    public async Task GetEnabledTwoFactorTypesAsync_WhenAllEnabledChannelsDisabled_ReturnsEmpty()
    {
        // TOTP enrolled + enabled, but the deployment disabled the TOTP channel and
        // there is no other usable method → empty set → caller treats as 2FA off.
        var user = new User
        {
            UserName = "user",
            AuthenticatorTwoFactorEnabled = true,
            TwoFactorEnabled = true,
        };
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync("KEY");
        var service = CreateServiceWithOtp(new OtpOptions { EnableSms = false, EnableEmail = false, EnableTotp = false });

        var types = await service.GetEnabledTwoFactorTypesAsync(user);

        Assert.Empty(types);
    }

    #endregion
}
