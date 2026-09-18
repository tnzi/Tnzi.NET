using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Tnzi.AspNetCore.Tests.Security.Captcha;

public class RequireCaptchaFilterTests
{
    private sealed class Body : ICaptchaProtectedRequest
    {
        public string? CaptchaToken { get; set; }
    }

    private static (RequireCaptchaFilter Filter, Mock<ICaptchaVerifier> Verifier) Create(bool enabled = true)
    {
        var verifier = new Mock<ICaptchaVerifier>();
        verifier.SetupGet(x => x.IsEnabled).Returns(enabled);
        return (new RequireCaptchaFilter("contact", verifier.Object, Mock.Of<ILogger<RequireCaptchaFilter>>()), verifier);
    }

    private static ActionExecutingContext Context(string? headerToken = null, object? body = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/contact";
        if (headerToken != null)
            http.Request.Headers[RequireCaptchaAttribute.TokenHeaderName] = headerToken;

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var arguments = new Dictionary<string, object?>();
        if (body != null) arguments["input"] = body;
        return new ActionExecutingContext(actionContext, [], arguments, controller: new object());
    }

    private static (ActionExecutionDelegate Next, Func<int> Calls) Next()
    {
        var calls = 0;
        return (() =>
        {
            calls++;
            return Task.FromResult<ActionExecutedContext>(null!);
        }, () => calls);
    }

    [Fact]
    public async Task Disabled_LetsTheRequestThrough_WithoutVerifying()
    {
        var (filter, verifier) = Create(enabled: false);
        var (next, calls) = Next();

        await filter.OnActionExecutionAsync(Context(), next);

        Assert.Equal(1, calls());
        verifier.Verify(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HeaderToken_IsVerifiedAgainstThePurpose()
    {
        var (filter, verifier) = Create();
        verifier.Setup(x => x.VerifyAsync("hdr", "contact", It.IsAny<CancellationToken>())).ReturnsAsync(CaptchaVerification.Pass("turnstile"));
        var (next, calls) = Next();

        var ctx = Context(headerToken: "hdr");
        await filter.OnActionExecutionAsync(ctx, next);

        Assert.Equal(1, calls());
        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task BodyToken_IsUsedWhenTheHeaderIsAbsent()
    {
        var (filter, verifier) = Create();
        verifier.Setup(x => x.VerifyAsync("body", "contact", It.IsAny<CancellationToken>())).ReturnsAsync(CaptchaVerification.Pass("turnstile"));
        var (next, calls) = Next();

        await filter.OnActionExecutionAsync(Context(body: new Body { CaptchaToken = "body" }), next);

        Assert.Equal(1, calls());
    }

    [Fact]
    public async Task HeaderWinsOverBody()
    {
        var (filter, verifier) = Create();
        string? seen = null;
        verifier.Setup(x => x.VerifyAsync(It.IsAny<string?>(), "contact", It.IsAny<CancellationToken>()))
            .Callback<string?, string, CancellationToken>((t, _, _) => seen = t)
            .ReturnsAsync(CaptchaVerification.Pass("turnstile"));
        var (next, _) = Next();

        await filter.OnActionExecutionAsync(Context(headerToken: "hdr", body: new Body { CaptchaToken = "body" }), next);

        Assert.Equal("hdr", seen);
    }

    [Fact]
    public async Task Rejection_ShortCircuitsWith400_CaptchaRequired_AndTheReasonInDetails()
    {
        var (filter, verifier) = Create();
        verifier.Setup(x => x.VerifyAsync(It.IsAny<string?>(), "contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Fail("turnstile", CaptchaFailure.ExpiredOrReplayed));
        var (next, calls) = Next();

        var ctx = Context(headerToken: "stale");
        await filter.OnActionExecutionAsync(ctx, next);

        Assert.Equal(0, calls());
        var bad = Assert.IsType<BadRequestObjectResult>(ctx.Result);
        var envelope = Assert.IsType<ApiResult>(bad.Value);
        Assert.Equal(400, envelope.Code);
        Assert.Equal(ErrorCodes.CAPTCHA_REQUIRED, envelope.ErrorCode);
        var details = JsonSerializer.Serialize(envelope.ErrorDetails);
        Assert.Contains("turnstile", details, StringComparison.Ordinal);
        Assert.Contains("ExpiredOrReplayed", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingToken_IsHandedToTheVerifierAsNull()
    {
        // 缺不缺令牌由验证器裁决（它知道未启用要放行），过滤器不自作主张。
        var (filter, verifier) = Create();
        verifier.Setup(x => x.VerifyAsync(null, "contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Fail("turnstile", CaptchaFailure.MissingToken));
        var (next, calls) = Next();

        var ctx = Context();
        await filter.OnActionExecutionAsync(ctx, next);

        Assert.Equal(0, calls());
        Assert.IsType<BadRequestObjectResult>(ctx.Result);
    }

    [Fact]
    public void Attribute_BuildsTheFilterFromTheContainer()
    {
        var services = new ServiceCollection();
        var verifier = new Mock<ICaptchaVerifier>();
        services.AddSingleton(verifier.Object);
        services.AddLogging();
        var attribute = new RequireCaptchaAttribute("contact");

        var filter = attribute.CreateInstance(services.BuildServiceProvider());

        Assert.IsType<RequireCaptchaFilter>(filter);
        Assert.Equal("contact", attribute.Purpose);
        Assert.False(attribute.IsReusable);
    }

    [Fact]
    public void Attribute_RejectsABlankPurpose()
    {
        Assert.ThrowsAny<ArgumentException>(() => new RequireCaptchaAttribute(" "));
    }
}
