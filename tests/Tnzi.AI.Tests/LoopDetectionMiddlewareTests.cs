namespace Tnzi.AI.Tests;

public class LoopDetectionMiddlewareTests
{
    private readonly LoopDetectionOptions _options = new()
    {
        WarnThreshold = 2,
        HardLimit = 3,
        WindowSize = 10,
        MaxTrackedThreads = 50
    };

    private LoopDetectionMiddleware CreateMiddleware() =>
        new(TestHelpers.CreateOptionsMonitor(_options), NullLogger<LoopDetectionMiddleware>.Instance);

    [Fact]
    public void Order_ReturnsLoopDetectionOrder()
    {
        var middleware = CreateMiddleware();
        Assert.Equal(AiMiddlewareOrders.LoopDetection, middleware.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoToolCalls_PassesThrough()
    {
        var middleware = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var called = false;

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            called = true;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        Assert.True(called);
        Assert.Equal("ok", result.Response);
    }

    [Fact]
    public async Task InvokeAsync_RepeatedToolCalls_AtWarnThreshold_InjectsWarning()
    {
        var middleware = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();

        // Simulate 2 identical tool call patterns (warn_threshold = 2)
        for (var i = 0; i < 2; i++)
        {
            context.Messages.Add(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent($"call-{i}", "search", new Dictionary<string, object?> { ["query"] = "test" })
            ]));
            // Add result for the tool call
            context.Messages.Add(new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent($"call-{i}", "result")
            ]));

            await middleware.InvokeAsync(context, (ctx, ct) =>
                Task.FromResult(new AgentRunResult { Response = $"call-{i}" }));
        }

        // After 2nd call: should have warning injected
        var hasWarning = context.Messages.Any(m =>
            m.Role == ChatRole.User && m.Text?.Contains("LOOP DETECTED") == true);
        Assert.True(hasWarning);
    }

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThrough()
    {
        var opts = new LoopDetectionOptions { Enabled = false };
        var middleware = new LoopDetectionMiddleware(
            TestHelpers.CreateOptionsMonitor(opts),
            NullLogger<LoopDetectionMiddleware>.Instance);
        var context = TestHelpers.CreateMinimalContext();

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        Assert.Equal("ok", result.Response);
    }
}
