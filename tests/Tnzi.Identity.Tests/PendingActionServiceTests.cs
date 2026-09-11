namespace Tnzi.Identity.Tests;

/// <summary>
/// 待办义务的完成：凭临时令牌把欠的事办完，全办完才拿令牌。
/// </summary>
public class PendingActionServiceTests
{
    /// <summary>
    /// ★★★ 还欠着别的时**不签发令牌、也不消费令牌** —— 同一枚继续用来办下一件。
    /// </summary>
    /// <remarks>
    /// 办完一件就直接放人进去，是这套机制最容易出的错：它会让「必须先绑 TOTP」
    /// 在一个同时欠着改密的账号上变成一句空话，而且不会有任何测试变红。
    /// </remarks>
    [Fact]
    public async Task Complete_WhenOtherActionsRemain_DoesNotIssueTokens()
    {
        var fixture = new Fixture(PendingUserActions.ChangePassword | PendingUserActions.EnrollTotp);

        var result = await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.Completed);
        Assert.Equal([nameof(PendingUserActions.EnrollTotp)], result.Data.RemainingActions);
        Assert.Null(result.Data.Token);
        Assert.False(fixture.TokenConsumed, "the token must stay usable for the next action");
    }

    /// <summary>
    /// ★ 清位只清刚办完的那一件，不能把其它欠着的一起抹掉。
    /// </summary>
    [Fact]
    public async Task Complete_ClearsOnlyTheActionJustPerformed()
    {
        var fixture = new Fixture(PendingUserActions.ChangePassword | PendingUserActions.ConfirmEmail);

        await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.False(fixture.User.HasPendingAction(PendingUserActions.ChangePassword));
        Assert.True(fixture.User.HasPendingAction(PendingUserActions.ConfirmEmail));
    }

    [Fact]
    public async Task Complete_WhenNothingElseRemains_ConsumesTheTokenAndIssues()
    {
        var fixture = new Fixture(PendingUserActions.ChangePassword);

        var result = await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.True(result.Data!.Completed);
        Assert.NotNull(result.Data.Token);
        Assert.True(fixture.TokenConsumed);
        Assert.Equal(PendingUserActions.None, fixture.User.PendingActions);
    }

    /// <summary>
    /// ★★★ 共享签发出口挑战时，挑战必须原样透出 —— 尤其 <c>ErrorDetails</c> 里的临时令牌。
    /// </summary>
    /// <remarks>
    /// 复现路径（默认配置即可）：管理员重置密码（<c>requireChangeOnNextLogin</c> 默认 true）
    /// 作用于一个开着 2FA 的账号 → 用户过 2FA → 被要求改密 → 改密成功后这条出口重新判定 2FA，
    /// 扣掉本次已证明的因子之后仍有剩余方式 → 403 + 一枚新的临时令牌。
    /// 把它压成 <c>200 { completed: true, token: null }</c> 的后果是**事情已经办完了**
    /// （密码已改、义务位已清、待办令牌已消费）而界面显示失败，同页重试必然再失败。
    /// </remarks>
    [Fact]
    public async Task Complete_WhenIssuingIsChallenged_SurfacesTheChallengeWithItsDetails()
    {
        var fixture = new Fixture(PendingUserActions.ChangePassword);
        fixture.IssueOutcome = Result<TokenResult>.Failure(
            "Two-factor authentication required",
            403,
            ErrorCodes.IDENTITY_2FA_REQUIRED,
            new { TempToken = "second-hop-token", SupportedTypes = new[] { TwoFactorType.Totp } });

        var result = await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_2FA_REQUIRED, result.ErrorCode);
        Assert.Equal("Two-factor authentication required", result.Message);

        // 临时令牌就在这里；丢了它，前端没有任何办法继续。
        Assert.NotNull(result.ErrorDetails);
        Assert.Contains("second-hop-token", System.Text.Json.JsonSerializer.Serialize(result.ErrorDetails));
    }

    /// <summary>
    /// ★★ 不欠这件事就不受理。否则这个端点成了一条旁路：谁手里有一枚未过期的
    /// 挑战令牌，都能凭它跳过原本要走的其它义务。
    /// </summary>
    [Fact]
    public async Task Complete_AnActionNotOwed_IsRejected()
    {
        var fixture = new Fixture(PendingUserActions.EnrollTotp);

        var result = await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_TOKEN_INVALID, result.ErrorCode);
        Assert.Equal(PendingUserActions.EnrollTotp, fixture.User.PendingActions);
    }

    /// <summary>
    /// ★ 事没办成就不清位、不消费令牌 —— 验证码输错是常事，
    /// 烧掉令牌等于让人重新登录一遍。
    /// </summary>
    [Fact]
    public async Task Complete_WhenTheActionItselfFails_KeepsTheTokenUsable()
    {
        var fixture = new Fixture(PendingUserActions.EnrollTotp);
        fixture.TotpEnrollmentSucceeds = false;

        var result = await fixture.Service.CompleteEnrollTotpAsync(
            new CompletePendingActionCodeDto { TempToken = fixture.TempToken, Code = "000000" });

        Assert.False(result.Succeeded);
        Assert.False(fixture.TokenConsumed);
        Assert.True(fixture.User.HasPendingAction(PendingUserActions.EnrollTotp));
    }

    /// <summary>
    /// 失效 / 已用 / 过期 / 不存在回答同一句话。
    /// </summary>
    [Fact]
    public async Task Complete_WithAnUnknownToken_GivesTheSameAnswerAsAnExpiredOne()
    {
        var fixture = new Fixture(PendingUserActions.ChangePassword);

        var unknown = await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = "made-up", NewPassword = "P@ssw0rd!" });

        fixture.ExpireToken();
        var expired = await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.Equal(unknown.Code, expired.Code);
        Assert.Equal(unknown.ErrorCode, expired.ErrorCode);
        Assert.Equal(unknown.Message, expired.Message);
    }

    /// <summary>
    /// 欠着绑验证器时，描述要带上密钥 —— 否则前端只知道「你得绑个验证器」却没有二维码可扫。
    /// </summary>
    [Fact]
    public async Task Describe_CarriesTheTotpSetupKey_WhenEnrolmentIsOwed()
    {
        var fixture = new Fixture(PendingUserActions.EnrollTotp);

        var described = await fixture.Service.DescribeAsync(fixture.TempToken);

        Assert.True(described.Succeeded);
        Assert.Contains(nameof(PendingUserActions.EnrollTotp), described.Data!.RequiredActions);
        Assert.NotNull(described.Data.TotpSetup);
        Assert.False(fixture.TokenConsumed, "describing must not consume the token");
    }

    /// <summary>
    /// 确认邮箱那一项要带掩码地址，供提示「码发到哪」；且不能是完整地址。
    /// </summary>
    [Fact]
    public async Task Describe_MasksTheEmail_WhenConfirmationIsOwed()
    {
        var fixture = new Fixture(PendingUserActions.ConfirmEmail);

        var described = await fixture.Service.DescribeAsync(fixture.TempToken);

        Assert.NotNull(described.Data!.MaskedEmail);
        Assert.DoesNotContain(Fixture.Email, described.Data.MaskedEmail!, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// ★★★ 绑定验证器之后签发时必须扣掉 TOTP 因子。
    /// </summary>
    /// <remarks>
    /// <c>EnableTotpAsync</c> 会把 2FA 总开关一并打开（它是聚合值），于是共享签发出口上的
    /// 2FA 判定会当场再问一次验证器码 —— 而用户刚刚为了完成绑定就输过一个。
    /// 框架 2026-08-26 为此加了 <c>satisfiedFactor</c>，这里必须用上。
    /// </remarks>
    [Fact]
    public async Task CompleteEnrollTotp_DeductsTheFactorItJustProved()
    {
        var fixture = new Fixture(PendingUserActions.EnrollTotp);

        await fixture.Service.CompleteEnrollTotpAsync(
            new CompletePendingActionCodeDto { TempToken = fixture.TempToken, Code = "123456" });

        Assert.Equal(TwoFactorType.Totp, fixture.IssuedSatisfiedFactor);
    }

    /// <summary>
    /// 确认邮箱同理：刚用邮箱验证码证明了「能收这个邮箱」，邮箱 2FA 再问一遍问的是同一件事。
    /// </summary>
    [Fact]
    public async Task CompleteConfirmEmail_DeductsTheEmailFactor()
    {
        var fixture = new Fixture(PendingUserActions.ConfirmEmail);

        await fixture.Service.CompleteConfirmEmailAsync(
            new CompletePendingActionCodeDto { TempToken = fixture.TempToken, Code = "123456" });

        Assert.Equal(TwoFactorType.Email, fixture.IssuedSatisfiedFactor);
        Assert.True(fixture.User.EmailConfirmed);
    }

    /// <summary>
    /// 改密什么因子都没证明，不能扣。
    /// </summary>
    [Fact]
    public async Task CompleteChangePassword_DeductsNothing()
    {
        var fixture = new Fixture(PendingUserActions.ChangePassword);

        await fixture.Service.CompleteChangePasswordAsync(
            new CompletePasswordChangeDto { TempToken = fixture.TempToken, NewPassword = "P@ssw0rd!" });

        Assert.Null(fixture.IssuedSatisfiedFactor);
    }

    /// <summary>
    /// ★★ 「确认邮箱」的码用专属用途，不能与「换绑联系方式」共用一个码池。
    /// </summary>
    /// <remarks>
    /// 两者都往邮箱发码，但问的是不同的问题：换绑问「你能收到这个新地址吗」，
    /// 本流程问「你还能收到账号上现有这个地址吗」。共用用途就是把两个流程放进同一个码池。
    /// </remarks>
    [Fact]
    public async Task EmailConfirmation_UsesItsOwnCodePurpose()
    {
        var fixture = new Fixture(PendingUserActions.ConfirmEmail);

        await fixture.Service.SendEmailConfirmationCodeAsync(fixture.TempToken);
        await fixture.Service.CompleteConfirmEmailAsync(
            new CompletePendingActionCodeDto { TempToken = fixture.TempToken, Code = "123456" });

        Assert.Equal(VerificationCodePurpose.ConfirmEmail, fixture.SentPurpose);
        Assert.Equal(VerificationCodePurpose.ConfirmEmail, fixture.VerifiedPurpose);
    }

    private sealed class Fixture
    {
        public const string Email = "owing@example.com";

        public PendingActionService Service { get; }

        public User User { get; }

        public string TempToken { get; } = "temp-token-value";

        public bool TokenConsumed { get; private set; }

        public bool TotpEnrollmentSucceeds { get; set; } = true;

        /// <summary>
        /// 共享签发出口的回答。默认成功；置成挑战信封即可复现「2FA 已启用 + 欠义务」的账号。
        /// </summary>
        public Result<TokenResult> IssueOutcome { get; set; } =
            Result<TokenResult>.Success(new TokenResult { AccessToken = "issued" });

        public TwoFactorType? IssuedSatisfiedFactor { get; private set; }

        public VerificationCodePurpose? SentPurpose { get; private set; }

        public VerificationCodePurpose? VerifiedPurpose { get; private set; }

        private readonly AuthToken _entry;

        public Fixture(PendingUserActions owed)
        {
            User = new User
            {
                Id = Guid.NewGuid(),
                UserName = "owing",
                Email = Email,
                PendingActions = owed,
            };

            _entry = new AuthToken
            {
                Id = Guid.NewGuid(),
                UserId = User.Id,
                LoginProvider = IdentityConstants.LoginProvider.PendingAction,
                Name = IdentityConstants.TokenName.PendingActionToken,
                Value = TempToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            };

            var store = new Mock<IUserStore<User>>();
            var userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            userManager.Setup(x => x.FindByIdAsync(User.Id.ToString())).ReturnsAsync(User);
            userManager.Setup(x => x.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

            var authTokenService = new Mock<IAuthTokenService>();
            authTokenService
                .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string provider, string name, string value) =>
                    value == TempToken && !_entry.IsUsed ? _entry : null);
            authTokenService
                .Setup(x => x.MarkTokenAsUsedAsync(_entry.Id))
                .ReturnsAsync(() => { TokenConsumed = true; _entry.IsUsed = true; return true; });

            var authService = new Mock<IAuthService>();
            authService
                .Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()))
                .ReturnsAsync((User _, LoginMethod __, TwoFactorType? satisfied) =>
                {
                    IssuedSatisfiedFactor = satisfied;
                    return IssueOutcome;
                });

            var passwordService = new Mock<IPasswordService>();
            passwordService
                .Setup(x => x.ForceSetPasswordAsync(It.IsAny<User>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Success());

            var twoFactor = new Mock<ITwoFactorService>();
            twoFactor
                .Setup(x => x.GetTotpSetupInfoAsync(It.IsAny<Guid>()))
                .ReturnsAsync(Result<TotpSetupDto>.Success(new TotpSetupDto { SharedKey = "KEY", AuthenticatorUri = "otpauth://x" }));
            twoFactor
                .Setup(x => x.SendCodeToUserAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()))
                .ReturnsAsync((Guid _, TwoFactorType __, VerificationCodePurpose purpose) =>
                {
                    SentPurpose = purpose;
                    return Result<string?>.Success("m***@example.com");
                });
            twoFactor
                .Setup(x => x.VerifyCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()))
                .ReturnsAsync((Guid _, string __, TwoFactorType ___, VerificationCodePurpose purpose) =>
                {
                    VerifiedPurpose = purpose;
                    return Result.Success();
                });
            twoFactor
                .Setup(x => x.EnableTotpAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync(() => TotpEnrollmentSucceeds
                    ? Result.Success()
                    : Result.Failure("Invalid verification code", 400));

            Service = new PendingActionService(
                BuildServiceProvider(),
                userManager.Object,
                authTokenService.Object,
                authService.Object,
                passwordService.Object,
                twoFactor.Object);
        }

        public void ExpireToken() => _entry.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        private static IServiceProvider BuildServiceProvider()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            return serviceProvider.Object;
        }
    }
}
