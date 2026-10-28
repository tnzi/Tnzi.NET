using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Tnzi.Identity.Controllers;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 个人中心「绑定第三方账号」经 OAuth 发起 / 回调两个匿名端点的落地。
/// </summary>
/// <remarks>
/// ★ 守的是两条：带 <c>linkToken</c> 的回调<b>绝不能</b>走登录路径（<c>HandleOAuthCallbackAsync</c>），
/// 那条路的兜底是「新建账号 + 签发令牌」：此前前端的「绑定」按钮就是这么把一个孤儿账号建出来的；
/// 也<b>绝不能</b>在这个匿名回调里直接关联（见 <see cref="OAuthLinkCsrfTests"/>）：它只暂存并交回完成码，
/// 回调页拿到的结果里没有任何令牌。
/// </remarks>
public class DefaultAuthControllerOAuthLinkTests
{
    private readonly Mock<IOAuthService> _oauth = new();
    private readonly Mock<IOAuthLinkTokenService> _linkTokens = new();
    private readonly Mock<IIdentityPageService> _pages = new();
    private readonly Mock<IAuthenticationService> _authentication = new();
    private OAuthCallbackResultDto? _renderedResult;

    public DefaultAuthControllerOAuthLinkTests()
    {
        _pages.Setup(p => p.GenerateOAuthCallbackHtml(It.IsAny<OAuthCallbackResultDto>(), It.IsAny<string?>()))
            .Returns((OAuthCallbackResultDto r, string? _) => { _renderedResult = r; return "<callback/>"; });
        _pages.Setup(p => p.GenerateOAuthErrorHtml(It.IsAny<string>())).Returns((string m) => $"<error>{m}</error>");
        _authentication.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Identity.External", It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
    }

