using Tnzi.Identity.Events;

using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 登录守卫（<see cref="ILoginGuard"/> / <see cref="ILoginGuardEvaluator"/>）。
/// </summary>
/// <remarks>
/// 这里最要紧的是 <c>LoginAsync_WhenGuardDenies_*</c> 那组：它们把「守卫必须跑在
/// 令牌签发之前」钉死。消费应用此前只能子类化 <c>DefaultAuthController</c>、在
/// <c>base.Login()</c> 拿到令牌之后再检查 IP，那样会留下三个副作用——会话已建立
/// （多设备策略据此踢掉其它设备）、失败计数被清零、登录日志记成一次成功——而且
/// 「403 只在口令正确时出现」本身就是个口令预言机。这些断言就是防它回退的锁。
/// </remarks>
public class LoginGuardTests
{
    // ── LoginGuardEvaluator ──────────────────────────────────────────────

    private static LoginGuardEvaluator CreateEvaluator(params ILoginGuard[] guards)
        => new(guards, new Mock<ILogger<LoginGuardEvaluator>>().Object);

    private static LoginGuardContext AnyContext()
        => new(new User { Id = Guid.NewGuid(), UserName = "u" }, LoginMethod.Password, "10.0.0.1", "agent");

    [Fact]
    public async Task Evaluator_WithNoGuards_AllowsAndReportsNoGuards()
    {
        var evaluator = CreateEvaluator();

        Assert.False(evaluator.HasGuards);
        Assert.True((await evaluator.EvaluateAsync(AnyContext())).Allowed);
    }

    [Fact]
    public async Task Evaluator_RunsGuardsInOrderAndShortCircuitsOnFirstDeny()
    {
        var calls = new List<string>();
        var first = new StubGuard("first", order: 1, LoginGuardResult.Allow(), calls);
        var denier = new StubGuard("denier", order: 2, LoginGuardResult.DenyAsInvalidCredentials("nope"), calls);
        var never = new StubGuard("never", order: 3, LoginGuardResult.Allow(), calls);

        // 故意乱序注册，求值器必须按 Order 排。
        var result = await CreateEvaluator(never, denier, first).EvaluateAsync(AnyContext());

        Assert.False(result.Allowed);
        Assert.Equal("nope", result.AuditReason);
        Assert.Equal(new[] { "first", "denier" }, calls);
    }

    [Fact]
    public async Task Evaluator_WhenGuardThrows_DeniesFailClosed()
    {
        // 一条准入策略静默失效（白名单服务挂了就人人都能进）比一次登录失败危险得多。
        var result = await CreateEvaluator(new ThrowingGuard()).EvaluateAsync(AnyContext());

        Assert.False(result.Allowed);
        Assert.Equal(400, result.Code);
        Assert.Contains("ThrowingGuard", result.AuditReason);
    }

