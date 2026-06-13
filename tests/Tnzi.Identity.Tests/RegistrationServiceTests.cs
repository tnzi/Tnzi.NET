
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class RegistrationServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<ICaptchaService> _captchaServiceMock;
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly Mock<IAuthTokenService> _authTokenServiceMock;
    private readonly Mock<IPasswordPolicyService> _passwordPolicyServiceMock;
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
        _captchaServiceMock = new Mock<ICaptchaService>();
        _twoFactorServiceMock = new Mock<ITwoFactorService>();
        _authTokenServiceMock = new Mock<IAuthTokenService>();
        _passwordPolicyServiceMock = new Mock<IPasswordPolicyService>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _registrationService = new RegistrationService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _captchaServiceMock.Object,
            _twoFactorServiceMock.Object,
            _authTokenServiceMock.Object,
            _passwordPolicyServiceMock.Object
        );
    }

    [Fact]
    public async Task RegisterAsync_WithValidInput_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new RegisterDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!"
        };
        var user = new User
        {
            Id = userId,
            UserName = input.UserName,
            Email = input.Email
        };

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), input.Password))
            .ReturnsAsync((User u, string p) =>
            {
                u.Id = userId; // 设置用户ID
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

    [Fact]
    public async Task RegisterAsync_WithCaptchaEnabled_ValidatesCaptcha()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions(),
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
            _captchaServiceMock.Object,
            _twoFactorServiceMock.Object,
            _authTokenServiceMock.Object,
            _passwordPolicyServiceMock.Object
        );

        var input = new RegisterDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!",
            CaptchaId = "captcha_id",
            CaptchaCode = "wrong_code"
        };

        _captchaServiceMock.Setup(x => x.VerifyAsync("captcha_id", "wrong_code", "register"))
            .ReturnsAsync(false);

        // Act
        var result = await service.RegisterAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("captcha", result.Message);
    }

    [Fact]
    public async Task SendQuickRegisterCodeAsync_WithValidEmail_ReturnsSuccess()
    {
        // Arrange
        var input = new SendQuickRegisterCodeDto
        {
            Email = "test@example.com"
        };
        _twoFactorServiceMock.Setup(x => x.SendCodeByAddressAsync(input.Email!, TwoFactorType.Email, null))
            .ReturnsAsync(Result.Success());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.QuickRegisterCodeSentEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _registrationService.SendQuickRegisterCodeAsync(input);

        // Assert
        Assert.True(result.Succeeded);
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

        _twoFactorServiceMock.Setup(x => x.VerifyCodeByAddressAndMarkUsedAsync(input.Email!, input.Code, TwoFactorType.Email))
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

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(input.Password))
            .Returns((string?)null);

        _userManagerMock.Setup(x => x.HasPasswordAsync(user))
            .ReturnsAsync(true);

        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, input.Token, input.Password))
            .ReturnsAsync(IdentityResult.Success);

        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(true);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserPasswordResetEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _registrationService.SetPasswordAsync(input);

        // Assert
        Assert.True(result.Succeeded);
    }
}