    private DefaultAuthController Build()
    {
        var schemeProvider = new Mock<IAuthenticationSchemeProvider>();
        schemeProvider.Setup(s => s.GetAllSchemesAsync())
            .ReturnsAsync([new AuthenticationScheme("GitHub", "GitHub", typeof(IAuthenticationHandler))]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(schemeProvider.Object);
        services.AddSingleton(_authentication.Object);
        var provider = services.BuildServiceProvider();

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
            oauthLinkTokens: _linkTokens.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } },
        };
        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/api/auth/oauth/github/callback");
        controller.Url = url.Object;
        return controller;
    }

    [Fact]
    public async Task OAuthLogin_WithAnInvalidLinkToken_Returns400_WithoutChallenging()
    {
        _linkTokens.Setup(t => t.PeekAsync("bad", "github")).ReturnsAsync(Result<Guid>.Failure("Invalid or expired link token", 400));

        var result = await Build().OAuthLogin("github", linkToken: "bad");

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task OAuthLogin_WithAValidLinkToken_CarriesItIntoTheChallenge()
    {
        _linkTokens.Setup(t => t.PeekAsync("good", "github")).ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));

        var result = await Build().OAuthLogin("github", linkToken: "good");

        var challenge = result.ShouldBeOfType<ChallengeResult>();
        challenge.Properties!.Items["linkToken"].ShouldBe("good");
    }

    [Fact]
    public async Task Callback_WithALinkToken_StagesTheLink_AndLinksNothingYet()
    {
        var owner = Guid.NewGuid();
        _linkTokens.Setup(t => t.ConsumeAsync("good", "github")).ReturnsAsync(Result<Guid>.Success(owner));
        _linkTokens.Setup(t => t.StagePendingLinkAsync(owner, "github", It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Result<string>.Success("completion-code"));
        ArrangeExternalAuthentication(new Dictionary<string, string?> { ["linkToken"] = "good" });

        await Build().OAuthCallbackHandler("github");

        _linkTokens.Verify(t => t.StagePendingLinkAsync(owner, "github", It.IsAny<ClaimsPrincipal>()), Times.Once);
        _oauth.Verify(o => o.LinkExternalLoginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>()), Times.Never);
        _oauth.Verify(o => o.LinkOAuthAccountAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _oauth.Verify(o => o.HandleOAuthCallbackAsync(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>()), Times.Never);
        _renderedResult.ShouldNotBeNull();
        _renderedResult!.Success.ShouldBeTrue();
        _renderedResult.LinkedProvider.ShouldBe("github");
        _renderedResult.LinkCode.ShouldBe("completion-code");
        _renderedResult.AccessToken.ShouldBeNull();
        _renderedResult.RefreshToken.ShouldBeNull();
    }

    [Fact]
    public async Task Callback_WithALinkToken_WhenStagingFails_RendersTheErrorPage()
    {
        var owner = Guid.NewGuid();
        _linkTokens.Setup(t => t.ConsumeAsync("good", "github")).ReturnsAsync(Result<Guid>.Success(owner));
        _linkTokens.Setup(t => t.StagePendingLinkAsync(owner, "github", It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Result<string>.Failure("Provider key not found in claims", 400, ErrorCodes.IDENTITY_OAUTH_ERROR));
        ArrangeExternalAuthentication(new Dictionary<string, string?> { ["linkToken"] = "good" });

        var result = await Build().OAuthCallbackHandler("github");

        result.ShouldBeOfType<ContentResult>().Content!.ShouldContain("<error>");
        _renderedResult.ShouldBeNull();
        _oauth.Verify(o => o.HandleOAuthCallbackAsync(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    /// <summary>对照组：没有绑定令牌的回调走登录路径，与此前逐字相同。</summary>
    [Fact]
    public async Task Callback_WithoutALinkToken_StillTakesTheLoginPath()
    {
        _oauth.Setup(o => o.HandleOAuthCallbackAsync("github", It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Result<OAuthCallbackResultDto>.Success(new OAuthCallbackResultDto { Success = true, AccessToken = "at" }));
        ArrangeExternalAuthentication(new Dictionary<string, string?>());

        await Build().OAuthCallbackHandler("github");

        _oauth.Verify(o => o.HandleOAuthCallbackAsync("github", It.IsAny<ClaimsPrincipal>()), Times.Once);
        _linkTokens.Verify(t => t.ConsumeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// 回调里抛出的异常只进服务端日志：此前异常文本被原样渲染进页面并 postMessage 给打开它的窗口，
    /// 数据库错误、类型名之类的内部细节直接摆在最终用户面前。
    /// </summary>
    [Fact]
    public async Task Callback_WhenTheSignInThrows_ShowsAGenericMessage()
    {
        _oauth.Setup(o => o.HandleOAuthCallbackAsync("github", It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new InvalidOperationException("internal-detail-marker"));
        ArrangeExternalAuthentication(new Dictionary<string, string?>());

        var result = await Build().OAuthCallbackHandler("github");

        var content = result.ShouldBeOfType<ContentResult>().Content!;
        content.ShouldContain("<error>");
        content.ShouldNotContain("internal-detail-marker");
    }

    /// <summary>外部认证失败的原因同理：它可能带着第三方令牌端点的响应头与响应体。</summary>
    [Fact]
    public async Task Callback_WhenExternalAuthenticationFails_ShowsAGenericMessage()
    {
        _authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), "Identity.External"))
            .ReturnsAsync(AuthenticateResult.Fail("token-endpoint-body-marker"));

        var result = await Build().OAuthCallbackHandler("github");

        var content = result.ShouldBeOfType<ContentResult>().Content!;
        content.ShouldContain("<error>");
        content.ShouldNotContain("token-endpoint-body-marker");
    }

    private void ArrangeExternalAuthentication(Dictionary<string, string?> items)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "gh_123"), new Claim(ClaimTypes.Email, "someone@personal.example")], "GitHub"));
        var properties = new AuthenticationProperties(items);
        _authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), "Identity.External"))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Identity.External")));
    }
}
