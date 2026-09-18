using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.AspNetCore.Mvc.Conventions;
using Tnzi.AspNetCore.Mvc.Filters;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Security.Captcha;

/// <summary>
/// 把应用真的起起来跑一遍：配置 → 验证器 → 提供商 → 过滤器 → 控制器。
/// 组件各自的单测证明不了「挂在端点上的 [RequireCaptcha] 真的拿到了容器里的验证器」，
/// 也证明不了启动期那两条自检（配错即失败 / 没配即告警）真的被调用。
/// </summary>
public class CaptchaPipelineEndToEndTests
{
    private const string HmacKey = "e2e-hmac-key-e2e-hmac-key-e2e-hmac-key";

    private static Dictionary<string, string?> BaseSettings() => new()
    {
        ["Database:AutoDiscoverDbContexts"] = "false",
        ["AspNetCore:EnableForwardedHeaders"] = "false"
    };

    private static async Task<(WebApplication App, CaptureLoggerProvider Logs)> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);

        var capture = new CaptureLoggerProvider();
        CaptchaE2EStartupModule.CaptureFactory = new CaptureLoggerFactory(capture);
        try
        {
            var app = await TnziApp.CreateAsync<CaptchaE2EStartupModule>(builder);
            await app.StartAsync();
            return (app, capture);
        }
        finally
        {
            CaptchaE2EStartupModule.CaptureFactory = null;
        }
    }

    private static async Task StopAsync(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ConfiguredProviderThatNobodyRegistered_FailsStartup()
    {
        var settings = BaseSettings();
        settings["AspNetCore:Captcha:Provider"] = "geetest";

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var (app, _) = await StartAsync(settings);
            await StopAsync(app);
        });

        // 异常可能被启动管线包一层，按消息断言。
        Assert.Contains("geetest", ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("ICaptchaProvider", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoProvider_GatedEndpointLetsRequestsThrough_AndStartupNamesIt()
    {
        var (app, logs) = await StartAsync(BaseSettings());
        try
        {
            var response = await app.GetTestClient().PostAsync("/api/e2e/captcha/contact", JsonBody(new { message = "hi" }));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(logs.Snapshot(), m =>
                m.Contains("[RequireCaptcha]", StringComparison.Ordinal) && m.Contains("e2e/captcha/contact", StringComparison.Ordinal));

            // /captcha/config 如实说没启用。
            var config = await app.GetTestClient().GetFromJsonAsync<JsonElement>("/api/captcha/config");
            Assert.False(config.GetProperty("data").GetProperty("enabled").GetBoolean());
        }
        finally { await StopAsync(app); }
    }

    [Fact]
    public async Task Altcha_ChallengeIsSolvedThroughHttp_AndTheGateOpensOnce()
    {
        var settings = BaseSettings();
        settings["AspNetCore:Captcha:Provider"] = "altcha";
        settings["AspNetCore:Captcha:Altcha:HmacKey"] = HmacKey;
        settings["AspNetCore:Captcha:Altcha:MaxNumber"] = "500";

        var (app, logs) = await StartAsync(settings);
        try
        {
            var client = app.GetTestClient();

            // 没配好提供商的告警不该出现。
            Assert.DoesNotContain(logs.Snapshot(), m => m.Contains("[RequireCaptcha]", StringComparison.Ordinal));

            // 客户端配置：提供商、脚本、挑战端点模板。
            var config = (await client.GetFromJsonAsync<JsonElement>("/api/captcha/config")).GetProperty("data");
            Assert.True(config.GetProperty("enabled").GetBoolean());
            Assert.Equal("altcha", config.GetProperty("provider").GetString());
            Assert.Contains("{purpose}", config.GetProperty("challengeUrl").GetString(), StringComparison.Ordinal);

            // 没带令牌 → 400 CAPTCHA_REQUIRED。
            var denied = await client.PostAsync("/api/e2e/captcha/contact", JsonBody(new { message = "hi" }));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            var deniedBody = await denied.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("CAPTCHA_REQUIRED", deniedBody.GetProperty("errorCode").GetString());

            // 领题、解题、带令牌提交 → 200。
            // ★ 出题端点回的是裸的 Altcha 文档，不是 ApiResult 信封：这个地址的消费方是 <altcha-widget> 控件本身，
            // 它按「有没有 challenge 键」校验响应体，信封会被它判成 Challenge validation failed。
            // 这里刻意不拆 data：此前这条测试自己拆信封，于是控件在浏览器里拿不到题时它照样全绿。
            var challengeResponse = await client.GetAsync("/api/captcha/altcha/challenge?purpose=contact");
            Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
            Assert.Contains("json", challengeResponse.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
            var challenge = await challengeResponse.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var key in new[] { "algorithm", "challenge", "maxnumber", "salt", "signature" })
                Assert.True(challenge.TryGetProperty(key, out _), $"bare Altcha document must carry '{key}'");
            foreach (var envelopeKey in new[] { "code", "success", "succeeded", "data" })
                Assert.False(challenge.TryGetProperty(envelopeKey, out _), $"challenge document must not be wrapped ('{envelopeKey}' present)");
            Assert.Equal("SHA-256", challenge.GetProperty("algorithm").GetString());
            var payload = Solve(challenge);

            var viaHeader = new HttpRequestMessage(HttpMethod.Post, "/api/e2e/captcha/contact") { Content = JsonBody(new { message = "hi" }) };
            viaHeader.Headers.Add(RequireCaptchaAttribute.TokenHeaderName, payload);
            var ok = await client.SendAsync(viaHeader);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

            // 同一枚令牌第二次 → 重放，拒绝。
            var replay = await client.PostAsync("/api/e2e/captcha/contact", JsonBody(new { message = "hi", captchaToken = payload }));
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

            // 模型验证先于验证码：一个缺必填字段的请求被 400 拒掉时，令牌不能被烧掉，补齐字段后同一枚仍然能过。
            var fresh = Solve(await client.GetFromJsonAsync<JsonElement>("/api/captcha/altcha/challenge?purpose=contact"));
            var invalid = await client.PostAsync("/api/e2e/captcha/contact", JsonBody(new { captchaToken = fresh }));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            // 框架的模型验证信封不带 errorCode；这里要证明的是 400 来自验证而不是验证码闸门。
            var invalidBody = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEqual("CAPTCHA_REQUIRED", invalidBody.GetProperty("errorCode").GetString());
            Assert.Contains("Message", invalidBody.GetProperty("message").GetString(), StringComparison.Ordinal);
            var afterValidation = await client.PostAsync("/api/e2e/captcha/contact", JsonBody(new { message = "hi", captchaToken = fresh }));
            Assert.Equal(HttpStatusCode.OK, afterValidation.StatusCode);

            // 为别的用途领的题不能用在这里。
            var other = await client.GetFromJsonAsync<JsonElement>("/api/captcha/altcha/challenge?purpose=login");
            var wrongPurpose = await client.PostAsync("/api/e2e/captcha/contact", JsonBody(new { message = "hi", captchaToken = Solve(other) }));
            Assert.Equal(HttpStatusCode.BadRequest, wrongPurpose.StatusCode);
        }
        finally { await StopAsync(app); }
    }

    [Fact]
    public async Task AltchaChallengeEndpoint_Is404WhenAltchaIsNotTheActiveProvider()
    {
        var settings = BaseSettings();
        settings["AspNetCore:Captcha:Provider"] = "turnstile";
        settings["AspNetCore:Captcha:SiteKey"] = "site";
        settings["AspNetCore:Captcha:SecretKey"] = "secret";

        var (app, _) = await StartAsync(settings);
        try
        {
            var response = await app.GetTestClient().GetAsync("/api/captcha/altcha/challenge?purpose=login");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            // 失败仍是带真实状态码的信封（控件对非 2xx 一律按失败处理，框架客户端读得到错误码）。
            var notFoundBody = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(404, notFoundBody.GetProperty("code").GetInt32());
            Assert.False(notFoundBody.GetProperty("success").GetBoolean());

            var config = (await app.GetTestClient().GetFromJsonAsync<JsonElement>("/api/captcha/config")).GetProperty("data");
            Assert.Equal("turnstile", config.GetProperty("provider").GetString());
            Assert.Equal("site", config.GetProperty("siteKey").GetString());
            Assert.StartsWith("https://challenges.cloudflare.com/", config.GetProperty("scriptUrl").GetString(), StringComparison.Ordinal);
        }
        finally { await StopAsync(app); }
    }

    private static StringContent JsonBody(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string Solve(JsonElement challenge)
    {
        var salt = challenge.GetProperty("salt").GetString()!;
        var target = challenge.GetProperty("challenge").GetString()!;
        var max = challenge.GetProperty("maxnumber").GetInt32();
        long? number = null;
        for (long n = 0; n <= max; n++)
        {
            if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + n))).ToLowerInvariant() == target)
            {
                number = n;
                break;
            }
        }
        Assert.NotNull(number);

        var json = JsonSerializer.Serialize(new
        {
            algorithm = challenge.GetProperty("algorithm").GetString(),
            challenge = target,
            number = number.Value,
            salt,
            signature = challenge.GetProperty("signature").GetString()
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private sealed class CaptureLoggerFactory(CaptureLoggerProvider provider) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider loggerProvider)
        {
        }

        public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(_messages);

        public List<string> Snapshot()
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                {
                    sink.Add(formatter(state, exception));
                }
            }
        }
    }
}

