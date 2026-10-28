using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Tnzi.AspNetCore.Mvc.Filters;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// <c>[AjaxOnly]</c> 必须真的把 <see cref="AjaxOnlyFilter"/> 带进管线。
/// </summary>
/// <remarks>
/// 特性曾经是一个裸 <c>Attribute</c>：过滤器只注册进容器、不在全局过滤器里，也没有任何东西读这个特性，
/// 于是标了它的端点照样接受跨站表单提交与普通导航，且没有任何信号。只有走真实 MVC 管线才证得出这一点。
/// </remarks>
public class AjaxOnlyAttributePipelineTests
{
    [Fact]
    public async Task MarkedAction_WithoutTheHeader_IsRejected()
    {
        await RunAsync(async client =>
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/e2e/ajax-only/marked")).StatusCode));
    }

    [Fact]
    public async Task MarkedAction_WithTheHeader_Runs()
    {
        await RunAsync(async client =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/e2e/ajax-only/marked");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        });
    }

    [Fact]
    public async Task MarkedAction_WithTheValueOnlyInTheQueryString_IsRejected()
    {
        // 查询串是跨站导航能带上的；只有自定义头才证明请求来自脚本。
        await RunAsync(async client =>
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/e2e/ajax-only/marked?X-Requested-With=XMLHttpRequest")).StatusCode));
    }

    [Fact]
    public async Task UnmarkedAction_IsUnaffected()
    {
        await RunAsync(async client =>
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/ajax-only/unmarked")).StatusCode));
    }

    [Fact]
    public async Task ClassLevelMark_AppliesToEveryAction()
    {
        await RunAsync(async client =>
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/e2e/ajax-only-class/submit", null)).StatusCode));
    }

    private static async Task RunAsync(Func<HttpClient, Task> assertAsync)
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

        var app = await TnziApp.CreateAsync<AjaxOnlyProbeStartupModule>(builder);
        await app.StartAsync();
        try
        {
            await assertAsync(app.GetTestClient());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

[DependsOn(typeof(AspNetCoreModule))]
public sealed class AjaxOnlyProbeStartupModule : TnziCustomModule
{
}

[ApiController]
[Route("e2e/ajax-only")]
public sealed class AjaxOnlyProbeController : ApiControllerBase
{
    [AjaxOnly]
    [HttpGet("marked")]
    public ApiResult<string> Marked() => Ok("marked", "Success");

    [HttpGet("unmarked")]
    public ApiResult<string> Unmarked() => Ok("unmarked", "Success");
}

[ApiController]
[AjaxOnly]
[Route("e2e/ajax-only-class")]
public sealed class AjaxOnlyClassProbeController : ApiControllerBase
{
    [HttpPost("submit")]
    public ApiResult<string> Submit() => Ok("submitted", "Success");
}
