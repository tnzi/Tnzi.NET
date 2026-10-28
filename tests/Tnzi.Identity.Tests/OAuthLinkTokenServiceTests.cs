using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 第三方账号绑定令牌：由已登录用户在个人中心签发、绑定本人与提供商、一次性、短期，
/// 由匿名的 OAuth 发起端点校验、回调端点消费。
/// </summary>
/// <remarks>
/// ★ 它存在的理由：OAuth 的发起与回调都是 <c>[AllowAnonymous]</c> 的整页跳转，不带 bearer，
/// 后端在回调里连「谁在绑定」都无从知道 —— 此前个人中心的「绑定」按钮直接重走匿名登录流程，
/// 邮箱不一致时凭空建出一个孤儿账号。这枚令牌把「当前用户是谁」经 <c>Identity.External</c> cookie 往返带到回调。
/// </remarks>
public class OAuthLinkTokenServiceTests
{
    [Fact]
    public async Task Issue_StoresOnlyTheHash_BoundToUserAndProvider()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();

        var issued = (await fixture.Service.IssueAsync(userId, "GitHub")).Data!;

        Assert.NotEqual(issued.Token, fixture.LastSavedValue);
        Assert.Equal(OneTimeToken.Hash(issued.Token), fixture.LastSavedValue);
        Assert.Equal("github", issued.Provider);
        Assert.True(issued.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Peek_ReturnsTheUser_AndDoesNotConsume()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        var issued = (await fixture.Service.IssueAsync(userId, "github")).Data!;

        var first = await fixture.Service.PeekAsync(issued.Token, "github");
        var second = await fixture.Service.PeekAsync(issued.Token, "GitHub");

        Assert.True(first.Succeeded);
        Assert.Equal(userId, first.Data);
        Assert.True(second.Succeeded);
    }

