namespace Tnzi.Identity.Tests;

public class OAuthServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IUserLoginService> _userLoginServiceMock;
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IOAuthEmailVerificationPolicy> _emailPolicyMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly OAuthService _oauthService;

    public OAuthServiceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _userLoginServiceMock = new Mock<IUserLoginService>();
        _authServiceMock = new Mock<IAuthService>();
        _emailPolicyMock = new Mock<IOAuthEmailVerificationPolicy>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        // 默认放行：地址已被提供商证实。要测「未验证」的分支各自覆盖这一条。
        _emailPolicyMock
            .Setup(x => x.IsEmailVerified(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>()))
            .Returns(true);

        _oauthService = new OAuthService(
            _userManagerMock.Object,
            _userLoginServiceMock.Object,
            _serviceProviderMock.Object,
            _authServiceMock.Object,
            _emailPolicyMock.Object
        );
    }

    private static ClaimsPrincipal PrincipalFor(string providerKey, string? email = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, providerKey) };
        if (email != null) claims.Add(new Claim(ClaimTypes.Email, email));
        claims.Add(new Claim(ClaimTypes.Name, "Test User"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private void SetupIssuedToken(string accessToken = "access_token")
        => _authServiceMock
            .Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()))
            .ReturnsAsync(Result<TokenResult>.Success(new TokenResult
            {
                AccessToken = accessToken,
                RefreshToken = "refresh_token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            }));

    [Fact]
    public async Task HandleOAuthCallbackAsync_WithExistingUser_ReturnsTokenResult()
    {
        var provider = "Google";
        var providerKey = "google_user_id";
        var user = new User { Id = Guid.NewGuid(), UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, providerKey)).ReturnsAsync(user);
        SetupIssuedToken();

        var result = await _oauthService.HandleOAuthCallbackAsync(provider, PrincipalFor(providerKey, "test@example.com"));

        Assert.True(result.Succeeded);
        Assert.Equal("access_token", result.Data!.AccessToken);
    }

    /// <summary>
    /// ★★★ 签发必须经共享出口。此前本服务手抄了 <c>IssueTokenAsync</c> 的后半段，
    /// 于是 2FA 判定与义务位在第三方登录这条路上整个消失 —— 而那两件事都在共享出口里。
    /// 断言「调过它」而不是「令牌长什么样」：后者在手抄版本上也照样成立。
    /// </summary>
    [Fact]
    public async Task HandleOAuthCallbackAsync_IssuesThroughTheSharedExit()
    {
        var provider = "Google";
        var providerKey = "google_user_id";
        var user = new User { Id = Guid.NewGuid(), UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, providerKey)).ReturnsAsync(user);
        SetupIssuedToken();

        await _oauthService.HandleOAuthCallbackAsync(provider, PrincipalFor(providerKey, "test@example.com"));

        _authServiceMock.Verify(
            x => x.IssueTokenAsync(user, LoginMethod.OAuth, It.IsAny<TwoFactorType?>()), Times.Once);
    }

    /// <summary>
    /// 共享出口用失败信封承载 2FA 挑战；本服务必须把错误码与细节<b>原样带出</b>，
    /// 否则回调页只会渲染一句「OAuth callback failed」，挑战无从继续。
    /// </summary>
    [Fact]
    public async Task HandleOAuthCallbackAsync_PropagatesChallengeEnvelope()
    {
        var provider = "Google";
        var providerKey = "google_user_id";
        var user = new User { Id = Guid.NewGuid(), UserName = "testuser" };
        var details = new { TempToken = "temp-token" };

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, providerKey)).ReturnsAsync(user);
        _authServiceMock
            .Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()))
            .ReturnsAsync(Result<TokenResult>.Failure(
                "Two-factor authentication required", 403, ErrorCodes.IDENTITY_2FA_REQUIRED, details));

        var result = await _oauthService.HandleOAuthCallbackAsync(provider, PrincipalFor(providerKey, "test@example.com"));

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_2FA_REQUIRED, result.ErrorCode);
        Assert.Same(details, result.ErrorDetails);
    }

    [Fact]
    public async Task HandleOAuthCallbackAsync_WithNewUser_CreatesAccountAndReturnsToken()
    {
        var provider = "Google";
        var providerKey = "new_google_user_id";
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, providerKey)).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.FindByEmailAsync("newuser@example.com")).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => { u.Id = userId; return IdentityResult.Success; });
        _userManagerMock.Setup(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);
        SetupIssuedToken();

        var result = await _oauthService.HandleOAuthCallbackAsync(provider, PrincipalFor(providerKey, "newuser@example.com"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data!.AccessToken);
    }

    /// <summary>
    /// 提供商证实过地址 → 新账号的 <c>EmailConfirmed</c> 为真。
    /// 与下面那条构成对照组：只有「未验证时为假」的话，一个恒 false 的实现也能通过。
    /// </summary>
    [Fact]
    public async Task HandleOAuthCallbackAsync_VerifiedEmail_MarksNewUserConfirmed()
    {
        User? created = null;
        SetupNewUserFlow(u => created = u);

        await _oauthService.HandleOAuthCallbackAsync("google", PrincipalFor("key", "new@example.com"));

        Assert.NotNull(created);
        Assert.True(created!.EmailConfirmed);
    }

    /// <summary>
    /// ★★ 提供商没证实地址 → 新账号<b>不</b>带确认位。
    /// 此前这里是「邮箱非空即已确认」，于是一个用户自己填的地址在本地拿到了
    /// 「已验证」的断言，而框架下游拿那一位决定要不要往这个地址发找回密码的码。
    /// </summary>
    [Fact]
    public async Task HandleOAuthCallbackAsync_UnverifiedEmail_DoesNotMarkNewUserConfirmed()
    {
        _emailPolicyMock
            .Setup(x => x.IsEmailVerified(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>()))
            .Returns(false);

        User? created = null;
        SetupNewUserFlow(u => created = u);

        await _oauthService.HandleOAuthCallbackAsync("github", PrincipalFor("key", "new@example.com"));

        Assert.NotNull(created);
        Assert.False(created!.EmailConfirmed);
    }

    /// <summary>
    /// ★★★ 地址未被证实、而本地已有人用着它 → 拒绝，既不认领也不新建。
    /// 这是整条接管链的封堵点：用受害者的邮箱去第三方注册一个号，再用它登录，
    /// 此前会直接关联到受害者的本地账号并签发令牌 —— 不需要密码，也不过两步验证。
    /// </summary>
    [Fact]
    public async Task HandleOAuthCallbackAsync_UnverifiedEmailMatchingExistingUser_RefusesToLink()
    {
        var victim = new User { Id = Guid.NewGuid(), UserName = "victim", Email = "victim@example.com" };

        _emailPolicyMock
            .Setup(x => x.IsEmailVerified(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>()))
            .Returns(false);
        _userManagerMock.Setup(x => x.FindByLoginAsync("github", "attacker-key")).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.FindByEmailAsync("victim@example.com")).ReturnsAsync(victim);

        var result = await _oauthService.HandleOAuthCallbackAsync("github", PrincipalFor("attacker-key", "victim@example.com"));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_OAUTH_LINK_CONFIRMATION_REQUIRED, result.ErrorCode);

        // 关键在于「一枚令牌都没签出去」，而不只是返回了失败。
        _authServiceMock.Verify(
            x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()), Times.Never);
        _userManagerMock.Verify(
            x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task LinkOAuthAccountAsync_WithValidInput_LinksAccount()
    {
        var userId = Guid.NewGuid();
        var provider = "Google";
        var providerKey = "google_user_id";
        var displayName = "Test User";
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.AddLoginAsync(user, It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);
        _userLoginServiceMock.Setup(x => x.RecordLoginAsync(userId, provider, providerKey, displayName))
            .Returns(Task.CompletedTask);

        await _oauthService.LinkOAuthAccountAsync(userId, provider, providerKey, displayName);

        _userManagerMock.Verify(x => x.AddLoginAsync(user, It.IsAny<UserLoginInfo>()), Times.Once);
        _userLoginServiceMock.Verify(x => x.RecordLoginAsync(userId, provider, providerKey, displayName), Times.Once);
    }

    /// <summary>
    /// ★★ 个人中心「绑定第三方账号」的服务层：只给<b>当前账号</b>加一条外部登录，不签发令牌、不新建账号。
    /// 此前前端把绑定做成「已登录态重走一遍 OAuth 登录」，而回调从头到尾不读当前用户 ——
    /// 邮箱不一致时凭空建出一个孤儿账号并永久占住该 provider key。
    /// </summary>
    [Fact]
    public async Task LinkExternalLoginAsync_LinksToTheGivenUser_AndCreatesNoAccount()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "me", Email = "me@corp.example" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.FindByLoginAsync("github", "gh_123")).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.AddLoginAsync(user, It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);

        // 第三方邮箱与本站不同：那是登录流程里新建账号的分支，绑定流程绝不能走到它。
        var result = await _oauthService.LinkExternalLoginAsync(userId, "github", PrincipalFor("gh_123", "me@personal.example"));

        Assert.True(result.Succeeded, result.Message);
        _userManagerMock.Verify(x => x.AddLoginAsync(user, It.Is<UserLoginInfo>(l => l.LoginProvider == "github" && l.ProviderKey == "gh_123")), Times.Once);
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Never);
        _authServiceMock.Verify(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()), Times.Never);
    }

    /// <summary>provider key 已属于另一个账号 → 409，且不动任何一边。</summary>
    [Fact]
    public async Task LinkExternalLoginAsync_WhenTheProviderKeyBelongsToSomeoneElse_Returns409()
    {
        var userId = Guid.NewGuid();
        var someoneElse = new User { Id = Guid.NewGuid(), UserName = "other" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(new User { Id = userId, UserName = "me" });
        _userManagerMock.Setup(x => x.FindByLoginAsync("github", "gh_123")).ReturnsAsync(someoneElse);

        var result = await _oauthService.LinkExternalLoginAsync(userId, "github", PrincipalFor("gh_123"));

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    /// <summary>已经绑在本人账号上 → 幂等成功。</summary>
    [Fact]
    public async Task LinkExternalLoginAsync_WhenAlreadyLinkedToTheSameUser_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "me" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.FindByLoginAsync("github", "gh_123")).ReturnsAsync(user);
        _userLoginServiceMock.Setup(x => x.HasLoginAsync(userId, "github", "gh_123")).ReturnsAsync(true);

        var result = await _oauthService.LinkExternalLoginAsync(userId, "github", PrincipalFor("gh_123"));

        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task UnlinkOAuthAccountAsync_WithValidInput_UnlinksAccount()
    {
        var userId = Guid.NewGuid();
        var provider = "Google";
        var providerKey = "google_user_id";
        var user = new User { Id = userId, UserName = "testuser" };

        var loginInfo = new UserLoginInfo(provider, providerKey, provider);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetLoginsAsync(user)).ReturnsAsync(new List<UserLoginInfo> { loginInfo });
        _userManagerMock.Setup(x => x.RemoveLoginAsync(user, provider, providerKey)).ReturnsAsync(IdentityResult.Success);
        _userLoginServiceMock.Setup(x => x.RemoveLoginAsync(userId, provider, providerKey)).Returns(Task.CompletedTask);

        await _oauthService.UnlinkOAuthAccountAsync(userId, provider);

        _userManagerMock.Verify(x => x.RemoveLoginAsync(user, provider, providerKey), Times.Once);
        _userLoginServiceMock.Verify(x => x.RemoveLoginAsync(userId, provider, providerKey), Times.Once);
    }

    /// <summary>新建账号那条路径的公共装配；<paramref name="onCreated"/> 捕获被创建的实体。</summary>
    private void SetupNewUserFlow(Action<User> onCreated)
    {
        _userManagerMock.Setup(x => x.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => { u.Id = Guid.NewGuid(); onCreated(u); return IdentityResult.Success; });
        _userManagerMock.Setup(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);
        SetupIssuedToken();
    }
}
