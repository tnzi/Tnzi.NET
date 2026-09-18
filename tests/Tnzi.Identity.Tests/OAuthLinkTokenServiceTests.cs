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

    [Fact]
    public async Task Issue_RejectsEmptyUserOrProvider()
    {
        var fixture = new Fixture();

        Assert.False((await fixture.Service.IssueAsync(Guid.Empty, "github")).Succeeded);
        Assert.False((await fixture.Service.IssueAsync(Guid.NewGuid(), " ")).Succeeded);
    }

    private sealed class Fixture
    {
        private readonly List<AuthToken> _tokens = [];

        public OAuthLinkTokenService Service { get; }

        public string? LastSavedValue { get; private set; }

        public Fixture()
        {
            var authTokenService = new Mock<IAuthTokenService>();

            authTokenService
                .Setup(x => x.SaveTokenAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()))
                .ReturnsAsync((Guid userId, string provider, string name, string value, DateTime? expiresAt, Guid _) =>
                {
                    LastSavedValue = value;
                    var token = new AuthToken
                    {
                        Id = Guid.NewGuid(), UserId = userId, LoginProvider = provider, Name = name, Value = value, ExpiresAt = expiresAt
                    };
                    _tokens.RemoveAll(t => t.UserId == userId && t.LoginProvider == provider && t.Name == name);
                    _tokens.Add(token);
                    return token.Id;
                });

            authTokenService
                .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string provider, string name, string value) =>
                    _tokens.FirstOrDefault(t => t.LoginProvider == provider && t.Name == name && t.Value == value));

            authTokenService
                .Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) =>
                {
                    var token = _tokens.FirstOrDefault(t => t.Id == id);
                    if (token == null || token.IsUsed)
                    {
                        return false;
                    }

                    token.IsUsed = true;
                    return true;
                });

            var options = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
            options.Setup(x => x.CurrentValue).Returns(new TnziIdentityOptions());

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

            Service = new OAuthLinkTokenService(serviceProvider.Object, authTokenService.Object, options.Object);
        }

        public void ExpireStoredTokens()
        {
            foreach (var token in _tokens)
            {
                token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
        }
    }
}
