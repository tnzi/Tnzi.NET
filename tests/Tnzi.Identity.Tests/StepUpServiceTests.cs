using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 二次确认（step-up）：在已登录会话之上，要求对某个动作当场再证明一次本人。
/// </summary>
/// <remarks>
/// 这里的用例分两组：一组钉「没配置时不许把功能拦死」，另一组钉「配置了就不许被绕过」。
/// 两组都必须成立 —— 只满足前者是没做，只满足后者是漏配就全站瘫痪。
/// </remarks>
public class StepUpServiceTests
{
    private static readonly Guid SessionUser = Guid.NewGuid();

    [Fact]
    public async Task IsSatisfied_WhenDisabled_LetsEverythingThrough()
    {
        // ★ 这是加固项不是运行前提。漏配的后果应当是「没有额外保护」，
        //   而不是「所有标了特性的端点一律 401」。
        var fixture = new Fixture(enabled: false);

        Assert.True(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task IsSatisfied_WhenEnabledAndNeverVerified_IsFalse()
    {
        var fixture = new Fixture(enabled: true);

        Assert.False(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task VerifyWithCode_ThenSatisfied()
    {
        var fixture = new Fixture(enabled: true);
        fixture.CodeIsValid = true;

        var grant = await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");

        Assert.True(grant.Succeeded);
        Assert.Equal("tip.download", grant.Data!.Scope);
        Assert.True(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task AGrantDoesNotSpillIntoOtherScopes()
    {
        // ★ 为下载附件做的确认，不该顺便把「删除全部记录」也放行。
        var fixture = new Fixture(enabled: true);
        fixture.CodeIsValid = true;

        await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");

        Assert.True(await fixture.Service.IsSatisfiedAsync("tip.download"));
        Assert.False(await fixture.Service.IsSatisfiedAsync("tip.destroy"));
    }

    [Fact]
    public async Task ScopeMatchingIgnoresCaseAndPadding()
    {
        var fixture = new Fixture(enabled: true);
        fixture.CodeIsValid = true;

        await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "  Tip.Download  ");

        Assert.True(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task VerifyWithCode_WhenCodeIsWrong_Fails()
    {
        var fixture = new Fixture(enabled: true);
        fixture.CodeIsValid = false;

        var grant = await fixture.Service.VerifyWithCodeAsync("000000", TwoFactorType.Totp, "tip.download");

        Assert.False(grant.Succeeded);
        Assert.False(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task VerifyWithPasskey_WhenTheAssertionBelongsToSomeoneElse_IsRejected()
    {
        // ★★★ 断言成功只说明「有一把注册过的 passkey 在场」。若它属于另一个账号，
        //     证明的是别人在场 —— 而这里要证明的恰恰是「就是这个会话的主人」。
        //     少了这一比，任何持有自己 passkey 的人都能替一个被接管的会话完成确认。
        var fixture = new Fixture(enabled: true);
        fixture.AssertedUserId = Guid.NewGuid();

        var grant = await fixture.Service.VerifyWithPasskeyAsync(new PasskeyCompleteDto(), "tip.download");

        Assert.False(grant.Succeeded);
        Assert.False(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task VerifyWithPasskey_WhenTheAssertionIsTheSessionUser_Grants()
    {
        var fixture = new Fixture(enabled: true);
        fixture.AssertedUserId = SessionUser;

        var grant = await fixture.Service.VerifyWithPasskeyAsync(new PasskeyCompleteDto(), "tip.download");

        Assert.True(grant.Succeeded);
        Assert.True(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task SingleUse_ConsumesTheGrantOnFirstCheck()
    {
        var fixture = new Fixture(enabled: true, singleUse: true);
        fixture.CodeIsValid = true;

        await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.destroy");

        Assert.True(await fixture.Service.IsSatisfiedAsync("tip.destroy"));
        Assert.False(await fixture.Service.IsSatisfiedAsync("tip.destroy"));
    }

    [Fact]
    public async Task AnExpiredGrantNoLongerSatisfies()
    {
        var fixture = new Fixture(enabled: true);
        fixture.CodeIsValid = true;

        await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");
        fixture.ExpireStoredGrants();

        Assert.False(await fixture.Service.IsSatisfiedAsync("tip.download"));
    }

    [Fact]
    public async Task TheStoredGrantIsNotAUsableCredential()
    {
        // 这条记录不是凭据，但仍然存哈希：能读到库的人不该因此得到一个「确认过」的凭证。
        var fixture = new Fixture(enabled: true);
        fixture.CodeIsValid = true;

        await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");

        Assert.NotNull(fixture.LastSavedValue);
        Assert.Equal(OneTimeToken.HashLength, fixture.LastSavedValue!.Length);
    }

    [Fact]
    public async Task AnAnonymousCallerCannotStepUp()
    {
        // 二次确认是加在会话之上的，没有会话就无从加起。
        var fixture = new Fixture(enabled: true, authenticated: false);
        fixture.CodeIsValid = true;

        var grant = await fixture.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");

        Assert.False(grant.Succeeded);
        Assert.Equal(401, grant.Code);
    }

    /// <summary>
    /// ★★ 确认记录绑定发起确认的那条会话，不只绑用户。同一用户的另一条会话（被盗令牌）
    /// 不能搭本人这次确认的便车 —— 否则 step-up 挡的正好不是它的威胁模型里那个「终端已易手」。
    /// </summary>
    [Fact]
    public async Task AGrantInOneSession_DoesNotSatisfyAnotherSessionOfTheSameUser()
    {
        var store = new List<AuthToken>();
        var victim = new Fixture(enabled: true, sessionId: Guid.NewGuid(), sharedTokens: store);
        var attacker = new Fixture(enabled: true, sessionId: Guid.NewGuid(), sharedTokens: store);
        victim.CodeIsValid = true;

        await victim.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "identity.contact.change");

        Assert.True(await victim.Service.IsSatisfiedAsync("identity.contact.change"));
        Assert.False(await attacker.Service.IsSatisfiedAsync("identity.contact.change"));
    }

    /// <summary>
    /// 没有会话 claim 的部署（未启用会话 / 遗留令牌）两边都是 Guid.Empty，行为与绑定之前逐字相同。
    /// </summary>
    [Fact]
    public async Task WithoutASessionClaim_TheGrantStillSatisfiesTheSameUser()
    {
        var store = new List<AuthToken>();
        var first = new Fixture(enabled: true, sharedTokens: store);
        var second = new Fixture(enabled: true, sharedTokens: store);
        first.CodeIsValid = true;

        await first.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");

        Assert.True(await second.Service.IsSatisfiedAsync("tip.download"));
    }

    /// <summary>反向也要成立：在没有会话 claim 的上下文里做的确认，不能被一条有会话的请求拿去用。</summary>
    [Fact]
    public async Task AGrantWithoutASessionClaim_DoesNotSatisfyASessionBoundRequest()
    {
        var store = new List<AuthToken>();
        var sessionless = new Fixture(enabled: true, sharedTokens: store);
        var bound = new Fixture(enabled: true, sessionId: Guid.NewGuid(), sharedTokens: store);
        sessionless.CodeIsValid = true;

        await sessionless.Service.VerifyWithCodeAsync("123456", TwoFactorType.Totp, "tip.download");

        Assert.False(await bound.Service.IsSatisfiedAsync("tip.download"));
    }

    private sealed class Fixture
    {
        private readonly List<AuthToken> _tokens;

        public StepUpService Service { get; }

        public string? LastSavedValue { get; private set; }

        public bool CodeIsValid { get; set; }

        public Guid AssertedUserId { get; set; } = SessionUser;

        public Fixture(
            bool enabled, bool singleUse = false, bool authenticated = true,
            Guid? sessionId = null, List<AuthToken>? sharedTokens = null)
        {
            _tokens = sharedTokens ?? [];
            var authTokenService = new Mock<IAuthTokenService>();

            // 与真实 AuthTokenService 同一把唯一键 (UserId, LoginProvider, Name, SessionId)：会话绑定的记录按会话各存一条。
            authTokenService
                .Setup(x => x.SaveTokenAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()))
                .ReturnsAsync((Guid userId, string provider, string name, string value, DateTime? expiresAt, Guid session) =>
                {
                    LastSavedValue = value;
                    _tokens.RemoveAll(t => t.UserId == userId && t.LoginProvider == provider && t.Name == name && t.SessionId == session);
                    var token = new AuthToken
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        LoginProvider = provider,
                        Name = name,
                        Value = value,
                        ExpiresAt = expiresAt,
                        SessionId = session
                    };
                    _tokens.Add(token);
                    return token.Id;
                });

            authTokenService
                .Setup(x => x.GetUserTokensAsync(It.IsAny<Guid>(), It.IsAny<string?>()))
                .ReturnsAsync((Guid userId, string? provider) =>
                    _tokens.Where(t => t.UserId == userId && (provider == null || t.LoginProvider == provider)).ToList());

            authTokenService
                .Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) =>
                {
                    var token = _tokens.FirstOrDefault(t => t.Id == id);
                    if (token == null)
                    {
                        return false;
                    }

                    token.IsUsed = true;
                    return true;
                });

            var passkeyService = new Mock<IPasskeyService>();
            passkeyService
                .Setup(x => x.VerifyAssertionAsync(It.IsAny<PasskeyCompleteDto>()))
                .ReturnsAsync(() => Result<Guid>.Success(AssertedUserId));

            TwoFactorService = new Mock<ITwoFactorService>();
            TwoFactorService
                .Setup(x => x.VerifyCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TwoFactorType>(), VerificationCodePurpose.StepUp))
                .ReturnsAsync(() => CodeIsValid
                    ? Result.Success()
                    : Result.Failure("Invalid verification code", 400));
            TwoFactorService
                .Setup(x => x.SendCodeToUserAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()))
                .ReturnsAsync(Result<string?>.Success("a***@example.com"));

            var identityOptions = new TnziIdentityOptions();
            identityOptions.StepUp.Enabled = enabled;
            identityOptions.StepUp.SingleUse = singleUse;

            var options = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
            options.Setup(x => x.CurrentValue).Returns(identityOptions);

            Service = new StepUpService(
                BuildServiceProvider(authenticated, sessionId),
                authTokenService.Object,
                passkeyService.Object,
                TwoFactorService.Object,
                options.Object);
        }

        /// <summary>验证码服务 mock，供断言「二次确认发出的码用途是 StepUp」。</summary>
        public Mock<ITwoFactorService> TwoFactorService { get; private set; } = null!;

        public void ExpireStoredGrants()
        {
            foreach (var token in _tokens)
            {
                token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
        }

        private static IServiceProvider BuildServiceProvider(bool authenticated, Guid? sessionId)
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var currentUser = new Mock<ICurrentUser>();
            currentUser.Setup(x => x.Id).Returns(authenticated ? SessionUser : null);
            currentUser.Setup(x => x.FindClaim(IdentityConstants.ClaimTypeNames.SessionId)).Returns(sessionId?.ToString());

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            serviceProvider.Setup(x => x.GetService(typeof(ICurrentUser))).Returns(currentUser.Object);
            return serviceProvider.Object;
        }
    }

    /// <summary>
    /// ★ 二次确认的码用途必须是 <c>StepUp</c>，不能借登录 2FA 的码池。
    /// </summary>
    /// <remarks>
    /// 这个入口此前根本不存在：短信/邮箱做二次确认只能去用别的流程发出来的码，
    /// 而那正是「一枚登录码可以确认一笔转账」的由来。
    /// </remarks>
    [Fact]
    public async Task SendCodeAsync_IssuesWithStepUpPurpose()
    {
        var fixture = new Fixture(enabled: true);

        var result = await fixture.Service.SendCodeAsync(TwoFactorType.Email);

        Assert.True(result.Succeeded);
        Assert.Equal("a***@example.com", result.Data);
        fixture.TwoFactorService.Verify(
            x => x.SendCodeToUserAsync(SessionUser, TwoFactorType.Email, VerificationCodePurpose.StepUp),
            Times.Once);
    }

    /// <summary>
    /// 未登录不发码：二次确认是加在会话之上的，没有会话就无从加起 ——
    /// 也不该成为一个谁都能拿来给别人发信的入口。
    /// </summary>
    [Fact]
    public async Task SendCodeAsync_WhenNotAuthenticated_SendsNothing()
    {
        var fixture = new Fixture(enabled: true, authenticated: false);

        var result = await fixture.Service.SendCodeAsync(TwoFactorType.Email);

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
        fixture.TwoFactorService.Verify(
            x => x.SendCodeToUserAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()),
            Times.Never);
    }
}
