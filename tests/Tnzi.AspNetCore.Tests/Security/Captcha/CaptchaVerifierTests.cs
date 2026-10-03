namespace Tnzi.AspNetCore.Tests.Security.Captcha;

public class CaptchaVerifierTests
{
    private static CaptchaVerifier Create(CaptchaVerifierOptions options, IEnumerable<ICaptchaProvider>? providers = null, string? clientIp = null)
    {
        var snapshot = new Mock<IOptionsSnapshot<CaptchaVerifierOptions>>();
        snapshot.SetupGet(x => x.Value).Returns(options);
        IScopedContext? scoped = null;
        if (clientIp != null)
        {
            var ctx = new Mock<IScopedContext>();
            ctx.SetupGet(x => x.ClientIpAddress).Returns(clientIp);
            scoped = ctx.Object;
        }
        return new CaptchaVerifier(snapshot.Object, providers ?? [], Mock.Of<ILogger<CaptchaVerifier>>(), scoped);
    }

    private static Mock<ICaptchaProvider> Provider(string name, CaptchaVerification? answer = null)
    {
        var p = new Mock<ICaptchaProvider>();
        p.SetupGet(x => x.Name).Returns(name);
        p.Setup(x => x.VerifyAsync(It.IsAny<CaptchaVerificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer ?? CaptchaVerification.Pass(name));
        return p;
    }

    [Fact]
    public async Task NotConfigured_PassesEverything_AndReportsSkipped()
    {
        var verifier = Create(new CaptchaVerifierOptions());

        Assert.False(verifier.IsEnabled);
        Assert.Null(verifier.ProviderName);

        var result = await verifier.VerifyAsync(null, "login");
        Assert.True(result.Passed);
        Assert.True(result.Skipped);
        Assert.False(verifier.GetClientConfig().Enabled);
    }

    [Fact]
    public async Task Configured_MissingToken_IsRejectedWithoutAskingTheProvider()
    {
        var provider = Provider("turnstile");
        var verifier = Create(new CaptchaVerifierOptions { Provider = "turnstile" }, [provider.Object]);

        var result = await verifier.VerifyAsync("  ", "login");

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.MissingToken, result.Failure);
        provider.Verify(x => x.VerifyAsync(It.IsAny<CaptchaVerificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OversizedToken_IsRejectedWithoutAskingTheProvider_EvenUnderTheAllowPolicy()
    {
        // 令牌完全由匿名客户端决定；超长的只可能是构造出来的，不该出站到验证服务。
        var provider = Provider("turnstile", CaptchaVerification.Fail("turnstile", CaptchaFailure.VerifierUnavailable, "HTTP 503"));
        var verifier = Create(new CaptchaVerifierOptions { Provider = "turnstile", OnVerifierUnavailable = CaptchaUnavailablePolicy.Allow }, [provider.Object]);

        var result = await verifier.VerifyAsync(new string('a', CaptchaVerifier.MaxTokenLength + 1), "login");

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
        provider.Verify(x => x.VerifyAsync(It.IsAny<CaptchaVerificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TokenAtTheLengthLimit_IsStillHandedToTheProvider()
    {
        var provider = Provider("turnstile");
        var verifier = Create(new CaptchaVerifierOptions { Provider = "turnstile" }, [provider.Object]);

        var result = await verifier.VerifyAsync(new string('a', CaptchaVerifier.MaxTokenLength), "login");

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task DispatchesToTheConfiguredProvider_CaseInsensitively_WithPurposeAndClientIp()
    {
        CaptchaVerificationRequest? seen = null;
        var provider = Provider("Turnstile");
        provider.Setup(x => x.VerifyAsync(It.IsAny<CaptchaVerificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CaptchaVerificationRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(CaptchaVerification.Pass("Turnstile"));
        var other = Provider("hcaptcha");

        var verifier = Create(new CaptchaVerifierOptions { Provider = "turnstile" }, [other.Object, provider.Object], clientIp: "203.0.113.9");

        var result = await verifier.VerifyAsync("tok", "contact");

        Assert.True(result.Passed);
        Assert.NotNull(seen);
        Assert.Equal("tok", seen!.Token);
        Assert.Equal("contact", seen.Purpose);
        Assert.Equal("203.0.113.9", seen.RemoteIp);
        other.Verify(x => x.VerifyAsync(It.IsAny<CaptchaVerificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenTheNamedProviderIsNotRegistered()
    {
        var verifier = Create(new CaptchaVerifierOptions { Provider = "geetest" }, [Provider("turnstile").Object]);

        var ex = Assert.Throws<ConfigurationException>(() => verifier.EnsureConfigured());
        Assert.Contains("geetest", ex.Message, StringComparison.Ordinal);
        Assert.Contains("turnstile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureConfigured_IsSilentWhenNotConfiguredOrWhenRegistered()
    {
        Create(new CaptchaVerifierOptions()).EnsureConfigured();
        Create(new CaptchaVerifierOptions { Provider = "turnstile" }, [Provider("turnstile").Object]).EnsureConfigured();
    }

    [Fact]
    public async Task UnregisteredProviderAtRuntime_FailsClosed()
    {
        var verifier = Create(new CaptchaVerifierOptions { Provider = "geetest" });

        var result = await verifier.VerifyAsync("tok", "login");

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.ProviderNotRegistered, result.Failure);
    }

    [Fact]
    public async Task VerifierUnavailable_DefaultsToDeny()
    {
        var provider = Provider("turnstile", CaptchaVerification.Fail("turnstile", CaptchaFailure.VerifierUnavailable, "timeout"));
        var verifier = Create(new CaptchaVerifierOptions { Provider = "turnstile" }, [provider.Object]);

        var result = await verifier.VerifyAsync("tok", "login");

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.VerifierUnavailable, result.Failure);
    }

    [Fact]
    public async Task VerifierUnavailable_WithAllowPolicy_PassesButKeepsTheReasonInDetail()
    {
        var provider = Provider("turnstile", CaptchaVerification.Fail("turnstile", CaptchaFailure.VerifierUnavailable, "timeout"));
        var verifier = Create(
            new CaptchaVerifierOptions { Provider = "turnstile", OnVerifierUnavailable = CaptchaUnavailablePolicy.Allow },
            [provider.Object]);

        var result = await verifier.VerifyAsync("tok", "login");

        Assert.True(result.Passed);
        Assert.False(result.Skipped);
        Assert.Contains("timeout", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowPolicy_DoesNotRescueOrdinaryRejections()
    {
        var provider = Provider("turnstile", CaptchaVerification.Fail("turnstile", CaptchaFailure.Rejected));
        var verifier = Create(
            new CaptchaVerifierOptions { Provider = "turnstile", OnVerifierUnavailable = CaptchaUnavailablePolicy.Allow },
            [provider.Object]);

        var result = await verifier.VerifyAsync("tok", "login");

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Fact]
    public void DuplicateNames_LastRegistrationWins()
    {
        var builtIn = Provider("turnstile");
        builtIn.Setup(x => x.GetClientConfig()).Returns(new CaptchaClientConfigDto { SiteKey = "built-in" });
        var custom = Provider("turnstile");
        custom.Setup(x => x.GetClientConfig()).Returns(new CaptchaClientConfigDto { SiteKey = "custom" });

        var verifier = Create(new CaptchaVerifierOptions { Provider = "turnstile" }, [builtIn.Object, custom.Object]);

        Assert.Equal("custom", verifier.GetClientConfig().SiteKey);
    }

    [Fact]
    public void GetClientConfig_FillsEnabledAndProviderEvenWhenTheProviderReturnsNothing()
    {
        var provider = Provider("image");
        provider.Setup(x => x.GetClientConfig()).Returns((CaptchaClientConfigDto?)null);
        var verifier = Create(new CaptchaVerifierOptions { Provider = "image" }, [provider.Object]);

        var config = verifier.GetClientConfig();

        Assert.True(config.Enabled);
        Assert.Equal("image", config.Provider);
    }
}
