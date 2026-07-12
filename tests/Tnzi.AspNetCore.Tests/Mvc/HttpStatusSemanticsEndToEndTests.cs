using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Hosting;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 端到端锁定 Web 层 HTTP 语义修复：
/// - 任务 1：控制器返回失败信封时 HTTP 状态码 == body.code（不再恒 200）。
/// - 任务 2：模型验证失败返回统一 ApiResult 信封（有 code、succeeded=false）+ 真实 400，
///   而非 [ApiController] 内置的 RFC7807 ProblemDetails（无 code，令前端误判成功）。
/// </summary>
public class HttpStatusSemanticsEndToEndTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false"
        });

        var app = await TnziApp.CreateAsync<TestHttpSemanticsStartupModule>(builder);
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task FailureEnvelope_CarriesRealHttpStatusCode()
    {
        var app = await StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/api/e2e/http-semantics/not-found");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(404, doc.RootElement.GetProperty("code").GetInt32());
            Assert.False(doc.RootElement.GetProperty("succeeded").GetBoolean());
        }
        finally { await StopAsync(app); }
    }

    [Fact]
    public async Task SuccessEnvelope_Returns200()
    {
        var app = await StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/api/e2e/http-semantics/ok");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(200, doc.RootElement.GetProperty("code").GetInt32());
            Assert.True(doc.RootElement.GetProperty("succeeded").GetBoolean());
        }
        finally { await StopAsync(app); }
    }

    [Fact]
    public async Task ValidationFailure_ReturnsEnvelopeShapeAndReal400()
    {
        var app = await StartAsync();
        try
        {
            // Name 是 [Required]，故意缺省 → 模型验证失败
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await app.GetTestClient().PostAsync("/api/e2e/http-semantics/validate", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            // 信封形状：有 code 字段（值 400）+ succeeded=false
            Assert.True(doc.RootElement.TryGetProperty("code", out var codeEl));
            Assert.Equal(400, codeEl.GetInt32());
            Assert.False(doc.RootElement.GetProperty("succeeded").GetBoolean());
            // 不是 RFC7807 ProblemDetails（其 type 字段指向 IETF 文档且无 code）
            Assert.DoesNotContain("tools.ietf.org", body);
        }
        finally { await StopAsync(app); }
    }

    private static async Task StopAsync(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

[DependsOn(typeof(Tnzi.System.SystemModule))]
public sealed class TestHttpSemanticsStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);
        return base.ConfigureServicesAsync(context);
    }
}

[ApiController]
[Route("e2e/http-semantics")]
public sealed class HttpSemanticsController : ApiControllerBase
{
    [HttpGet("ok")]
    public ApiResult<string> GetOk() => Ok("hi", "Success");

    [HttpGet("not-found")]
    public ApiResult<string> Missing() => NotFound<string>("missing");

    [HttpPost("validate")]
    public ApiResult<string> Validate([FromBody] HttpSemanticsValidateDto input) => Ok(input.Name, "Success");
}

public sealed class HttpSemanticsValidateDto
{
    [Required]
    public string Name { get; set; } = null!;
}
