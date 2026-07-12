namespace Tnzi.AI.Tests;

public class SubAgentLimitMiddlewareTests
{
    [Fact]
    public void Order_ReturnsSubAgentLimitOrder()
    {
        var middleware = CreateMiddleware(maxConcurrent: 3);
        Assert.Equal(AiMiddlewareOrders.SubAgentLimit, middleware.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoTaskCalls_PassesThrough()
    {
        var middleware = CreateMiddleware(maxConcurrent: 2);
        var context = TestHelpers.CreateMinimalContext();
        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));
        Assert.Equal("ok", result.Response);
    }

    [Fact]
    public async Task InvokeAsync_ExcessTaskCalls_TruncatesExcess()
    {
        var middleware = CreateMiddleware(maxConcurrent: 2);
        var context = TestHelpers.CreateMinimalContext();

        // Add assistant message with 3 task tool calls + 1 other tool
        var contents = new AIContent[]
        {
            new FunctionCallContent("c1", "task", new Dictionary<string, object?> { ["prompt"] = "task 1" }),
            new FunctionCallContent("c2", "task", new Dictionary<string, object?> { ["prompt"] = "task 2" }),
            new FunctionCallContent("c3", "task", new Dictionary<string, object?> { ["prompt"] = "task 3" }),
            new FunctionCallContent("c4", "other_tool", new Dictionary<string, object?> { ["q"] = "test" }),
        };
        context.Messages.Add(new ChatMessage(ChatRole.Assistant, contents.ToList()));

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                var lastMsg = ctx.Messages.Last(m => m.Role == ChatRole.Assistant);
                var taskCalls = lastMsg.Contents.OfType<FunctionCallContent>().Where(f => f.Name == "task").ToList();
                Assert.Equal(2, taskCalls.Count);
                // other_tool should be preserved
                var otherCalls = lastMsg.Contents.OfType<FunctionCallContent>().Where(f => f.Name == "other_tool").ToList();
                Assert.Single(otherCalls);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
    }

    [Fact]
    public async Task InvokeAsync_WithinLimit_NoTruncation()
    {
        var middleware = CreateMiddleware(maxConcurrent: 3);
        var context = TestHelpers.CreateMinimalContext();

        var contents = new AIContent[]
        {
            new FunctionCallContent("c1", "task", new Dictionary<string, object?> { ["prompt"] = "task 1" }),
            new FunctionCallContent("c2", "task", new Dictionary<string, object?> { ["prompt"] = "task 2" }),
        };
        context.Messages.Add(new ChatMessage(ChatRole.Assistant, contents.ToList()));

        await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                var lastMsg = ctx.Messages.Last(m => m.Role == ChatRole.Assistant);
                var taskCalls = lastMsg.Contents.OfType<FunctionCallContent>().Where(f => f.Name == "task").ToList();
                Assert.Equal(2, taskCalls.Count);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
    }

    private static SubAgentLimitMiddleware CreateMiddleware(int maxConcurrent)
    {
        var options = new SubAgentOptions { MaxConcurrentSubAgents = maxConcurrent };
        return new SubAgentLimitMiddleware(new StaticOptionsMonitor<SubAgentOptions>(options), NullLogger<SubAgentLimitMiddleware>.Instance);
    }
}
