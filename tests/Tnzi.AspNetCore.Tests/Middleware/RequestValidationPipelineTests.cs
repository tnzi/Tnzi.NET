using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 打开请求校验的应用必须起得来：校验器是 Scoped，中间件按请求取它，而不是在构造时从根容器取。
/// </summary>
/// <remarks>
/// ★★ <see cref="RequestValidationMiddleware"/> 是约定式中间件，由根容器构造、进程内只有一个实例；
/// 模块把 <see cref="IRequestValidator"/> 注册为 Scoped。构造注入 = 从根容器解析 Scoped 服务，
/// 开发环境（ValidateScopes）管线构建当场抛异常。刻意跑真实管线且用 Development 环境：
/// 直接 new 中间件的单测走不到这条路。
/// </remarks>
public class RequestValidationPipelineTests
{
    [Fact]
    public async Task EnabledRequestValidation_StartsAndRejectsUnstampedRequest()
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
            ["AspNetCore:RequestValidation:Enabled"] = "true"
        });

        var app = await TnziApp.CreateAsync<RequestValidationProbeStartupModule>(builder);
        await app.StartAsync();
        try
        {
            // 不带时间戳 = 校验器真的跑了才会被 400 挡下（中间件被跳过则是 200）。
            var response = await app.GetTestClient().GetAsync("/api/e2e/hello");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

/// <summary>只依赖 AspNetCore 的最小启动模块。</summary>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class RequestValidationProbeStartupModule : TnziCustomModule;
