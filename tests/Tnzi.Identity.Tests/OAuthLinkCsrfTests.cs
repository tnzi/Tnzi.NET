using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Models;
using Tnzi.Identity.Controllers;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// ★★★ 个人中心「绑定第三方账号」整条链路的 login-CSRF：绑定令牌是 URL 参数，回调是匿名的。
/// </summary>
/// <remarks>
/// <para>
/// 攻击：攻击者给自己签一枚绑定令牌，把 <c>auth/oauth/github/login?linkToken=…</c> 发给受害者。
/// 受害者在 GitHub 那边早已授权过，整个流程静默走完：此前回调在这里就直接关联，
/// 受害者的 GitHub 身份挂到了攻击者账号上；此后受害者「用 GitHub 登录」登进的是攻击者的账号，
/// 在里面留下的东西攻击者都看得到。
/// </para>
/// <para>
/// 这里用真实的 <see cref="OAuthLinkTokenService"/>（令牌表换成内存版）把两个控制器串起来：
/// 回调只暂存并把完成码交回<b>跑回调的那个浏览器</b>，关联要等该浏览器里的已登录会话调完成端点，
/// 而那个会话必须就是签发令牌的账号。
/// </para>
/// </remarks>
public class OAuthLinkCsrfTests
{
    private readonly InMemoryAuthTokenStore _tokens = new();
    private readonly OAuthLinkTokenService _linkTokens;
    private readonly Mock<IOAuthService> _oauth = new();
    private readonly Mock<IIdentityPageService> _pages = new();
    private readonly Mock<IAuthenticationService> _authentication = new();
    private OAuthCallbackResultDto? _rendered;

    public OAuthLinkCsrfTests()
    {
        var options = new Mock<IOptionsMonitor<IdentityOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new IdentityOptions());
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _linkTokens = new OAuthLinkTokenService(services, _tokens.Service, options.Object);

        _pages.Setup(p => p.GenerateOAuthCallbackHtml(It.IsAny<OAuthCallbackResultDto>(), It.IsAny<string?>()))
            .Returns((OAuthCallbackResultDto r, string? _) => { _rendered = r; return "<callback/>"; });
        _pages.Setup(p => p.GenerateOAuthErrorHtml(It.IsAny<string>())).Returns((string m) => $"<error>{m}</error>");
        _authentication.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Identity.External", It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        _oauth.Setup(o => o.LinkOAuthAccountAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());
    }

    /// <summary>The anonymous OAuth callback, run in whatever browser followed the start link.</summary>
    private async Task<string?> RunCallbackAsync(string linkToken, string providerKey)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, providerKey)], "GitHub"));
        _authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), "Identity.External"))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                principal, new AuthenticationProperties(new Dictionary<string, string?> { ["linkToken"] = linkToken }), "Identity.External")));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_authentication.Object);
        var options = new Mock<IOptionsMonitor<IdentityOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new IdentityOptions());

        var controller = new DefaultAuthController(
            new Mock<ITwoFactorService>().Object,
            new Mock<IAuthService>().Object,
            new Mock<IRegistrationService>().Object,
            new Mock<IPasswordService>().Object,
            oAuthService: _oauth.Object,
            identityOptions: options.Object,
            identityPageService: _pages.Object,
            oauthLinkTokens: _linkTokens)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } },
        };
        controller.Url = new Mock<IUrlHelper>().Object;

        _rendered = null;
        await controller.OAuthCallbackHandler("github");
        return _rendered?.LinkCode;
    }

    /// <summary>The completion endpoint, called by the SPA in that same browser as whoever is signed in there.</summary>
    private async Task<ApiResult> CompleteAsSignedInUserAsync(Guid? signedInUserId, string code)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (signedInUserId.HasValue)
        {
            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(c => c.Id).Returns(signedInUserId.Value);
            currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
            services.AddSingleton(currentUser.Object);
        }

        var controller = new DefaultUserProfileController(
            new Mock<IUserService>().Object,
            new Mock<IPasswordService>().Object,
            oAuthService: _oauth.Object,
            oauthLinkTokens: _linkTokens)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } },
        };

        return await controller.CompleteLink("github", new CompleteOAuthLinkDto { Code = code });
    }

    private void AssertNothingWasLinked()
        => _oauth.Verify(o => o.LinkOAuthAccountAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);

    [Fact]
    public async Task AnAttackersLinkToken_FollowedByAVictimWhoIsSignedIn_LinksNothing()
    {
        var attacker = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var token = (await _linkTokens.IssueAsync(attacker, "github")).Data!.Token;

        // The victim's browser follows the attacker's start link; the provider step completes silently.
        var code = await RunCallbackAsync(token, providerKey: "gh_victim");
        code.ShouldNotBeNull();
        AssertNothingWasLinked();

        // The victim's SPA picks the completion code up and completes as the victim.
        var completed = await CompleteAsSignedInUserAsync(victim, code!);

        completed.Succeeded.ShouldBeFalse();
        completed.Code.ShouldBe(400);
        AssertNothingWasLinked();
    }

    [Fact]
    public async Task AnAttackersLinkToken_FollowedByAVictimWhoIsSignedOut_LinksNothing()
    {
        var attacker = Guid.NewGuid();
        var token = (await _linkTokens.IssueAsync(attacker, "github")).Data!.Token;
        var code = await RunCallbackAsync(token, providerKey: "gh_victim");

        var completed = await CompleteAsSignedInUserAsync(signedInUserId: null, code!);

        completed.Succeeded.ShouldBeFalse();
        completed.Code.ShouldBe(401);
        AssertNothingWasLinked();
    }

    /// <summary>对照：本人发起、本人完成，外部身份挂到本人账号上；完成码只能用一次。</summary>
    [Fact]
    public async Task TheOwnersOwnFlow_LinksToTheOwner_Once()
    {
        var owner = Guid.NewGuid();
        var token = (await _linkTokens.IssueAsync(owner, "github")).Data!.Token;
        var code = await RunCallbackAsync(token, providerKey: "gh_owner");

        var completed = await CompleteAsSignedInUserAsync(owner, code!);
        var replayed = await CompleteAsSignedInUserAsync(owner, code!);

        completed.Succeeded.ShouldBeTrue();
        replayed.Succeeded.ShouldBeFalse();
        _oauth.Verify(o => o.LinkOAuthAccountAsync(owner, "github", "gh_owner", It.IsAny<string?>()), Times.Once);
    }
}
