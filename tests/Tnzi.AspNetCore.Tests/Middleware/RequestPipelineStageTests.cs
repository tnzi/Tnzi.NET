using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Extensions;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// <see cref="RequestPipelineStage"/> 插入点的契约：登记的中间件按登记顺序、只在自己的阶段被挂上。
/// </summary>
/// <remarks>
/// 「插入点真的在限流与授权之前」由 <see cref="AccessLogPipelinePlacementTests"/> 端到端守着；这里只守登记与展开本身。
/// </remarks>
public class RequestPipelineStageTests
{
    [Fact]
    public async Task Registrations_AreAppliedInRegistrationOrder()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddRequestPipelineMiddleware(RequestPipelineStage.AfterAuthentication, app => app.Use(next => ctx => { order.Add("first"); return next(ctx); }));
        services.AddRequestPipelineMiddleware(RequestPipelineStage.AfterAuthentication, app => app.Use(next => ctx => { order.Add("second"); return next(ctx); }));
        var provider = services.BuildServiceProvider();

        var builder = new ApplicationBuilder(provider);
        builder.UseRequestPipelineStage(RequestPipelineStage.AfterAuthentication);
        await builder.Build()(new DefaultHttpContext { RequestServices = provider });

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task GenericOverload_UsesTheMiddlewareType()
    {
        var services = new ServiceCollection();
        services.AddRequestPipelineMiddleware<MarkerMiddleware>(RequestPipelineStage.AfterAuthentication);
        var provider = services.BuildServiceProvider();

        var builder = new ApplicationBuilder(provider);
        builder.UseRequestPipelineStage(RequestPipelineStage.AfterAuthentication);
        var context = new DefaultHttpContext { RequestServices = provider };
        await builder.Build()(context);

        Assert.True(context.Items.ContainsKey(MarkerMiddleware.Key));
    }

    [Fact]
    public void NothingRegistered_IsANoOp()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(provider);

        var returned = builder.UseRequestPipelineStage(RequestPipelineStage.AfterAuthentication);

        Assert.Same(builder, returned);
    }

    [Fact]
    public void Registration_RequiresAConfigureAction()
    {
        Assert.Throws<ArgumentNullException>(() => new RequestPipelineRegistration(RequestPipelineStage.AfterAuthentication, null!));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddRequestPipelineMiddleware(RequestPipelineStage.AfterAuthentication, null!));
    }

    private sealed class MarkerMiddleware(RequestDelegate next)
    {
        public const string Key = "marker-middleware";

        public Task InvokeAsync(HttpContext context)
        {
            context.Items[Key] = true;
            return next(context);
        }
    }
}
