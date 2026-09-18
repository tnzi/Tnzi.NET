using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 体日志开着时，真实管线里的响应必须原样到达客户端。
/// 直通采集流替换的是 <c>Response.Body</c>，而服务器把它包进 <c>StreamResponseBodyFeature</c>
/// 再交给 MVC 的结果执行器 —— 这一层只有起真实 TestServer 才走得到。
/// </summary>
public class BodyLoggingPipelineTests
{
    [Fact]
    public async Task WithBothBodySwitchesOn_TheResponseStillReachesTheClientIntact()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:RequestTracking:LogRequestBody"] = "true",
            ["AspNetCore:RequestTracking:LogResponseBody"] = "true"
        });

        var app = await TnziApp.CreateAsync<BodyLoggingPipelineStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/api/e2e/body-echo", new { text = "round-trip" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("round-trip", envelope.RootElement.GetProperty("data").GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

[DependsOn(typeof(AspNetCoreModule))]
public sealed class BodyLoggingPipelineStartupModule : TnziCustomModule
{
}

public sealed class BodyEchoRequest
{
    public string Text { get; set; } = string.Empty;
}

[ApiController]
[Route("e2e/body-echo")]
public sealed class BodyEchoController : ApiControllerBase
{
    [HttpPost]
    public ApiResult<string> Post([FromBody] BodyEchoRequest request) => Ok(request.Text, "Success");
}
