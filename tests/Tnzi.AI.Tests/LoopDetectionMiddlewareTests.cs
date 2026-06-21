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

    // ── Cross-request isolation (regression: GetThreadKey must NOT bucket all
    //    identity-less requests under a single shared key) ──────────────────────

    private static AiMiddlewareContext CreateContext(Guid? threadId, Guid? userId) =>
        new()
        {
            Request = new AgentRunRequest { UserMessage = "Hi", ThreadId = threadId, UserId = userId },
            Agent = AgentResolution.Success(agent: null!, provider: "TestProvider", model: "test-model", agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };

    private static async Task RepeatIdenticalToolCallAsync(LoopDetectionMiddleware middleware, AiMiddlewareContext context, int times)
    {
        for (var i = 0; i < times; i++)
        {
            context.Messages.Add(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent($"call-{i}", "search", new Dictionary<string, object?> { ["query"] = "test" })
            ]));
            context.Messages.Add(new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent($"call-{i}", "result")
            ]));
            await middleware.InvokeAsync(context, (ctx, ct) =>
                Task.FromResult(new AgentRunResult { Response = "ok" }));
        }
    }

    [Fact]
    public async Task InvokeAsync_AnonymousRequest_SkipsDetection()
    {
        // No ThreadId and no UserId → GetThreadKey returns null → detection skipped,
        // so repeated identical tool calls never inject loop-coaching messages.
        var middleware = CreateMiddleware();
        var context = CreateContext(threadId: null, userId: null);

        await RepeatIdenticalToolCallAsync(middleware, context, _options.HardLimit + 1);

        Assert.DoesNotContain(context.Messages, m => m.Role == ChatRole.User);
    }

    [Fact]
    public async Task InvokeAsync_DifferentUsers_DoNotShareLoopBucket()
    {
        // Shared singleton middleware. User A reaches the warn threshold; user B makes a
        // single identical call. If both bucketed under one key, B's call would be the
        // HardLimit-th and trip a hard stop on B. Per-user isolation must prevent that.
        var middleware = CreateMiddleware();
        var userA = CreateContext(threadId: null, userId: Guid.NewGuid());
        var userB = CreateContext(threadId: null, userId: Guid.NewGuid());

        await RepeatIdenticalToolCallAsync(middleware, userA, _options.WarnThreshold); // A: count == WarnThreshold
        await RepeatIdenticalToolCallAsync(middleware, userB, 1);                      // B: count == 1

        // User B's bucket only has 1 hit → no coaching message of any kind.
        Assert.DoesNotContain(userB.Messages, m => m.Role == ChatRole.User);
        // Sanity: per-user detection still works — A did get warned in its own bucket.
        Assert.Contains(userA.Messages, m => m.Role == ChatRole.User && m.Text?.Contains("LOOP DETECTED") == true);
    }
}
