using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;
using Tnzi.MultiTenancy;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 租户有效性检查器按请求解析，而不是在中间件构造时从根容器取一次。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <see cref="TenantResolverMiddleware"/> 是约定式中间件，<c>UseMiddleware</c> 用根容器构造它、
/// 整个进程只有一个实例。<see cref="ITenantChecker"/> 的实现通常是 Scoped（它查租户表，背后是 DbContext），
/// 构造注入就是从根容器解析 Scoped 服务：开发环境（ValidateScopes）管线构建当场抛异常、应用起不来；
/// 生产环境不校验，于是一个 checker 连同它的 DbContext 被根作用域捕获、服务所有并发请求，
/// 负载下抛「A second operation was started on this context」。
/// </para>
/// <para>
/// 刻意跑<strong>真实管线</strong>且用 Development 环境：直接 new 中间件的单测永远走不到根容器解析这条路。
/// 变异验证：把 checker 改回构造注入，启动即红（ValidateScopes）。
/// </para>
/// </remarks>
public class TenantResolverPipelineTests
{
    private static readonly Guid TenantId = Guid.Parse("3c1f2a9e-5b7d-4e8c-9a0b-1d2e3f4a5b6c");

    [Fact]
    public async Task ScopedTenantChecker_IsResolvedPerRequest()
    {
        var instances = new List<Guid>();
        TenantCheckerProbeStartupModule.Instances = instances;
        try
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
                ["AspNetCore:TenantResolver:Enabled"] = "true"
            });

            var app = await TnziApp.CreateAsync<TenantCheckerProbeStartupModule>(builder);
            await app.StartAsync();
            try
            {
                var client = app.GetTestClient();
                for (var i = 0; i < 2; i++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/e2e/hello");
                    request.Headers.Add("X-Tenant-Id", TenantId.ToString());
                    var response = await client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TenantCheckerProbeStartupModule.Instances = null;
        }

        // 每个请求问到的是自己作用域里的那一个 checker。
        Assert.Equal(2, instances.Count);
        Assert.NotEqual(instances[0], instances[1]);
    }
}

/// <summary>只依赖 AspNetCore 的最小启动模块：开多租户并注册一个 Scoped 的租户检查器。</summary>
/// <remarks>静态注入点：用例在 finally 里复位。</remarks>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class TenantCheckerProbeStartupModule : TnziCustomModule
{
    internal static List<Guid>? Instances;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // MultiTenancyOptions 平时由 EFCore 模块绑定；这里只装 AspNetCore，直接打开。
        context.Services.Configure<MultiTenancyOptions>(options => options.Enabled = true);
        context.Services.AddScoped<ITenantChecker>(_ => new ScopedProbeTenantChecker(Instances));
        return Task.CompletedTask;
    }
}

/// <summary>每个实例带一个随机 Id，被问到时记一笔，用来区分是不是同一个实例。</summary>
public sealed class ScopedProbeTenantChecker(List<Guid>? instances) : ITenantChecker
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (instances != null)
        {
            lock (instances)
            {
                instances.Add(_instanceId);
            }
        }
        return Task.FromResult(true);
    }
}
