using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tnzi.Modules;

namespace Tnzi.Swagger.Tests;

/// <summary>
/// 装上 <see cref="SwaggerModule"/> 的真实 TestServer：一个默认分组控制器 + 一个 admin 分组控制器。
/// UI 页面里的地址与序列化出来的文档，只有走一遍真实管道才看得到
/// </summary>
internal static class SwaggerTestHost
{
    public static async Task<WebApplication> StartAsync(IDictionary<string, string?> settings, string? pathBase = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddControllers().AddApplicationPart(typeof(SwaggerTestHost).Assembly);

        var module = new SwaggerModule();
        var configurationContext = new ServiceConfigurationContext(builder.Services, builder.Configuration);
        await module.PreConfigureServicesAsync(configurationContext);
        await module.ConfigureServicesAsync(configurationContext);

        var app = builder.Build();
        if (pathBase != null)
        {
            app.UsePathBase(pathBase);
            // 像挂在子路径下的应用一样：子路径之外的地址根本到不了这里
            app.Use(async (context, next) =>
            {
                if (!context.Request.PathBase.HasValue)
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                await next(context);
            });
            // 路由要在剥掉 PathBase 之后再匹配
            app.UseRouting();
        }

        await module.OnApplicationInitializationAsync(new ApplicationInitializationContext(app.Services, app, app.Environment, app));
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    public static async Task<JsonElement> GetDocumentAsync(WebApplication app, string documentName)
    {
        var response = await app.GetTestClient().GetAsync($"/swagger/{documentName}/swagger.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}

[ApiController]
[Route("probe")]
public class ProbeController : ControllerBase
{
    [HttpGet]
    public string Get() => "default";
}

[ApiController]
[Route("probe-admin")]
[ApiExplorerSettings(GroupName = "admin")]
public class ProbeAdminController : ControllerBase
{
    [HttpGet]
    public string Get() => "admin";
}