    /// <summary>★ 令牌绑定提供商：为 GitHub 签的不能拿去绑 Google。</summary>
    [Fact]
    public async Task Peek_RejectsAnotherProvider()
    {
        var fixture = new Fixture();
        var issued = (await fixture.Service.IssueAsync(Guid.NewGuid(), "github")).Data!;

        var result = await fixture.Service.PeekAsync(issued.Token, "google");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task Consume_ReturnsTheUserOnce_ThenRejects()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        var issued = (await fixture.Service.IssueAsync(userId, "github")).Data!;

        var consumed = await fixture.Service.ConsumeAsync(issued.Token, "github");
        var again = await fixture.Service.ConsumeAsync(issued.Token, "github");

        Assert.True(consumed.Succeeded);
        Assert.Equal(userId, consumed.Data);
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task Peek_RejectsExpired()
    {
        var fixture = new Fixture();
        var issued = (await fixture.Service.IssueAsync(Guid.NewGuid(), "github")).Data!;

        fixture.ExpireStoredTokens();

        Assert.False((await fixture.Service.PeekAsync(issued.Token, "github")).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task Peek_RejectsGarbage_WithTheSameAnswer(string token)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.PeekAsync(token, "github");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    // ------------------------------------------------- 回调只暂存，关联要回到发起绑定的会话

    /// <summary>
    /// ★★★ 暂存在 A 名下的绑定，B 拿着完成码也完成不了：这是「把受害者的第三方身份挂到攻击者账号上」被挡住的地方。
    /// </summary>
    /// <remarks>
    /// 攻击者 A 给自己签绑定令牌、把发起链接发给受害者 B；回调（匿名、跑在 B 的浏览器里）把 B 的第三方身份暂存在 A 名下，
    /// 完成码交回 B 的浏览器。B 的前端以 B 的身份去完成：必须失败，且不能把 A 那份暂存消费掉或变成 B 的。
    /// </remarks>
    [Fact]
    public async Task StagedLink_CannotBeCompletedByAnotherAccount()
    {
        var fixture = new Fixture();
        var attacker = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var code = (await fixture.Service.StagePendingLinkAsync(attacker, "GitHub", GitHubPrincipal())).Data!;

        var byVictim = await fixture.Service.ConsumePendingLinkAsync(victim, "github", code);

        Assert.False(byVictim.Succeeded);
        Assert.Equal(400, byVictim.Code);
        Assert.Null(byVictim.Data);
    }

    [Fact]
    public async Task StagedLink_CompletesForTheIssuingAccount_Once()
    {
        var fixture = new Fixture();
        var owner = Guid.NewGuid();
        var code = (await fixture.Service.StagePendingLinkAsync(owner, "github", GitHubPrincipal("gh_owner", "owner-login"))).Data!;

        var completed = await fixture.Service.ConsumePendingLinkAsync(owner, "GitHub", code);
        var again = await fixture.Service.ConsumePendingLinkAsync(owner, "github", code);

        Assert.True(completed.Succeeded);
        Assert.Equal("github", completed.Data!.Provider);
        Assert.Equal("gh_owner", completed.Data.ProviderKey);
        Assert.Equal("owner-login", completed.Data.DisplayName);
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task StagedLink_RejectsAWrongCode_AndKeepsTheStagingForTheRightOne()
    {
        var fixture = new Fixture();
        var owner = Guid.NewGuid();
        var code = (await fixture.Service.StagePendingLinkAsync(owner, "github", GitHubPrincipal())).Data!;

        var wrong = await fixture.Service.ConsumePendingLinkAsync(owner, "github", "not-the-code");
        var right = await fixture.Service.ConsumePendingLinkAsync(owner, "github", code);

        Assert.False(wrong.Succeeded);
        Assert.True(right.Succeeded);
    }

    [Fact]
    public async Task StagedLink_RejectsAnotherProviderAndExpiry()
    {
        var fixture = new Fixture();
        var owner = Guid.NewGuid();
        var code = (await fixture.Service.StagePendingLinkAsync(owner, "github", GitHubPrincipal())).Data!;

        Assert.False((await fixture.Service.ConsumePendingLinkAsync(owner, "google", code)).Succeeded);

        fixture.ExpireStoredTokens();
        Assert.False((await fixture.Service.ConsumePendingLinkAsync(owner, "github", code)).Succeeded);
    }

    /// <summary>库里只存完成码的哈希：读到这张表的人换不出一枚能用的完成码。</summary>
    [Fact]
    public async Task StagedLink_StoresOnlyTheHashOfTheCode()
    {
        var fixture = new Fixture();

        var code = (await fixture.Service.StagePendingLinkAsync(Guid.NewGuid(), "github", GitHubPrincipal())).Data!;

        Assert.DoesNotContain(code, fixture.LastSavedValue!, StringComparison.Ordinal);
        Assert.Contains(OneTimeToken.Hash(code), fixture.LastSavedValue!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stage_WithoutAProviderKey_IsRejected()
    {
        var fixture = new Fixture();
        var nameless = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "x@example.com")], "GitHub"));

        var staged = await fixture.Service.StagePendingLinkAsync(Guid.NewGuid(), "github", nameless);

        Assert.False(staged.Succeeded);
        Assert.Equal(400, staged.Code);
        Assert.Empty(fixture.Tokens);
    }

    [Fact]
    public async Task Issue_RejectsEmptyUserOrProvider()
    {
        var fixture = new Fixture();

        Assert.False((await fixture.Service.IssueAsync(Guid.Empty, "github")).Succeeded);
        Assert.False((await fixture.Service.IssueAsync(Guid.NewGuid(), " ")).Succeeded);
    }

    private static ClaimsPrincipal GitHubPrincipal(string providerKey = "gh_victim", string login = "victim-login")
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, providerKey), new Claim("login", login)], "GitHub"));

    private sealed class Fixture
    {
        private readonly InMemoryAuthTokenStore _store = new();

        public OAuthLinkTokenService Service { get; }

        public string? LastSavedValue => _store.LastSavedValue;

        public IReadOnlyList<AuthToken> Tokens => _store.Tokens;

        public Fixture()
        {
            var options = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
            options.Setup(x => x.CurrentValue).Returns(new TnziIdentityOptions());

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

            Service = new OAuthLinkTokenService(serviceProvider.Object, _store.Service, options.Object);
        }

        public void ExpireStoredTokens() => _store.ExpireAll();
    }
}
