using System.Text;
using System.Net;

namespace Tnzi.AspNetCore.Tests.Security.Captcha;

/// <summary>
/// siteverify 协议适配器：四家共用，所以按协议断言（请求形状、error-codes 的翻译、策略），不逐家重复。
/// </summary>
public class SiteVerifyCaptchaProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") });

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await Respond(request);
        }
    }

    private static (SiteVerifyCaptchaProvider Provider, FakeHandler Handler) Create(
        SiteVerifyCaptchaDescriptor descriptor,
        CaptchaVerifierOptions? options = null)
    {
        var handler = new FakeHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        options ??= new CaptchaVerifierOptions { Provider = descriptor.Name, SiteKey = "site-key", SecretKey = "secret-key" };
        var snapshot = new Mock<IOptionsSnapshot<CaptchaVerifierOptions>>();
        snapshot.SetupGet(x => x.Value).Returns(options);

        return (new SiteVerifyCaptchaProvider(descriptor, factory.Object, snapshot.Object, Mock.Of<ILogger<SiteVerifyCaptchaProvider>>()), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static CaptchaVerificationRequest Request(string purpose = "login", string? ip = "198.51.100.7")
        => new("client-token", purpose, ip);

    [Fact]
    public async Task PostsSecretResponseAndRemoteIp_AsFormToTheDescriptorUrl()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile);

        var result = await provider.VerifyAsync(Request());

        Assert.True(result.Passed);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(SiteVerifyCaptchaDescriptor.Turnstile.VerifyUrl, handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("secret=secret-key", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("response=client-token", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("remoteip=198.51.100.7", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sitekey=", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HCaptcha_AlsoSendsTheSiteKey()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.HCaptcha);

        await provider.VerifyAsync(Request());

        Assert.Contains("sitekey=site-key", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyUrlOverride_IsUsed_ForExampleRecaptchaNet()
    {
        var options = new CaptchaVerifierOptions
        {
            Provider = "recaptcha", SiteKey = "s", SecretKey = "k",
            VerifyUrl = "https://recaptcha.net/recaptcha/api/siteverify"
        };
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.ReCaptcha, options);

        await provider.VerifyAsync(Request());

        Assert.Equal("https://recaptcha.net/recaptcha/api/siteverify", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SuccessFalse_WithTimeoutOrDuplicate_IsExpiredOrReplayed()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.ReCaptcha);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}"));

        var result = await provider.VerifyAsync(Request());

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.ExpiredOrReplayed, result.Failure);
        Assert.Contains("timeout-or-duplicate", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessFalse_Otherwise_IsRejected()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.HCaptcha);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":false,\"error-codes\":[\"invalid-input-response\"]}"));

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientNon2xx_IsVerifierUnavailable_NotRejected(HttpStatusCode status)
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile);
        handler.Respond = _ => Task.FromResult(new HttpResponseMessage(status));

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.VerifierUnavailable, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.Found)]
    public async Task OtherNon2xx_IsRejected_SoTheAllowPolicyCannotRescueIt(HttpStatusCode status)
    {
        // 4xx 说的是「这个请求本身不对」：若它算「不可达」，OnVerifierUnavailable=Allow 下
        // 一枚构造出来让验证服务回 400 的令牌（例如超长）就等于通行证。
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile);
        handler.Respond = _ => Task.FromResult(new HttpResponseMessage(status));

        var result = await provider.VerifyAsync(Request());

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task NetworkFailure_IsVerifierUnavailable()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile);
        handler.Respond = _ => throw new HttpRequestException("dns");

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.VerifierUnavailable, result.Failure);
    }

    [Fact]
    public async Task Timeout_IsVerifierUnavailable()
    {
        var options = new CaptchaVerifierOptions { Provider = "turnstile", SiteKey = "s", SecretKey = "k", TimeoutSeconds = 1 };
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile, options);
        handler.Respond = async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return Json("{\"success\":true}");
        };

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.VerifierUnavailable, result.Failure);
        Assert.Equal("Timeout", result.Detail);
    }

    [Fact]
    public async Task GarbageBody_IsVerifierUnavailable()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile);
        handler.Respond = _ => Task.FromResult(Json("<html>maintenance</html>"));

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.VerifierUnavailable, result.Failure);
    }

    [Fact]
    public async Task ReCaptchaV3_ScoreBelowThreshold_IsLowScore()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.ReCaptchaV3);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"score\":0.2,\"action\":\"login\",\"hostname\":\"example.test\"}"));

        var result = await provider.VerifyAsync(Request());

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.LowScore, result.Failure);
        Assert.Equal(0.2, result.Score);
    }

    [Fact]
    public async Task ReCaptchaV3_ScoreAtOrAboveThreshold_Passes_AndReportsTheScore()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.ReCaptchaV3);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"score\":0.9,\"action\":\"login\",\"hostname\":\"example.test\"}"));

        var result = await provider.VerifyAsync(Request());

        Assert.True(result.Passed);
        Assert.Equal(0.9, result.Score);
        Assert.Equal("login", result.Action);
        Assert.Equal("example.test", result.Hostname);
    }

    [Fact]
    public async Task ReCaptchaV2_IgnoresAScoreItNeverReports()
    {
        // v2 描述符不出评分：即便端点回了一个 score 字段也不拿它拒人。
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.ReCaptcha);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"score\":0.1}"));

        var result = await provider.VerifyAsync(Request());

        Assert.True(result.Passed);
        Assert.Null(result.Score);
    }

    [Fact]
    public async Task ActionMismatch_IsRejected_WhenEnforced()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"action\":\"register\"}"));

        var result = await provider.VerifyAsync(Request(purpose: "login"));

        Assert.False(result.Passed);
        Assert.Equal(CaptchaFailure.ActionMismatch, result.Failure);
        Assert.Equal("register", result.Action);
    }

    [Fact]
    public async Task ActionComparison_FoldsHyphenAndUnderscore_ForReCaptchaV3()
    {
        // The client sends password_recovery because Google refuses hyphens in v3 action names.
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.ReCaptchaV3);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"score\":0.9,\"action\":\"password_recovery\"}"));

        var result = await provider.VerifyAsync(Request(purpose: "password-recovery"));

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task HCaptcha_ScoreIsNotInterpreted_ItIsARiskScoreNotAHumanScore()
    {
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.HCaptcha);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"score\":0.05}"));

        var result = await provider.VerifyAsync(Request());

        Assert.True(result.Passed);
        Assert.Null(result.Score);
    }

    [Fact]
    public async Task ActionMismatch_IsTolerated_WhenEnforceActionIsOff()
    {
        var options = new CaptchaVerifierOptions { Provider = "turnstile", SiteKey = "s", SecretKey = "k", EnforceAction = false };
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile, options);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"action\":\"register\"}"));

        var result = await provider.VerifyAsync(Request(purpose: "login"));

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task HostnameOutsideTheAllowList_IsRejected()
    {
        var options = new CaptchaVerifierOptions
        {
            Provider = "hcaptcha", SiteKey = "s", SecretKey = "k",
            ExpectedHostnames = ["app.example.test"]
        };
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.HCaptcha, options);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"hostname\":\"evil.example\"}"));

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.HostnameMismatch, result.Failure);
        Assert.Equal("evil.example", result.Hostname);
    }

    [Fact]
    public async Task HostnameInsideTheAllowList_Passes_CaseInsensitively()
    {
        var options = new CaptchaVerifierOptions
        {
            Provider = "hcaptcha", SiteKey = "s", SecretKey = "k",
            ExpectedHostnames = ["App.Example.Test"]
        };
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.HCaptcha, options);
        handler.Respond = _ => Task.FromResult(Json("{\"success\":true,\"hostname\":\"app.example.test\"}"));

        var result = await provider.VerifyAsync(Request());

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task MissingSecret_RejectsWithoutCallingTheEndpoint()
    {
        var options = new CaptchaVerifierOptions { Provider = "turnstile", SiteKey = "s" };
        var (provider, handler) = Create(SiteVerifyCaptchaDescriptor.Turnstile, options);

        var result = await provider.VerifyAsync(Request());

        Assert.Equal(CaptchaFailure.Rejected, result.Failure);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public void ClientConfig_UsesTheDescriptorScript_AndSubstitutesTheSiteKey()
    {
        var (v3, _) = Create(SiteVerifyCaptchaDescriptor.ReCaptchaV3);
        var (turnstile, _) = Create(SiteVerifyCaptchaDescriptor.Turnstile);

        var v3Config = v3.GetClientConfig();
        var turnstileConfig = turnstile.GetClientConfig();

        Assert.True(v3Config.Enabled);
        Assert.Equal("recaptcha-v3", v3Config.Provider);
        Assert.Equal("site-key", v3Config.SiteKey);
        Assert.Equal("https://www.google.com/recaptcha/api.js?render=site-key", v3Config.ScriptUrl);
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit", turnstileConfig.ScriptUrl);
        Assert.Null(turnstileConfig.ChallengeUrl);
    }

    [Fact]
    public void ClientConfig_HonoursTheScriptUrlOverride()
    {
        var options = new CaptchaVerifierOptions { Provider = "turnstile", SiteKey = "s", SecretKey = "k", ScriptUrl = "https://cdn.example.test/turnstile.js" };
        var (provider, _) = Create(SiteVerifyCaptchaDescriptor.Turnstile, options);

        Assert.Equal("https://cdn.example.test/turnstile.js", provider.GetClientConfig().ScriptUrl);
    }
}
