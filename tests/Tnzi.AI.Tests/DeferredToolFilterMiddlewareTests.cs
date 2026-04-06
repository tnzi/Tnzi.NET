namespace Tnzi.AI.Tests;

public class DeferredToolFilterMiddlewareTests
{
    [Fact]
    public void Order_ReturnsDeferredToolFilterOrder()
    {
        var middleware = new DeferredToolFilterMiddleware(NullLogger<DeferredToolFilterMiddleware>.Instance);
        Assert.Equal(AiMiddlewareOrders.DeferredToolFilter, middleware.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoRegistry_PassesThrough()
    {
        ToolDeferredRegistry.Current = null;
        var middleware = new DeferredToolFilterMiddleware(NullLogger<DeferredToolFilterMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();
        var called = false;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            called = true;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_WithDeferredTools_RemovesFromAdditionalTools()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("deferred_tool", "A deferred tool", null);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var middleware = new DeferredToolFilterMiddleware(NullLogger<DeferredToolFilterMiddleware>.Instance);
            var context = TestHelpers.CreateMinimalContext();

            // Add a mock AITool with the deferred name
            var mockTool = AIFunctionFactory.Create(() => "result", "deferred_tool", "test");
            context.AdditionalTools.Add(mockTool);
            var nonDeferredTool = AIFunctionFactory.Create(() => "result", "keep_tool", "keep");
            context.AdditionalTools.Add(nonDeferredTool);

            await middleware.InvokeAsync(context, (ctx, ct) =>
            {
                Assert.Single(ctx.AdditionalTools);
                Assert.Equal("keep_tool", ctx.AdditionalTools[0].Name);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public async Task InvokeAsync_LoadedDeferredTool_KeepsItInAdditionalTools()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("deferred_tool", "A deferred tool", null, null, null, shouldDefer: true);
        registry.LoadTools(["deferred_tool"]);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var middleware = new DeferredToolFilterMiddleware(NullLogger<DeferredToolFilterMiddleware>.Instance);
            var context = TestHelpers.CreateMinimalContext();

            context.AdditionalTools.Add(AIFunctionFactory.Create(() => "result", "deferred_tool", "test"));

            await middleware.InvokeAsync(context, (ctx, ct) =>
            {
                Assert.Single(ctx.AdditionalTools);
                Assert.Equal("deferred_tool", ctx.AdditionalTools[0].Name);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public async Task InvokeAsync_AlwaysLoadTool_KeepsItInAdditionalTools()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("always_tool", "Always-loaded tool", null, null, null, shouldDefer: true, alwaysLoad: true);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var middleware = new DeferredToolFilterMiddleware(NullLogger<DeferredToolFilterMiddleware>.Instance);
            var context = TestHelpers.CreateMinimalContext();

            context.AdditionalTools.Add(AIFunctionFactory.Create(() => "result", "always_tool", "test"));

            await middleware.InvokeAsync(context, (ctx, ct) =>
            {
                Assert.Single(ctx.AdditionalTools);
                Assert.Equal("always_tool", ctx.AdditionalTools[0].Name);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }
}
