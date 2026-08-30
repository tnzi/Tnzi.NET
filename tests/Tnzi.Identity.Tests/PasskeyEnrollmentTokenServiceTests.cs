using System.Security.Cryptography;
using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// Passkey 注册令牌原语：一次性、绑定目标身份、可配有效期、消费即失效。
/// </summary>
public class PasskeyEnrollmentTokenServiceTests
{
    [Fact]
    public async Task Issue_ShouldStoreOnlyTheHash_NeverThePlaintext()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();

        var issued = (await fixture.Service.IssueAsync(userId)).Data!;

        // ★ 库里存的必须是哈希：拿到数据库读权限的人不该顺手拿到一批可用的注册令牌。
        Assert.NotEqual(issued.Token, fixture.LastSavedValue);
        Assert.Equal(Sha256Hex(issued.Token), fixture.LastSavedValue);
        Assert.Equal(userId, issued.UserId);
    }

    [Fact]
    public async Task Validate_ShouldReturnTargetUser_AndNotConsume()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        var issued = (await fixture.Service.IssueAsync(userId)).Data!;

        var first = await fixture.Service.ValidateAsync(issued.Token);
        var second = await fixture.Service.ValidateAsync(issued.Token);

        // ★ 校验不消费：WebAuthn 是两段式的，begin 校验一次、complete 再校验一次，
        // 合成一步会让用户在系统弹窗上点取消白烧掉一枚令牌。
        Assert.True(first.Succeeded);
        Assert.Equal(userId, first.Data);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task Consume_ShouldInvalidateTheToken()
    {
        var fixture = new Fixture();
        var issued = (await fixture.Service.IssueAsync(Guid.NewGuid())).Data!;

        await fixture.Service.ConsumeAsync(issued.Token);
        var afterConsume = await fixture.Service.ValidateAsync(issued.Token);

        Assert.False(afterConsume.Succeeded);
    }

    [Fact]
    public async Task Consume_ShouldBeIdempotent()
    {
        var fixture = new Fixture();
        var issued = (await fixture.Service.IssueAsync(Guid.NewGuid())).Data!;

        await fixture.Service.ConsumeAsync(issued.Token);
        var again = await fixture.Service.ConsumeAsync(issued.Token);

        // 幂等：调用方不必自己判重。真正的守卫在 Validate 上。
        Assert.True(again.Succeeded);
    }

    [Fact]
    public async Task Validate_ShouldRejectExpiredToken()
    {
        var fixture = new Fixture();
        var issued = (await fixture.Service.IssueAsync(Guid.NewGuid(), TimeSpan.FromMinutes(5))).Data!;

        fixture.ExpireStoredToken();
        var result = await fixture.Service.ValidateAsync(issued.Token);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task Validate_ShouldRejectGarbage_WithTheSameAnswerAsAnExpiredToken(string token)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ValidateAsync(token);

        // 失效 / 过期 / 不存在共用同一个回答 —— 区分开就是在帮人试探哪些令牌是真的。
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task Issue_ShouldRejectEmptyUserId()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.IssueAsync(Guid.Empty);

        Assert.False(result.Succeeded);
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// 用一个记账式的 <see cref="IAuthTokenService"/> 替身，好断言"到底存进去了什么"。
    /// </summary>
    private sealed class Fixture
    {
        private readonly List<AuthToken> _tokens = [];

        public PasskeyEnrollmentTokenService Service { get; }

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
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        LoginProvider = provider,
                        Name = name,
                        Value = value,
                        ExpiresAt = expiresAt
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
                    if (token == null)
                    {
                        return false;
                    }

                    token.IsUsed = true;
                    return true;
                });

            var options = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
            options.Setup(x => x.CurrentValue).Returns(new TnziIdentityOptions());

            Service = new PasskeyEnrollmentTokenService(
                BuildServiceProvider(),
                authTokenService.Object,
                options.Object);
        }

        public void ExpireStoredToken()
        {
            foreach (var token in _tokens)
            {
                token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
        }

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
