namespace Tnzi.AspNetCore.Tests.Security.Captcha;

public class CaptchaVerifierOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(CaptchaVerifierOptions options)
        => new CaptchaVerifierOptionsValidator().Validate(null, options);

    [Fact]
    public void NoProvider_IsValid_CaptchaIsOptIn()
    {
        Assert.True(Validate(new CaptchaVerifierOptions()).Succeeded);
    }

    [Theory]
    [InlineData("recaptcha")]
    [InlineData("recaptcha-v3")]
    [InlineData("hcaptcha")]
    [InlineData("turnstile")]
    [InlineData("Turnstile")]
    public void HostedProvider_RequiresSiteKeyAndSecretKey(string provider)
    {
        var result = Validate(new CaptchaVerifierOptions { Provider = provider });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("SiteKey", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("SecretKey", StringComparison.Ordinal));
    }

    [Fact]
    public void HostedProvider_WithBothKeys_IsValid()
    {
        var result = Validate(new CaptchaVerifierOptions { Provider = "turnstile", SiteKey = "site", SecretKey = "secret" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Altcha_RequiresAnHmacKeyOfAtLeast32Chars()
    {
        Assert.Contains(Validate(new CaptchaVerifierOptions { Provider = "altcha" }).Failures!,
            f => f.Contains("Altcha.HmacKey is required", StringComparison.Ordinal));

        Assert.Contains(Validate(new CaptchaVerifierOptions { Provider = "altcha", Altcha = { HmacKey = "short" } }).Failures!,
            f => f.Contains("at least 32", StringComparison.Ordinal));

        Assert.True(Validate(new CaptchaVerifierOptions { Provider = "altcha", Altcha = { HmacKey = new string('k', 32) } }).Succeeded);
    }

    [Fact]
    public void UnknownProviderName_PassesTheValidator_RegistrationIsCheckedAtStartup()
    {
        // 消费方自注册的提供商名在这里无从判断，交给 ICaptchaVerifier.EnsureConfigured。
        Assert.True(Validate(new CaptchaVerifierOptions { Provider = "geetest" }).Succeeded);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ScoreThreshold_MustBeWithinZeroAndOne(double threshold)
    {
        Assert.Contains(Validate(new CaptchaVerifierOptions { ScoreThreshold = threshold }).Failures!,
            f => f.Contains("ScoreThreshold", StringComparison.Ordinal));
    }

    [Fact]
    public void VerifyUrl_MustBeAbsolute()
    {
        var result = Validate(new CaptchaVerifierOptions { VerifyUrl = "/relative" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("VerifyUrl", StringComparison.Ordinal));
    }

    /// <summary>
    /// 绝对但不是 http(s) 的地址同样拒掉。Linux 上 <c>/relative</c> 本身就会被解析成 <c>file:///relative</c>，
    /// 这里显式列出这种形态，让只查 <see cref="UriKind.Absolute"/> 的回归在任何系统上都变红。
    /// </summary>
    [Theory]
    [InlineData("file:///relative")]
    [InlineData("ftp://example.com/siteverify")]
    public void VerifyUrl_MustUseHttpOrHttps(string verifyUrl)
    {
        var result = Validate(new CaptchaVerifierOptions { VerifyUrl = verifyUrl });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("VerifyUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://recaptcha.net/recaptcha/api/siteverify")]
    [InlineData("http://captcha.internal:8080/siteverify")]
    public void VerifyUrl_AbsoluteHttpOrHttps_IsAccepted(string verifyUrl)
    {
        var result = Validate(new CaptchaVerifierOptions { VerifyUrl = verifyUrl });

        Assert.DoesNotContain(result.Failures ?? [], f => f.Contains("VerifyUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AltchaOptions.MaxNumberUpperBound + 1)]
    [InlineData(int.MaxValue)]
    public void AltchaMaxNumber_HasAnUpperBound(int maxNumber)
    {
        // 出题时取 MaxNumber + 1 作随机数上界：int.MaxValue 溢出成负数，出题端点每次都抛。
        Assert.Contains(Validate(new CaptchaVerifierOptions { Altcha = { MaxNumber = maxNumber } }).Failures!,
            f => f.Contains("Altcha.MaxNumber", StringComparison.Ordinal));
    }

    [Fact]
    public void AltchaMaxNumber_AtTheUpperBound_IsAccepted()
    {
        var result = Validate(new CaptchaVerifierOptions { Altcha = { MaxNumber = AltchaOptions.MaxNumberUpperBound } });
        Assert.DoesNotContain(result.Failures ?? [], f => f.Contains("Altcha.MaxNumber", StringComparison.Ordinal));
    }

    [Fact]
    public void TimeoutAndAltchaNumbers_MustBePositive()
    {
        var failures = Validate(new CaptchaVerifierOptions
        {
            TimeoutSeconds = 0,
            Altcha = { MaxNumber = 0, ExpiresSeconds = 0 }
        }).Failures!.ToList();

        Assert.Contains(failures, f => f.Contains("TimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("Altcha.MaxNumber", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("Altcha.ExpiresSeconds", StringComparison.Ordinal));
    }
}
