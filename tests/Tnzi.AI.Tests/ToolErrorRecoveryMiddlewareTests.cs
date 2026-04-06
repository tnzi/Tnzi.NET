namespace Tnzi.AI.Tests;

public class ToolErrorRecoveryMiddlewareTests
{
    [Fact]
    public void Order_ReturnsToolErrorRecoveryOrder()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        Assert.Equal(AiMiddlewareOrders.ToolErrorRecovery, middleware.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();
        var expected = new AgentRunResult { Response = "ok" };

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(expected));

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_AiPipelineException_Bubbles()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context,
                (ctx, ct) => throw new InvalidOperationException("pipeline broke")));
    }

    [Fact]
    public async Task InvokeToolAsync_OperationCanceled_Rethrows()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "test_tool" };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            middleware.InvokeAsync(context, () => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task InvokeToolAsync_Exception_ReturnsRecoveredResult()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "test_tool" };

        var result = await middleware.InvokeAsync(context, () => throw new InvalidOperationException("broken"));

        result.ShouldBe("Tool execution failed: InvalidOperationException: broken");
        ToolErrorRecoveryMiddleware.GetRecoveredError(context).ShouldBe("InvalidOperationException: broken");
    }
}