/// <summary>只依赖 AspNetCore 的最小启动模块；控制器住在本测试程序集里，由模块程序集扫描发现。</summary>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class CaptchaE2EStartupModule : TnziCustomModule
{
    internal static ILoggerFactory? CaptureFactory;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 与 HostingModule 同一句：激活 [DefaultController]（DefaultCaptchaController 就是一个），其余保持最小模块图。
        context.Services.AddSingleton<DefaultControllerEnabledMarker>();

        if (CaptureFactory != null)
        {
            context.Services.RemoveAll<ILoggerFactory>();
            context.Services.AddSingleton(CaptureFactory);
        }
        return Task.CompletedTask;
    }
}

/// <summary>一个匿名可达、挂了 [RequireCaptcha] 的写端点。</summary>
[ApiController]
[Route("e2e/captcha")]
[AllowAnonymous]
public sealed class CaptchaE2EController : ApiControllerBase
{
    public sealed class ContactInput : ICaptchaProtectedRequest
    {
        [Required]
        public string? Message { get; set; }

        public string? CaptchaToken { get; set; }
    }

    [HttpPost("contact")]
    [RequireCaptcha("contact")]
    public ApiResult<string> Contact([FromBody] ContactInput input) => Ok($"received: {input.Message}", "Success");
}
