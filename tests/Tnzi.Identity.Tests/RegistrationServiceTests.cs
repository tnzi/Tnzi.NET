
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class RegistrationServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<ICaptchaVerifier> _captchaVerifierMock;
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly Mock<IAuthTokenService> _authTokenServiceMock;
    private readonly Mock<IPasswordService> _passwordServiceMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly RegistrationService _registrationService;

    public RegistrationServiceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _identityOptionsMock = new Mock<IOptionsMonitor<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions
            {
                EnableSelfRegistration = true, // 默认 false（deny-by-default），这些用例测的是开着时的行为
                EnableQuickRegisterEmail = true,
                EnableQuickRegisterSms = true,
                DefaultUserNameFromEmail = true,
                RequireConfirmedEmail = true // 启用邮箱确认，这样注册后不返回 Token
            },
            Captcha = new CaptchaOptions
            {
                EnableCaptchaOnRegister = false
            },
            Otp = new OtpOptions()
        });

        _eventBusMock = new Mock<IEventBus>();
        _captchaVerifierMock = new Mock<ICaptchaVerifier>();
        _captchaVerifierMock.SetupGet(x => x.ProviderName).Returns("image");
        // 默认：任何令牌都按「没交 / 不对」拒绝；具体用例再为某个令牌设 Pass（Moq 后设的 Setup 优先）。
        _captchaVerifierMock.Setup(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? token, string _, CancellationToken _) => CaptchaVerification.Fail("image", string.IsNullOrEmpty(token) ? CaptchaFailure.MissingToken : CaptchaFailure.Rejected));
        _twoFactorServiceMock = new Mock<ITwoFactorService>();
        _authTokenServiceMock = new Mock<IAuthTokenService>();
        _passwordServiceMock = new Mock<IPasswordService>();
        _passwordServiceMock
            .Setup(x => x.ForceSetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success());
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _registrationService = new RegistrationService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _twoFactorServiceMock.Object,
            _authTokenServiceMock.Object,
            passwordService: _passwordServiceMock.Object,
            captchaVerifier: _captchaVerifierMock.Object
        );
    }

    [Fact]
    public async Task RegisterAsync_WithValidInput_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password123!"
        };
        User? created = null;

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), input.Password))
            .ReturnsAsync((User u, string p) =>
            {
                u.Id = userId; // 设置用户ID
                created = u;
                return IdentityResult.Success;
            });

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserRegisteredEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _registrationService.RegisterAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.RequireEmailConfirmation); // 需要邮箱确认
        Assert.Equal(input.Email, result.Data.Email);
        Assert.Equal(userId, result.Data.UserId);
        Assert.Equal(input.Email, created!.UserName); // 默认用邮箱作用户名
    }

    [Fact]
    public async Task RegisterAsync_WithUserNameDifferentFromEmail_IsRejectedByDefault()
    {
        var input = new RegisterDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!"
        };

        var result = await _registrationService.RegisterAsync(input);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithInvalidPassword_ReturnsFailure()
    {
        // Arrange
        var input = new RegisterDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "weak"
        };

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), input.Password))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));

        // Act
        var result = await _registrationService.RegisterAsync(input);

        // Assert
        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// ★★★ 关掉自助注册之后，端点必须自己拒绝。
    /// </summary>
    /// <remarks>
    /// 此前 <c>POST auth/register</c> <b>没有任何开关</b>：<c>RegistrationOptions</c> 只有两个
    /// quick-register 标志，而 <c>GET auth/config</c> 的 <c>enableRegistration</c> 只由那两个推导 ——
    /// 于是出厂状态是「配置说注册关着、登录页把入口藏起来、而端点照样给任何人开户」。
    /// 前端隐藏入口是体验，端点匿名可达，门必须在服务层。
    /// </remarks>
    [Fact]
    public async Task RegisterAsync_WhenSelfRegistrationDisabled_IsRejected()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions { EnableSelfRegistration = false },
            Captcha = new CaptchaOptions(),
        });

        var result = await _registrationService.RegisterAsync(new RegisterDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!",
        });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        // 账号一个都不该被建出来 —— 只返回失败但仍然 CreateAsync 了，等于没挡住。
        _userManagerMock.Verify(
            x => x.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// ★★ 邮箱确认重发：账号不存在 / 已确认 / 已发送，三种结果同一句话。
    /// 此前分别是 404 / 400 / 200，于是任何人都能拿这个匿名端点枚举出哪些邮箱注册过、
    /// 并顺带读出每一个的确认状态。
    /// </summary>
    [Fact]
    public async Task ResendEmailConfirmation_AnswersUniformly()
    {
        _userManagerMock.Setup(x => x.FindByEmailAsync("nobody@example.com")).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.FindByEmailAsync("confirmed@example.com"))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Email = "confirmed@example.com", EmailConfirmed = true });
        _userManagerMock.Setup(x => x.FindByEmailAsync("pending@example.com"))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Email = "pending@example.com", EmailConfirmed = false });

        var unknown = await _registrationService.ResendEmailConfirmationAsync(new ResendEmailConfirmationDto { Email = "nobody@example.com" });
        var confirmed = await _registrationService.ResendEmailConfirmationAsync(new ResendEmailConfirmationDto { Email = "confirmed@example.com" });
        var pending = await _registrationService.ResendEmailConfirmationAsync(new ResendEmailConfirmationDto { Email = "pending@example.com" });

        Assert.True(unknown.Succeeded);
        Assert.True(confirmed.Succeeded);
        Assert.True(pending.Succeeded);
        // 连消息文本都必须一致：状态码相同而文案不同，一样是预言机。
        Assert.Equal(pending.Data, unknown.Data);
        Assert.Equal(pending.Data, confirmed.Data);
    }

    /// <summary>
    /// 重发确认邮件与注册发码同一个开关：开着 <c>EnableCaptchaOnRegister</c> 时先过人机验证，且在查用户之前。
    /// 此前这个每次调用都真的发一封信的匿名入口不受任何验证码开关管辖。
    /// </summary>
    [Fact]
    public async Task ResendEmailConfirmation_WhenRegisterCaptchaEnabledAndMissing_RejectsBeforeLookingUpTheUser()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnRegister = true }
        });

        var result = await _registrationService.ResendEmailConfirmationAsync(new ResendEmailConfirmationDto { Email = "pending@example.com" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        _userManagerMock.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _eventBusMock.Verify(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.EmailConfirmationResentEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>「未启用，放行」不是「校验通过」：注册验证码开着而验证器报告没有生效的提供商时拒绝。</summary>
    [Fact]
    public async Task ResendEmailConfirmation_WhenRegisterCaptchaIsOnButTheVerifierReportsNotEnabled_Rejects()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnRegister = true }
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.NotEnabled());

        var result = await _registrationService.ResendEmailConfirmationAsync(new ResendEmailConfirmationDto { Email = "pending@example.com", CaptchaToken = "widget-token" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        _userManagerMock.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResendEmailConfirmation_WhenRegisterCaptchaValid_ProceedsUnderTheRegisterPurpose()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnRegister = true }
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync("widget-token", CaptchaPurpose.Register, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Pass("turnstile"));
        _userManagerMock.Setup(x => x.FindByEmailAsync("pending@example.com"))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Email = "pending@example.com", EmailConfirmed = false });
        _userManagerMock.Setup(x => x.GenerateEmailConfirmationTokenAsync(It.IsAny<User>())).ReturnsAsync("confirm-token");

        var result = await _registrationService.ResendEmailConfirmationAsync(new ResendEmailConfirmationDto { Email = "pending@example.com", CaptchaToken = "widget-token" });

        Assert.True(result.Succeeded);
        _captchaVerifierMock.Verify(x => x.VerifyAsync("widget-token", CaptchaPurpose.Register, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithCaptchaEnabled_ValidatesCaptcha()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions { EnableSelfRegistration = true },
            Captcha = new CaptchaOptions
            {
                EnableCaptchaOnRegister = true
            }
        });

        var service = new RegistrationService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _twoFactorServiceMock.Object,
            _authTokenServiceMock.Object,
            passwordService: _passwordServiceMock.Object,
            captchaVerifier: _captchaVerifierMock.Object
        );

        var input = new RegisterDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!",
            CaptchaId = "captcha_id",
            CaptchaCode = "wrong_code"
        };

        _captchaVerifierMock.Setup(x => x.VerifyAsync("captcha_id:wrong_code", CaptchaPurpose.Register, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Fail("image", CaptchaFailure.Rejected));

        // Act
        var result = await service.RegisterAsync(input);

        // Assert - the dedicated code, so the login page can reset its captcha widget.
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        Assert.Equal("image", Assert.IsType<CaptchaDto>(result.ErrorDetails).Provider);
    }

    [Fact]
    public async Task SendQuickRegisterCodeAsync_WithValidEmail_ReturnsSuccess()
    {
        // Arrange
        var input = new SendQuickRegisterCodeDto
        {
            Email = "test@example.com"
        };
        _twoFactorServiceMock.Setup(x => x.SendCodeByAddressAsync(input.Email!, TwoFactorType.Email, VerificationCodePurpose.Registration, null))
            .ReturnsAsync(Result.Success());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.QuickRegisterCodeSentEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _registrationService.SendQuickRegisterCodeAsync(input);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SendQuickRegisterCodeAsync_WhenCaptchaEnabledAndInvalid_ReturnsFailureWithoutSending()
    {
        // Arrange - register captcha on; the send-code step must gate on it.
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions { EnableQuickRegisterEmail = true },
            Captcha = new CaptchaOptions { EnableCaptchaOnRegister = true },
            Otp = new OtpOptions()
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync("cid:wrong", CaptchaPurpose.Register, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Fail("image", CaptchaFailure.Rejected));

        var input = new SendQuickRegisterCodeDto { Email = "test@example.com", CaptchaId = "cid", CaptchaCode = "wrong" };

        // Act
        var result = await _registrationService.SendQuickRegisterCodeAsync(input);

        // Assert - rejected with the dedicated code; the OTP is never sent (no SMS/email cost).
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task SendQuickRegisterCodeAsync_WhenCaptchaEnabledAndValid_SendsCode()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions { EnableQuickRegisterEmail = true },
            Captcha = new CaptchaOptions { EnableCaptchaOnRegister = true },
            Otp = new OtpOptions()
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync("cid:good", CaptchaPurpose.Register, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Pass("image"));
        _twoFactorServiceMock.Setup(x => x.SendCodeByAddressAsync("test@example.com", TwoFactorType.Email, VerificationCodePurpose.Registration, null))
            .ReturnsAsync(Result.Success());

        var input = new SendQuickRegisterCodeDto { Email = "test@example.com", CaptchaId = "cid", CaptchaCode = "good" };

        // Act
        var result = await _registrationService.SendQuickRegisterCodeAsync(input);

        // Assert - captcha passes → the OTP send proceeds.
        Assert.True(result.Succeeded);
        _twoFactorServiceMock.Verify(x => x.SendCodeByAddressAsync("test@example.com", TwoFactorType.Email, VerificationCodePurpose.Registration, null), Times.Once);
    }

    [Fact]
    public async Task QuickRegisterAsync_WithValidCode_ReturnsResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new QuickRegisterDto
        {
            Email = "test@example.com",
            Code = "123456"
        };

        _twoFactorServiceMock.Setup(x => x.VerifyCodeByAddressAndMarkUsedAsync(input.Email!, input.Code, TwoFactorType.Email, VerificationCodePurpose.Registration))
            .ReturnsAsync(Result<Guid?>.Success(null));

        _userManagerMock.Setup(x => x.FindByEmailAsync(input.Email!))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) =>
            {
                u.Id = userId; // 设置用户ID
                return IdentityResult.Success;
            });

        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(It.Is<User>(u => u.Id == userId)))
            .ReturnsAsync("set_password_token");

        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(userId, "Identity", "SetPassword", It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserRegisteredEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _registrationService.QuickRegisterAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(userId, result.Data.UserId);
    }

    [Fact]
    public async Task QuickRegisterAsync_WithUserNameDifferentFromEmail_IsRejectedBeforeTheCodeIsConsumed()
    {
        var input = new QuickRegisterDto { Email = "test@example.com", UserName = "someone-else", Code = "123456" };

        var result = await _registrationService.QuickRegisterAsync(input);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _twoFactorServiceMock.Verify(
            x => x.VerifyCodeByAddressAndMarkUsedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()),
            Times.Never);
    }

    [Fact]
    public async Task SetPasswordAsync_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new SetPasswordDto
        {
            UserId = userId,
            Token = "temp_token",
            Password = "Password123!"
        };
        var user = new User
        {
            Id = userId,
            UserName = "testuser"
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), input.Token))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = input.Token });

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("Identity", "SetPassword", input.Token))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = input.Token, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });

        _userManagerMock.Setup(x => x.HasPasswordAsync(user))
            .ReturnsAsync(true);

        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(true);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserPasswordResetEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _registrationService.SetPasswordAsync(input);

        // Assert
        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// ★★★ 注册后自动登录时，会话建立失败必须当场返回，不能沿用一个空会话继续签发。
    /// </summary>
    /// <remarks>
    /// 没有会话就没有 <c>session_id</c> claim，而每请求的会话强制校验是按这个 claim 触发的：
    /// 这样签出来的令牌踢不掉、「登出全部设备」对它无效、多设备策略也管不着它，
    /// 而且外观与一次正常注册完全相同。<c>AuthService</c> 的四个签发出口在这一步失败时
    /// 一律 <c>return Fail</c>，这条路径此前是唯一的例外。
    /// </remarks>
    [Fact]
    public async Task RegisterAsync_WhenTheSessionCannotBeEstablished_IssuesNothing()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions
            {
                EnableSelfRegistration = true,
                DefaultUserNameFromEmail = true,
                RequireConfirmedEmail = false, // 走到自动登录那一段
            },
            Captcha = new CaptchaOptions { EnableCaptchaOnRegister = false },
            Otp = new OtpOptions(),
        });

        var coordinator = new Mock<ILoginSessionCoordinator>();
        coordinator.Setup(x => x.EstablishAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result<Guid>.Failure("Maximum number of concurrent sessions reached", 403));

        var tokenService = new Mock<ITokenService>();

        var service = new RegistrationService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _twoFactorServiceMock.Object,
            _authTokenServiceMock.Object,
            tokenService: tokenService.Object,
            loginSessionCoordinator: coordinator.Object,
            passwordService: _passwordServiceMock.Object,
            captchaVerifier: _captchaVerifierMock.Object);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>())).ReturnsAsync([]);

        var result = await service.RegisterAsync(new RegisterDto
        {
            Email = "no-session@example.com",
            Password = "Password123!",
        });

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        tokenService.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// ★★★ 设置密码必须走共享出口 <c>ForceSetPasswordAsync</c>，不能自己写一遍。
    /// </summary>
    /// <remarks>
    /// 手写的那份缺三样：密码历史查重（设回上一个密码会被接受）、历史写入
    /// （下一次查重因此也查不到，缺陷自我延续）、以及会话撤销
    /// （账号失陷后按提示改了密码，攻击者手里的令牌原样有效）。
    /// 这与 09-01 修掉的第七条改密路径逐字同形，修法也相同：委托，不复制。
    /// </remarks>
    [Fact]
    public async Task SetPasswordAsync_GoesThroughTheSharedPasswordExit()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "existing" };
        var input = new SetPasswordDto { UserId = userId, Token = "temp_token", Password = "Password123!" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("Identity", "SetPassword", input.Token))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = input.Token, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>())).ReturnsAsync(true);

        // 账号已有密码 = 这是一次替换，既有会话必须一并作废。
        _userManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(true);

        var result = await _registrationService.SetPasswordAsync(input);

        Assert.True(result.Succeeded);
        _passwordServiceMock.Verify(
            x => x.ForceSetPasswordAsync(user, input.Password, true),
            Times.Once);
        _userManagerMock.Verify(
            x => x.AddPasswordAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        _userManagerMock.Verify(
            x => x.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// ★ 快速注册的账号设**第一个**密码时不撤会话：没有旧凭据要作废，
    /// 而唯一在线的那条会话正是本人刚凭验证码换来的 —— 撤掉等于设完密码当场被登出。
    /// </summary>
    [Fact]
    public async Task SetPasswordAsync_OnAnAccountWithoutOne_KeepsTheSessionItWasJustGiven()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "fresh" };
        var input = new SetPasswordDto { UserId = userId, Token = "temp_token", Password = "Password123!" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("Identity", "SetPassword", input.Token))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = input.Token, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _userManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(false);

        var result = await _registrationService.SetPasswordAsync(input);

        Assert.True(result.Succeeded);
        _passwordServiceMock.Verify(
            x => x.ForceSetPasswordAsync(user, input.Password, false),
            Times.Once);
    }

    /// <summary>
    /// ★★ 设不上密码就不能消费令牌 —— 否则一个不合强度要求的密码会把令牌一起赔掉，
    /// 而这个端点的令牌是一次性的：用户拿不回第二次机会。
    /// </summary>
    [Fact]
    public async Task SetPasswordAsync_WhenTheChangeFails_KeepsTheTokenUsable()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "fresh" };
        var input = new SetPasswordDto { UserId = userId, Token = "temp_token", Password = "weak" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("Identity", "SetPassword", input.Token))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = input.Token, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _userManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(false);
        _passwordServiceMock
            .Setup(x => x.ForceSetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Failure("Password is too weak", 400, ErrorCodes.VALIDATION_ERROR));

        var result = await _registrationService.SetPasswordAsync(input);

        Assert.False(result.Succeeded);
        _authTokenServiceMock.Verify(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// ★★ 这个端点匿名可达，令牌校验是它<b>唯一</b>的身份证明。
    /// 令牌服务缺席时必须失败，绝不能放行：那等于任何人拿一个 userId 就能给别人设密码。
    /// </summary>
    [Fact]
    public async Task SetPasswordAsync_WithoutATokenService_RefusesInsteadOfSkippingTheCheck()
    {
        var userId = Guid.NewGuid();
        var service = new RegistrationService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _twoFactorServiceMock.Object,
            authTokenService: null,
            passwordService: _passwordServiceMock.Object,
            captchaVerifier: _captchaVerifierMock.Object);

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(new User { Id = userId, UserName = "victim" });

        var result = await service.SetPasswordAsync(
            new SetPasswordDto { UserId = userId, Token = "anything", Password = "Password123!" });

        Assert.False(result.Succeeded);
        _passwordServiceMock.Verify(
            x => x.ForceSetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
    }
}