
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class TwoFactorServiceTests
{
    private readonly Mock<IRepository<TwoFactorCode, Guid>> _repositoryMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IOptionsSnapshot<IdentityOptions>> _identityOptionsMock;
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
    private TwoFactorService CreateServiceWithOtp(OtpOptions otp, bool passkeyWiringOn = false)
    {
        var optionsMock = new Mock<IOptionsSnapshot<IdentityOptions>>();
        var options = new IdentityOptions { Otp = otp };
        options.Passkey.Enabled = passkeyWiringOn;
        optionsMock.Setup(x => x.Value).Returns(options);
        return new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            optionsMock.Object);
    }

    /// <summary>The passkey channel fully on: Otp.EnablePasskey AND the WebAuthn wiring itself.</summary>
    private TwoFactorService CreateServiceWithPasskeyChannel()
        => CreateServiceWithOtp(new OtpOptions { EnableEmail = true, EnableSms = false, EnableTotp = true, EnablePasskey = true }, passkeyWiringOn: true);

    private static UserPasskeyInfo APasskey() => new(
        credentialId: [1, 2, 3], publicKey: [4, 5, 6], createdAt: DateTimeOffset.UtcNow, signCount: 1,
        transports: ["usb"], isUserVerified: true, isBackupEligible: false, isBackedUp: false,
        attestationObject: [7], clientDataJson: [8]);

    private void GivenPasskeys(User user, int count)
        => _userManagerMock.Setup(x => x.GetPasskeysAsync(user))
            .ReturnsAsync(Enumerable.Range(0, count).Select(_ => APasskey()).ToList());

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
        var result = await service.SendSmsCodeAsync(Guid.NewGuid(), "13800138000", VerificationCodePurpose.TwoFactor);

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

    /// <summary>
    /// ★★ 已启用验证器的账号不能再走 setup：setup 会无条件 <c>ResetAuthenticatorKeyAsync</c>，
    /// 拿到一枚被盗访问令牌的人借它就能把受害者的第二因子<b>换成自己的</b>（而不只是摘掉），
    /// 受害者手里的验证器当场作废。要换验证器先经二次确认把旧的禁用掉。
    /// </summary>
    [Fact]
    public async Task GetTotpSetupInfoAsync_WhenTotpAlreadyEnabled_DoesNotResetKey_Returns409()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId, UserName = "testuser", Email = "test@example.com",
            TwoFactorEnabled = true, AuthenticatorTwoFactorEnabled = true, PreferredTwoFactorType = TwoFactorType.Totp
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);

        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(It.IsAny<User>()), Times.Never);
    }

    /// <summary>
    /// 对照组：旧式账号（总开关开着、按方式的标志位全空、有验证器密钥）经 Materialize 会被判成已启用 TOTP，
    /// 同样不许重置 —— 判定必须在 Materialize <b>之后</b>做。
    /// </summary>
    [Fact]
    public async Task GetTotpSetupInfoAsync_WhenLegacyTotpUserIsMaterializedAsEnabled_Returns409()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "legacy", Email = "legacy@example.com", TwoFactorEnabled = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync("JBSWY3DPEHPK3PXP");
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
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

    #region Passkey / 安全密钥作为第四种方式

    /// <summary>
    /// 渠道开着（两个开关都开）且账号登记过凭据 ⇒ 可启用；没登记 ⇒ 列出来但要先去登记（RequiresAddress 的 passkey 语义）。
    /// </summary>
    [Fact]
    public async Task GetTwoFactorStatusAsync_ListsPasskey_WhenBothSwitchesAreOn()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@example.com", EmailConfirmed = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        var service = CreateServiceWithPasskeyChannel();

        GivenPasskeys(user, 0);
        var without = (await service.GetTwoFactorStatusAsync(userId)).Data!.Methods.Single(m => m.Type == TwoFactorType.Passkey);
        Assert.False(without.Available);
        Assert.False(without.Enabled);
        Assert.True(without.RequiresAddress); // "register a passkey first"

        GivenPasskeys(user, 1);
        var with = (await service.GetTwoFactorStatusAsync(userId)).Data!.Methods.Single(m => m.Type == TwoFactorType.Passkey);
        Assert.True(with.Available);
        Assert.False(with.RequiresAddress);
    }

    /// <summary>
    /// ★ 只开 Otp.EnablePasskey 而没开 WebAuthn 接线 ⇒ 这一行根本不出现，也绝不查凭据存储：
    /// 没接 passkey 的部署一次都不该为此查表。
    /// </summary>
    [Fact]
    public async Task GetTwoFactorStatusAsync_OmitsPasskey_UnlessTheWiringIsOnToo()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@example.com", EmailConfirmed = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        var wiringOff = CreateServiceWithOtp(new OtpOptions { EnableEmail = true, EnablePasskey = true }, passkeyWiringOn: false);
        var channelOff = CreateServiceWithOtp(new OtpOptions { EnableEmail = true, EnablePasskey = false }, passkeyWiringOn: true);

        Assert.DoesNotContain((await wiringOff.GetTwoFactorStatusAsync(userId)).Data!.Methods, m => m.Type == TwoFactorType.Passkey);
        Assert.DoesNotContain((await channelOff.GetTwoFactorStatusAsync(userId)).Data!.Methods, m => m.Type == TwoFactorType.Passkey);
        _userManagerMock.Verify(x => x.GetPasskeysAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task EnableTwoFactorAsync_Passkey_RequiresARegisteredCredential()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        var service = CreateServiceWithPasskeyChannel();

        GivenPasskeys(user, 0);
        var refused = await service.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = TwoFactorType.Passkey });
        Assert.False(refused.Succeeded);
        Assert.Equal(400, refused.Code);
        Assert.False(user.PasskeyTwoFactorEnabled);

        GivenPasskeys(user, 1);
        var enabled = await service.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = TwoFactorType.Passkey });
        Assert.True(enabled.Succeeded);
        Assert.True(user.PasskeyTwoFactorEnabled);
        Assert.True(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorType.Passkey, user.PreferredTwoFactorType);
    }

    /// <summary>
    /// 无首选时 passkey 排最前（按抗钓鱼强度排，它比验证器 App 强）；用户自己选过的首选则原样保留。
    /// </summary>
    [Fact]
    public async Task EnableTwoFactorAsync_Passkey_IsPickedFirst_UnlessTheUserAlreadyChose()
    {
        var service = CreateServiceWithPasskeyChannel();

        // No preference on record: strength order picks the passkey, and the challenge lists it first.
        var undecidedId = Guid.NewGuid();
        var undecided = new User { Id = undecidedId, UserName = "u1", AuthenticatorTwoFactorEnabled = true, TwoFactorEnabled = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(undecidedId.ToString())).ReturnsAsync(undecided);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(undecided)).ReturnsAsync("KEY");
        GivenPasskeys(undecided, 1);
        await service.EnableTwoFactorAsync(undecidedId, new EnableTwoFactorDto { Type = TwoFactorType.Passkey });
        Assert.Equal(TwoFactorType.Passkey, undecided.PreferredTwoFactorType);
        Assert.Equal(new[] { TwoFactorType.Passkey, TwoFactorType.Totp }, await service.GetEnabledTwoFactorTypesAsync(undecided));

        // A choice already made is not overridden by a stronger method arriving.
        var decidedId = Guid.NewGuid();
        var decided = new User { Id = decidedId, UserName = "u2", AuthenticatorTwoFactorEnabled = true, TwoFactorEnabled = true, PreferredTwoFactorType = TwoFactorType.Totp };
        _userManagerMock.Setup(x => x.FindByIdAsync(decidedId.ToString())).ReturnsAsync(decided);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(decided)).ReturnsAsync("KEY");
        GivenPasskeys(decided, 1);
        await service.EnableTwoFactorAsync(decidedId, new EnableTwoFactorDto { Type = TwoFactorType.Passkey });
        Assert.Equal(TwoFactorType.Totp, decided.PreferredTwoFactorType);
        Assert.Equal(new[] { TwoFactorType.Totp, TwoFactorType.Passkey }, await service.GetEnabledTwoFactorTypesAsync(decided));
    }

    /// <summary>停用只关开关，凭据留着：它们还是登录与二次确认的凭据。</summary>
    [Fact]
    public async Task DisableTwoFactorMethodAsync_Passkey_ClearsTheFlagButKeepsTheCredentials()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", PasskeyTwoFactorEnabled = true, TwoFactorEnabled = true, PreferredTwoFactorType = TwoFactorType.Passkey };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        var service = CreateServiceWithPasskeyChannel();

        var result = await service.DisableTwoFactorMethodAsync(userId, TwoFactorType.Passkey);

        Assert.True(result.Succeeded);
        Assert.False(user.PasskeyTwoFactorEnabled);
        Assert.False(user.TwoFactorEnabled);
        Assert.Null(user.PreferredTwoFactorType);
        _userManagerMock.Verify(x => x.RemovePasskeyAsync(It.IsAny<User>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task DisableTwoFactorAsync_ResetsThePasskeyFlagToo()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", PasskeyTwoFactorEnabled = true, TwoFactorEnabled = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user)).ReturnsAsync(IdentityResult.Success);
        // The repository IS an IQueryable; back it with an async-capable empty set for the code sweep.
        var noCodes = new List<TwoFactorCode>().BuildMock();
        var queryable = _repositoryMock.As<IQueryable<TwoFactorCode>>();
        queryable.Setup(q => q.Provider).Returns(noCodes.Provider);
        queryable.Setup(q => q.Expression).Returns(noCodes.Expression);
        queryable.Setup(q => q.ElementType).Returns(noCodes.ElementType);
        queryable.Setup(q => q.GetEnumerator()).Returns(() => noCodes.GetEnumerator());

        await CreateServiceWithPasskeyChannel().DisableTwoFactorAsync(userId);

        Assert.False(user.PasskeyTwoFactorEnabled);
        Assert.False(user.TwoFactorEnabled);
    }

    /// <summary>
    /// 开关开着但凭据已经删光（或部署关了渠道）⇒ 登录不再提供它，别把人停在没人能完成的第二步。
    /// </summary>
    [Fact]
    public async Task GetEnabledTwoFactorTypesAsync_DropsPasskey_WhenNoCredentialRemains()
    {
        var user = new User { UserName = "u", PasskeyTwoFactorEnabled = true, EmailTwoFactorEnabled = true, Email = "u@example.com", EmailConfirmed = true, TwoFactorEnabled = true };
        var service = CreateServiceWithPasskeyChannel();

        GivenPasskeys(user, 1);
        Assert.Contains(TwoFactorType.Passkey, await service.GetEnabledTwoFactorTypesAsync(user));

        GivenPasskeys(user, 0);
        Assert.Equal(new[] { TwoFactorType.Email }, await service.GetEnabledTwoFactorTypesAsync(user));
    }

    /// <summary>passkey 不是验证码：发码与验码两条路都要说清楚，而不是掉进地址分支去找一个不存在的地址。</summary>
    [Fact]
    public async Task Passkey_IsNeitherSentNorVerifiedAsACode()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@example.com", EmailConfirmed = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        var service = CreateServiceWithPasskeyChannel();

        var sent = await service.SendCodeToUserAsync(userId, TwoFactorType.Passkey, VerificationCodePurpose.TwoFactor);
        var verified = await service.VerifyCodeAsync(userId, "123456", TwoFactorType.Passkey, VerificationCodePurpose.TwoFactor);

        Assert.False(sent.Succeeded);
        Assert.Equal(400, sent.Code);
        Assert.False(verified.Succeeded);
        Assert.Equal(400, verified.Code);
        _repositoryMock.Verify(x => x.InsertAsync(It.IsAny<TwoFactorCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #endregion

    #region 账号行写不进去

    /// <summary>
    /// <c>UserManager.UpdateAsync</c> 先跑全部用户校验器再落库。存量账号过不了当前规则时它返回失败而一个字都没写；
    /// 服务不看返回值就会对「启用 / 禁用第二因子」答 200 而库里原样。
    /// </summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable-all")]
    [InlineData("disable-method")]
    [InlineData("suspend")]
    [InlineData("resume")]
    [InlineData("preferred")]
    public async Task Writes_WhenTheUserRowFailsValidation_ReportFailureInsteadOfSuccess(string operation)
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "legacy",
            Email = "legacy@example.com",
            EmailConfirmed = true,
            EmailTwoFactorEnabled = operation != "enable",
            TwoFactorEnabled = operation != "enable",
        };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "DuplicateUserName", Description = "Username is taken." }));

        Result result = operation switch
        {
            "enable" => await _twoFactorService.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = TwoFactorType.Email }),
            "disable-all" => await _twoFactorService.DisableTwoFactorAsync(userId),
            "disable-method" => await _twoFactorService.DisableTwoFactorMethodAsync(userId, TwoFactorType.Email),
            "suspend" => await _twoFactorService.SuspendTwoFactorAsync(userId),
            "resume" => await _twoFactorService.ResumeTwoFactorAsync(userId),
            "preferred" => await _twoFactorService.SetPreferredTwoFactorAsync(userId, TwoFactorType.Email),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_USER_UPDATE_FAILED, result.ErrorCode);
    }

    /// <summary>重置验证器密钥没落库时不能把密钥交出去：扫进验证器的是一枚库里没有的密钥。</summary>
    [Fact]
    public async Task GetTotpSetupInfoAsync_WhenTheKeyResetIsNotSaved_DoesNotHandOutAKey()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@example.com" };
        var options = new OtpOptions { EnableTotp = true };
        var service = CreateServiceWithOtp(options);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "Email is taken." }));

        var result = await service.GetTotpSetupInfoAsync(userId);

        Assert.False(result.Succeeded);
        _userManagerMock.Verify(x => x.GetAuthenticatorKeyAsync(It.IsAny<User>()), Times.Never);
    }

    #endregion
}
