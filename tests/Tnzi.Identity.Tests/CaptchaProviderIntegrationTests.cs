using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 人机验证提供商层接进身份流程之后的契约：
/// 令牌形状 / 三条此前无门的发信入口 / 失败关闭 / 客户端配置随 /auth/config 下发。
/// </summary>
public class CaptchaProviderIntegrationTests
{
    // ── ImageCaptchaToken ──

    [Theory]
    [InlineData("abc", "1234", "abc:1234")]
    [InlineData("abc", null, null)]
    [InlineData(null, "1234", null)]
    [InlineData("", "1234", null)]
    [InlineData("abc", "  ", null)]
    public void ImageCaptchaToken_ComposeJoinsIdAndCode_OrReturnsNull(string? id, string? code, string? expected)
    {
        Assert.Equal(expected, ImageCaptchaToken.Compose(id, code));
    }

    [Theory]
    [InlineData("abc:1234", true, "abc", "1234")]
    [InlineData("abc:12:34", true, "abc", "12:34")]
    [InlineData("abc", false, "", "")]
    [InlineData(":1234", false, "", "")]
    [InlineData("abc:", false, "", "")]
    [InlineData("", false, "", "")]
    [InlineData(null, false, "", "")]
    public void ImageCaptchaToken_ParsesOnTheFirstSeparator(string? token, bool ok, string id, string code)
    {
        Assert.Equal(ok, ImageCaptchaToken.TryParse(token, out var parsedId, out var parsedCode));
        Assert.Equal(id, parsedId);
        Assert.Equal(code, parsedCode);
    }

    [Fact]
    public void ImageCaptchaToken_Resolve_PrefersTheUnifiedToken_ThenFallsBackToIdAndCode()
    {
        Assert.Equal("tok", ImageCaptchaToken.Resolve(new LoginDto { UserName = "u", Password = "p", CaptchaToken = "tok", CaptchaId = "a", CaptchaCode = "b" }));
        Assert.Equal("a:b", ImageCaptchaToken.Resolve(new LoginDto { UserName = "u", Password = "p", CaptchaId = "a", CaptchaCode = "b" }));
        Assert.Null(ImageCaptchaToken.Resolve(new LoginDto { UserName = "u", Password = "p" }));
    }

    // ── ImageCaptchaProvider ──

    [Fact]
    public async Task ImageCaptchaProvider_DelegatesToTheCaptchaService_WithThePurpose()
    {
        var service = new Mock<ICaptchaService>();
        service.Setup(x => x.VerifyAsync("cid", "code", "login")).ReturnsAsync(true);
        var provider = new ImageCaptchaProvider(service.Object);

        var ok = await provider.VerifyAsync(new CaptchaVerificationRequest("cid:code", "login", null));
        var bad = await provider.VerifyAsync(new CaptchaVerificationRequest("cid:other", "login", null));

        Assert.Equal("image", provider.Name);
        Assert.True(ok.Passed);
        Assert.Equal("login", ok.Action);
        Assert.False(bad.Passed);
        Assert.Equal(CaptchaFailure.Rejected, bad.Failure);
    }

    [Fact]
    public async Task ImageCaptchaProvider_RejectsAMalformedToken_WithoutTouchingTheService()
    {
        var service = new Mock<ICaptchaService>(MockBehavior.Strict);
        var provider = new ImageCaptchaProvider(service.Object);

        var result = await provider.VerifyAsync(new CaptchaVerificationRequest("no-separator", "login", null));

        Assert.False(result.Passed);
        Assert.Contains("{purpose}", provider.GetClientConfig().ChallengeUrl, StringComparison.Ordinal);
    }

    // ── IdentityOptionsValidator: 旧键搬家后必须启动即失败 ──

    [Theory]
    [InlineData("Provider", "recaptcha")]
    [InlineData("SiteKey", "site")]
    [InlineData("SecretKey", "secret")]
    public void Validator_RejectsTheMovedProviderKeys_AndPointsAtTheNewSection(string key, string value)
    {
        var captcha = new CaptchaOptions();
        typeof(CaptchaOptions).GetProperty(key)!.SetValue(captcha, value);
        var options = new IdentityOptions { Captcha = captcha };

        var result = new IdentityOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("AspNetCore:Captcha", StringComparison.Ordinal) && f.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsTheFlowSwitches_WithoutProviderKeys()
    {
        var options = new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = true, EnableCaptchaOnRegister = true, EnableCaptchaOnPasswordRecovery = true }
        };

        Assert.True(new IdentityOptionsValidator().Validate(null, options).Succeeded);
    }

    // ── PasswordService.ForgotPasswordAsync(dto) ──

    private sealed class PasswordFixture
    {
        public Mock<UserManager<User>> UserManager { get; }
        public Mock<ICaptchaVerifier> Verifier { get; } = new();
        public PasswordService Service { get; }

        public PasswordFixture(bool captchaOnRecovery, bool withVerifier = true)
        {
            var store = new Mock<IUserStore<User>>();
            UserManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            UserManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var options = new Mock<IOptionsSnapshot<IdentityOptions>>();
            options.Setup(x => x.Value).Returns(new IdentityOptions
            {
                Recovery = new RecoveryOptions { EnablePasswordResetByEmail = true },
                Captcha = new CaptchaOptions { EnableCaptchaOnPasswordRecovery = captchaOnRecovery }
            });

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
            var sp = new Mock<IServiceProvider>();
            sp.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

            Verifier.SetupGet(x => x.ProviderName).Returns("turnstile");
            Verifier.Setup(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? token, string _, CancellationToken _) =>
                    token == "good" ? CaptchaVerification.Pass("turnstile") : CaptchaVerification.Fail("turnstile", CaptchaFailure.Rejected));

            Service = new PasswordService(
                UserManager.Object,
                options.Object,
                sp.Object,
                captchaVerifier: withVerifier ? Verifier.Object : null);
        }
    }

    [Fact]
    public async Task ForgotPassword_WhenRecoveryCaptchaIsOff_DoesNotAskTheVerifier()
    {
        var f = new PasswordFixture(captchaOnRecovery: false);

        var result = await f.Service.ForgotPasswordAsync(new ForgotPasswordDto { Email = "a@example.com" });

        Assert.True(result.Succeeded);
        f.Verifier.Verify(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WhenRecoveryCaptchaIsOn_RejectsWithoutAToken_AndNeverLooksUpTheUser()
    {
        var f = new PasswordFixture(captchaOnRecovery: true);

        var result = await f.Service.ForgotPasswordAsync(new ForgotPasswordDto { Email = "a@example.com" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        Assert.Equal("turnstile", Assert.IsType<CaptchaDto>(result.ErrorDetails).Provider);
        f.UserManager.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WhenRecoveryCaptchaIsOn_AcceptsAValidToken_UnderThePasswordRecoveryPurpose()
    {
        var f = new PasswordFixture(captchaOnRecovery: true);

        var result = await f.Service.ForgotPasswordAsync(new ForgotPasswordDto { Email = "a@example.com", CaptchaToken = "good" });

        Assert.True(result.Succeeded);
        f.Verifier.Verify(x => x.VerifyAsync("good", CaptchaPurpose.PasswordRecovery, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_WhenCaptchaIsOnButNoVerifierIsRegistered_FailsClosed()
    {
        // 「没人能校验」不是「校验通过」：一个 DI 缺口不能把整个开关变成装饰。
        var f = new PasswordFixture(captchaOnRecovery: true, withVerifier: false);

        var result = await f.Service.ForgotPasswordAsync(new ForgotPasswordDto { Email = "a@example.com", CaptchaToken = "good" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        f.UserManager.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }
}