    [Fact]
    public async Task Evaluator_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateEvaluator(new CancellingGuard()).EvaluateAsync(AnyContext(), cts.Token));
    }

    // ── LoginGuardResult ─────────────────────────────────────────────────

    [Fact]
    public void DenyAsInvalidCredentials_IsIndistinguishableFromAWrongPassword()
    {
        // ValidateLoginAndGetUserAsync 对密码错误返回的正是 400 + 这句文案。
        // 两者一旦可区分，守卫就退化成口令预言机。
        var result = LoginGuardResult.DenyAsInvalidCredentials("ip not allowed");

        Assert.False(result.Allowed);
        Assert.Equal(400, result.Code);
        Assert.Equal("Invalid username or password", result.Message);
        // 真实原因只进审计，不进响应。
        Assert.Equal("ip not allowed", result.AuditReason);
    }

    [Fact]
    public void Deny_KeepsTheGivenMessageAndDefaultsAuditReasonToIt()
    {
        var result = LoginGuardResult.Deny("Your account is suspended", code: 403);

        Assert.False(result.Allowed);
        Assert.Equal(403, result.Code);
        Assert.Equal("Your account is suspended", result.Message);
        Assert.Equal("Your account is suspended", result.AuditReason);
    }

    // ── AuthService 接线（A1 回归锁）─────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WhenGuardDenies_IssuesNoTokenAndEstablishesNoSession()
    {
        var fixture = new LoginFixture(LoginGuardResult.DenyAsInvalidCredentials("ip not allowed"));

        var result = await fixture.LoginAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal("Invalid username or password", result.Message);

        // 令牌从未生成，会话从未建立 —— 这正是「事后在控制器里拒绝」做不到的。
        fixture.TokenService.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
        fixture.SessionCoordinator.Verify(x => x.EstablishAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenGuardDenies_CountsAsAFailedLoginInsteadOfClearingTheCounter()
    {
        var fixture = new LoginFixture(LoginGuardResult.DenyAsInvalidCredentials("ip not allowed"));

        await fixture.LoginAsync();

        // 事后拒绝的写法会先跑到 ClearLoginFailureAsync，把暴力破解的失败计数清零。
        fixture.Captcha.Verify(x => x.ClearLoginFailureAsync(It.IsAny<string>()), Times.Never);
        fixture.Captcha.Verify(x => x.RecordLoginFailureAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenGuardDenies_AuditsAFailureCarryingTheRealReason()
    {
        var fixture = new LoginFixture(LoginGuardResult.DenyAsInvalidCredentials("ip not allowed"));

        await fixture.LoginAsync();

        // 对外同形、对内可查：运维在登录日志里看得到是哪条守卫拦的。
        fixture.EventBus.Verify(x => x.PublishAsync(
            It.Is<UserLoginFailedEvent>(e => e.FailureReason == "ip not allowed"),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.EventBus.Verify(x => x.PublishAsync(
            It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenGuardAllows_SignsInNormally()
    {
        var fixture = new LoginFixture(LoginGuardResult.Allow());

        var result = await fixture.LoginAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("access_token", result.Data);
        fixture.Captcha.Verify(x => x.ClearLoginFailureAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithNoGuardsRegistered_IsUnaffected()
    {
        var fixture = new LoginFixture(guardResult: null);

        var result = await fixture.LoginAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task LoginAsync_WhenGuardDenies_NeverReachesTheTwoFactorChallenge()
    {
        // 守卫排在 2FA 之前：被拒的登录不该白发一条验证码短信，
        // 而且 2FA 挑战本身就等于告诉对方「口令是对的」。
        var fixture = new LoginFixture(LoginGuardResult.DenyAsInvalidCredentials("ip not allowed"));
        fixture.EnableTwoFactor();

        var result = await fixture.LoginAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid username or password", result.Message);
        fixture.TwoFactorService.Verify(
            x => x.GetEnabledTwoFactorTypesAsync(It.IsAny<User>()), Times.Never);
    }

    // ── Stubs ────────────────────────────────────────────────────────────

    private sealed class StubGuard(string name, int order, LoginGuardResult result, List<string> calls) : ILoginGuard
    {
        public int Order => order;

        public Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
        {
            calls.Add(name);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingGuard : ILoginGuard
    {
        public Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("allow-list backend is down");
    }

    private sealed class CancellingGuard : ILoginGuard
    {
        public Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LoginGuardResult.Allow());
        }
    }

    /// <summary>一次成功密码登录所需的全套 mock，守卫结果可注入。</summary>
    private sealed class LoginFixture
    {
        private const string Username = "testuser";
        private const string Password = "Password123!";

        public Mock<ITokenService> TokenService { get; } = new();
        public Mock<ICaptchaService> Captcha { get; } = new();
        public Mock<IEventBus> EventBus { get; } = new();
        public Mock<ILoginSessionCoordinator> SessionCoordinator { get; } = new();
        public Mock<ITwoFactorService> TwoFactorService { get; } = new();

        private readonly Mock<UserManager<User>> _userManager;
        private readonly AuthService _authService;
        private readonly User _user = new()
        {
            Id = Guid.NewGuid(),
            UserName = Username,
            Email = "test@example.com",
            EmailConfirmed = true,
        };

        public LoginFixture(LoginGuardResult? guardResult)
        {
            var store = new Mock<IUserStore<User>>();
            _userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

            var signInManager = new Mock<SignInManager<User>>(
                _userManager.Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IUserClaimsPrincipalFactory<User>>().Object,
                new Mock<IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>().Object,
                new Mock<ILogger<SignInManager<User>>>().Object,
                new Mock<IAuthenticationSchemeProvider>().Object,
                new Mock<IUserConfirmation<User>>().Object);

            var identityOptions = new Mock<IOptionsMonitor<IdentityOptions>>();
            identityOptions.Setup(x => x.CurrentValue).Returns(new IdentityOptions());

            var scopedContext = new Mock<IScopedContext>();
            scopedContext.Setup(x => x.ClientIpAddress).Returns("203.0.113.9");
            scopedContext.Setup(x => x.UserAgent).Returns("Test Browser");

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(IScopedContext))).Returns(scopedContext.Object);
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

            Captcha.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>())).ReturnsAsync(false);
            Captcha.Setup(x => x.RecordLoginFailureAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            Captcha.Setup(x => x.ClearLoginFailureAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

            _userManager.Setup(x => x.FindByNameAsync(Username)).ReturnsAsync(_user);
            _userManager.Setup(x => x.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(false);
            _userManager.Setup(x => x.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "User" });
            signInManager.Setup(x => x.CheckPasswordSignInAsync(_user, Password, It.IsAny<bool>()))
                .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

            var passwordPolicy = new Mock<IPasswordPolicyService>();
            passwordPolicy.Setup(x => x.CheckPasswordExpirationAsync(_user.Id))
                .ReturnsAsync(new PasswordExpirationResult { IsExpired = false });

            var loginSecurity = new Mock<ILoginSecurityService>();
            loginSecurity.Setup(x => x.DetectAbnormalLoginAsync(_user.Id, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(AbnormalLoginResult.Normal());

            SessionCoordinator.Setup(x => x.EstablishAsync(It.IsAny<Guid>()))
                .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));

            TokenService.Setup(x => x.GenerateToken(
                    It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()))
                .Returns("access_token");

            // guardResult 为 null = 消费应用一个守卫都没注册（HasGuards=false 的常态路径）。
            var evaluator = new Mock<ILoginGuardEvaluator>();
            evaluator.Setup(x => x.HasGuards).Returns(guardResult != null);
            evaluator.Setup(x => x.EvaluateAsync(It.IsAny<LoginGuardContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(guardResult ?? LoginGuardResult.Allow());

            _authService = new AuthService(
                _userManager.Object,
                signInManager.Object,
                TokenService.Object,
                identityOptions.Object,
                serviceProvider.Object,
                EventBus.Object,
                Captcha.Object,
                new Mock<IAuthTokenService>().Object,
                passwordPolicy.Object,
                new Mock<ISessionService>().Object,
                loginSecurity.Object,
                TwoFactorService.Object,
                loginSessionCoordinator: SessionCoordinator.Object,
                loginGuardEvaluator: evaluator.Object);
        }

        public void EnableTwoFactor()
            => _userManager.Setup(x => x.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);

        public Task<Result<string>> LoginAsync()
            => _authService.LoginAsync(new LoginDto { UserName = Username, Password = Password });
    }
}
