using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Mvc.Filters;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// <c>[UnitOfWork(IsDisabled = true)]</c> 必须真的关掉事务。
///
/// 特性本身是一个 <c>ServiceFilterAttribute</c>：只要标了它，MVC 就会解析并运行
/// <c>UnitOfWorkActionFilter</c>。此前那个过滤器一处都不读 <c>IsDisabled</c>，进来就
/// <c>EnableTransaction()</c> —— 读 <c>IsDisabled</c> 的只有全局过滤器 <c>UnitOfWorkFilter</c>，
/// 它只让自己跳过，管不到这个 ServiceFilter。于是一个刻意写着「已禁用事务」的 action
/// 在两种模式下都照样跑在环境事务里：写完再调外部网关的端点会在整个外部调用期间
/// 持有连接与事务，而排查的人手里拿着一行说它关了的代码。
/// </summary>
/// <remarks>
/// 过滤器经特性的 <c>CreateInstance</c> 取得 —— 那正是 MVC 的取法，也是唯一的公开入口。
/// </remarks>
public class UnitOfWorkActionFilterTests
{
    private static (IAsyncActionFilter Filter, ActionExecutingContext Context, Mock<IUnitOfWorkManager> Manager)
        Build(params UnitOfWorkAttribute[] metadata)
    {
        var manager = new Mock<IUnitOfWorkManager>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager.Object);
        services.AddScoped(metadata[0].ServiceType);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            metadata.Cast<IFilterMetadata>().ToList(),
            new Dictionary<string, object?>(),
            controller: null!);
        context.ActionDescriptor.EndpointMetadata = metadata.Cast<object>().ToList();

        // 取过滤器的方式与 MVC 相同：由特性从容器里造出来。
        var filter = (IAsyncActionFilter)metadata[0].CreateInstance(provider);

        return (filter, context, manager);
    }

    private static ActionExecutionDelegate Succeed(ActionExecutingContext context) =>
        () => Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller)
        {
            Result = new OkResult()
        });

    [Fact]
    public async Task IsDisabled_DoesNotEnableTransaction()
    {
        var (filter, context, manager) = Build(new UnitOfWorkAttribute { IsDisabled = true });

        var reached = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Succeed(context)();
        });

        Assert.True(reached, "the action itself must still run");
        manager.Verify(m => m.EnableTransaction(), Times.Never);
        manager.Verify(m => m.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        manager.Verify(m => m.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MethodLevelDisabled_BeatsClassLevelEnabled()
    {
        // 类上 [UnitOfWork]、方法上 [UnitOfWork(IsDisabled = true)]：两份特性各造一个过滤器实例，
        // 每一个都得看见「禁用」并让路 —— 与全局过滤器的规则一致。
        var (filter, context, manager) = Build(
            new UnitOfWorkAttribute(),
            new UnitOfWorkAttribute { IsDisabled = true });

        await filter.OnActionExecutionAsync(context, Succeed(context));

        manager.Verify(m => m.EnableTransaction(), Times.Never);
    }

    [Fact]
    public async Task Enabled_EnablesAndCommits()
    {
        // 防锈：证明上面两条不是因为「过滤器压根没接上」才绿。
        var (filter, context, manager) = Build(new UnitOfWorkAttribute());

        await filter.OnActionExecutionAsync(context, Succeed(context));

        manager.Verify(m => m.EnableTransaction(), Times.Once);
        manager.Verify(m => m.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
