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
    public async Task InvokeAsync_OperationCanceled_Rethrows()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            middleware.InvokeAsync(context,
                (ctx, ct) => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task InvokeAsync_Exception_ReturnsFailedResult()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => throw new InvalidOperationException("tool broke"));

        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Contains("tool broke", result.Response);
    }

    [Fact]
    public async Task InvokeAsync_Exception_InjectsErrorMessage()
    {
        var middleware = new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();

        await middleware.InvokeAsync(context,
            (ctx, ct) => throw new InvalidOperationException("broken"));

        Assert.Contains(context.Messages, m => m.Text?.Contains("TOOL ERROR") == true);
    }
}
