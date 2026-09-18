using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AspNetCore.Tests.Security.Captcha;

/// <summary>
/// Altcha：出题 → 客户端算出 number → 验签。用例里真的按协议把题解出来，而不是伪造一份「看起来对」的载荷，
/// 这样它同时证明我们出的题是控件解得开的。
/// </summary>
public class AltchaCaptchaProviderTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));

    private AltchaCaptchaProvider Create(AltchaOptions? altcha = null, string? scriptUrl = null, ICache? cache = null)
    {
        var options = new CaptchaVerifierOptions
        {
            Provider = "altcha",
            ScriptUrl = scriptUrl,
            Altcha = altcha ?? new AltchaOptions { HmacKey = Key, MaxNumber = 2_000, ExpiresSeconds = 120 }
        };
        var snapshot = new Mock<IOptionsSnapshot<CaptchaVerifierOptions>>();
        snapshot.SetupGet(x => x.Value).Returns(options);
        return new AltchaCaptchaProvider(cache ?? NewCache(), snapshot.Object, Mock.Of<ILogger<AltchaCaptchaProvider>>(), _clock);
    }

    private static ICache NewCache()
    {
        var services = new ServiceCollection();
        return new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            services.BuildServiceProvider());
    }

    /// <summary>照控件的做法：从 0 数到 maxnumber，找到 sha256(salt + n) == challenge 的 n。</summary>
    private static long Solve(AltchaChallengeDto challenge)
    {
        for (long n = 0; n <= challenge.MaxNumber; n++)
        {
            var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Salt + n))).ToLowerInvariant();
            if (hex == challenge.Challenge) return n;
        }
        throw new InvalidOperationException("challenge is unsolvable");
    }

    private static string Payload(AltchaChallengeDto challenge, long number, string? signature = null, string? salt = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            algorithm = challenge.Algorithm,
            challenge = challenge.Challenge,
            number,
            salt = salt ?? challenge.Salt,
            signature = signature ?? challenge.Signature
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public async Task SolvedChallenge_Passes_AndReportsThePurpose()
    {
        var provider = Create();
        var challenge = provider.CreateChallenge("contact");

        Assert.Equal("SHA-256", challenge.Algorithm);
        Assert.Equal(2_000, challenge.MaxNumber);
        Assert.Contains("expires=", challenge.Salt, StringComparison.Ordinal);
        Assert.Contains("purpose=contact", challenge.Salt, StringComparison.Ordinal);

        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(Payload(challenge, Solve(challenge)), "contact", null));

        Assert.True(result.Passed);
        Assert.Equal("altcha", result.Provider);
        Assert.Equal("contact", result.Action);
    }

    [Fact]
    public async Task Replay_IsRejected_TheSecondTime()
    {
        var provider = Create();
        var challenge = provider.CreateChallenge("login");
        var payload = Payload(challenge, Solve(challenge));

        Assert.True((await provider.VerifyAsync(new CaptchaVerificationRequest(payload, "login", null))).Passed);
        var second = await provider.VerifyAsync(new CaptchaVerificationRequest(payload, "login", null));

        Assert.False(second.Passed);
        Assert.Equal(CaptchaFailure.ExpiredOrReplayed, second.Failure);
    }

    [Fact]
    public async Task WrongPurpose_IsActionMismatch()
    {
        var provider = Create();
        var challenge = provider.CreateChallenge("register");

        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(Payload(challenge, Solve(challenge)), "login", null));

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.ActionMismatch, result.Failure);
    }

    [Fact]
    public async Task ExpiredChallenge_IsRejected_EvenWhenSolvedCorrectly()
    {
        var provider = Create();
        var challenge = provider.CreateChallenge("login");
        var payload = Payload(challenge, Solve(challenge));

        _clock.Advance(TimeSpan.FromSeconds(121));
        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(payload, "login", null));

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.ExpiredOrReplayed, result.Failure);
    }

    [Fact]
    public async Task WrongNumber_IsRejected()
    {
        var provider = Create();
        var challenge = provider.CreateChallenge("login");

        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(Payload(challenge, Solve(challenge) + 1), "login", null));

        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task TamperedSignature_IsRejected()
    {
        var provider = Create();
        var challenge = provider.CreateChallenge("login");
        var forged = new string('0', challenge.Signature.Length);

        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(Payload(challenge, Solve(challenge), signature: forged), "login", null));

        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task ChallengeSignedWithAnotherKey_IsRejected()
    {
        // 两个实例密钥不同（多实例部署配错密钥的形态）：A 出的题在 B 验不过。
        var a = Create();
        var b = Create(new AltchaOptions { HmacKey = new string('z', 32), MaxNumber = 2_000, ExpiresSeconds = 120 });
        var challenge = a.CreateChallenge("login");

        var result = await b.VerifyAsync(new CaptchaVerificationRequest(Payload(challenge, Solve(challenge)), "login", null));

        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task RewrittenSalt_CannotExtendTheExpiry()
    {
        // 把 expires 改到未来：salt 变了 challenge 就对不上，签名也覆盖不到伪造的 salt。
        var provider = Create();
        var challenge = provider.CreateChallenge("login");
        var number = Solve(challenge);
        var far = challenge.Salt.Replace("expires=", "expires=9", StringComparison.Ordinal);

        _clock.Advance(TimeSpan.FromSeconds(121));
        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(Payload(challenge, number, salt: far), "login", null));

        Assert.False(result.Passed);
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("e30=")] // {}
    [InlineData("bnVsbA==")] // null
    public async Task MalformedPayload_IsRejected(string token)
    {
        var provider = Create();

        var result = await provider.VerifyAsync(new CaptchaVerificationRequest(token, "login", null));

        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Fact]
    public void MissingHmacKey_RefusesToIssueAChallenge()
    {
        var provider = Create(new AltchaOptions { HmacKey = null });

        Assert.Throws<ConfigurationException>(() => provider.CreateChallenge("login"));
    }

    [Fact]
    public void ClientConfig_DefaultsToJsDelivr_AndExposesThePurposeTemplate()
    {
        var config = Create().GetClientConfig();

        Assert.True(config.Enabled);
        Assert.Equal("altcha", config.Provider);
        Assert.Equal(AltchaCaptchaProvider.DefaultScriptUrl, config.ScriptUrl);
        Assert.Contains("{purpose}", config.ChallengeUrl, StringComparison.Ordinal);
        Assert.Null(config.SiteKey);
    }

    [Fact]
    public void ClientConfig_HonoursTheScriptUrlOverride()
    {
        Assert.Equal("/static/altcha.js", Create(scriptUrl: "/static/altcha.js").GetClientConfig().ScriptUrl);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
