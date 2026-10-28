using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Models;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;
using Tnzi.SignalR.Controllers;
using Tnzi.SignalR.Metadata;

namespace Tnzi.SignalR.Tests.Controllers;

/// <summary>
/// 实时连接管理端只对宿主开放。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 连接登记是进程级的，元数据没有租户维度。此前管理端点全量读这张表、按任意用户 ID 断开连接：
/// 多租户开启时，一个被授予 <c>system.signalr.view</c> / <c>.execute</c> 的租户管理员
/// 能列出所有租户在线用户的用户名、来源地址与 UA，并强制断开别的租户的用户。
/// </para>
/// <para>
/// 过滤器经 <see cref="TypeFilterAttribute"/> 挂在控制器类上。这里按 MVC 的方式创建它
/// （<see cref="TypeFilterAttribute.CreateInstance"/>，构造参数从容器解析），再按 MVC 的方式调用它；
/// 断言「控制器类与它的子类都带着这个过滤器」守的是它不会在覆写动作时被绕过。
/// </para>
/// </remarks>
public class SignalRAdminHostOnlyTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class StubTenant(Guid? id) : ICurrentTenant
    {
        public Guid? Id => id;
        public string? Name => null;
        public bool IsAvailable => id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => throw new NotSupportedException();
    }

    private sealed class StubUser(Guid? tenantId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? Id => Guid.NewGuid();
        public string? UserName => "admin";
        public Guid? TenantId => tenantId;
        public string[] Roles => [];
        public bool IsInRole(string roleName) => false;
        public string? FindClaim(string claimType) => null;
        public string[] FindClaims(string claimType) => [];
    }

    /// <summary>经 MVC 同一条路径创建并执行过滤器，返回「动作有没有被执行」与结果。</summary>
    private static async Task<(bool ActionRan, IActionResult? Result)> RunAsync(
        bool multiTenancyEnabled, Guid? currentTenant, Guid? claimTenant)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(new StubTenant(currentTenant));
        services.AddSingleton<ICurrentUser>(new StubUser(claimTenant));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = multiTenancyEnabled }));
        using var provider = services.BuildServiceProvider();

        var filterAttribute = typeof(DefaultSignalRAdminController)
            .GetCustomAttributes<TypeFilterAttribute>(inherit: true)
            .Single(a => a.ImplementationType == typeof(HostContextOnlyFilter));
        var filter = (IAsyncActionFilter)filterAttribute.CreateInstance(provider);

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var actionRan = false;
        await filter.OnActionExecutionAsync(executing, () =>
        {
            actionRan = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
        });

        return (actionRan, executing.Result);
    }

    /// <summary>★★ 租户上下文（租户解析中间件已切到租户 A）：403，动作一行都不执行。</summary>
    [Fact]
    public async Task TenantCaller_IsRejected_BeforeTheActionRuns()
    {
        var (actionRan, result) = await RunAsync(multiTenancyEnabled: true, currentTenant: TenantA, claimTenant: TenantA);

        actionRan.ShouldBeFalse();
        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(403);
        objectResult.Value.ShouldBeOfType<ApiResult>().ErrorCode.ShouldBe(SignalRErrorCodes.HostManaged);
    }

    /// <summary>
    /// ★ 租户解析中间件没开（<see cref="ICurrentTenant"/> 为空）时退回令牌里的租户 claim，与全局过滤器同源。
    /// </summary>
    [Fact]
    public async Task TenantClaimWithoutResolvedTenant_IsStillRejected()
    {
        var (actionRan, result) = await RunAsync(multiTenancyEnabled: true, currentTenant: null, claimTenant: TenantA);

        actionRan.ShouldBeFalse();
        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
    }

    /// <summary>对照组：宿主（不属于任何租户）照常放行。</summary>
    [Fact]
    public async Task HostCaller_IsAllowed()
    {
        var (actionRan, result) = await RunAsync(multiTenancyEnabled: true, currentTenant: null, claimTenant: null);

        actionRan.ShouldBeTrue();
        result.ShouldBeNull();
    }

    /// <summary>对照组：多租户关闭时恒为宿主，单租户部署的行为不变（即使令牌里带着租户 claim）。</summary>
    [Fact]
    public async Task MultiTenancyDisabled_IsAlwaysTheHost()
    {
        var (actionRan, _) = await RunAsync(multiTenancyEnabled: false, currentTenant: null, claimTenant: TenantA);

        actionRan.ShouldBeTrue();
    }

    /// <summary>
    /// 过滤器挂在类上、随继承保留：消费方继承默认控制器并覆写某个动作，不会把宿主限定一起覆写掉。
    /// </summary>
    [Fact]
    public void TheFilter_IsCarriedByTheControllerAndItsSubclasses()
    {
        typeof(DefaultSignalRAdminController)
            .GetCustomAttributes<TypeFilterAttribute>(inherit: true)
            .ShouldContain(a => a.ImplementationType == typeof(HostContextOnlyFilter));
        typeof(DerivedSignalRAdminController)
            .GetCustomAttributes<TypeFilterAttribute>(inherit: true)
            .ShouldContain(a => a.ImplementationType == typeof(HostContextOnlyFilter));
    }

    private sealed class DerivedSignalRAdminController(IConnectionManager connectionManager)
        : DefaultSignalRAdminController(connectionManager)
    {
        public override Task<ApiResult<List<OnlineUserDto>>> GetOnlineUsers() => base.GetOnlineUsers();
    }
}
