using Tnzi.AI.Sandbox.Middleware;

namespace Tnzi.AI.Tests.Sandbox;

public class ThreadDataMiddlewareTests
{
    [Fact]
    public void Order_ReturnsThreadDataOrder()
    {
        var mw = CreateMiddleware();
        Assert.Equal(AiMiddlewareOrders.ThreadData, mw.Order);
    }

    [Fact]
    public async Task InvokeAsync_SetsThreadDataInProperties()
    {
        var mw = CreateMiddleware();
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);

        await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.True(ctx.Properties.ContainsKey("ThreadData"));
                var data = ctx.Properties["ThreadData"] as ThreadDataState;
                Assert.NotNull(data);
                Assert.Contains(threadId.ToString("N"), data.WorkspacePath);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
    }

    [Fact]
    public async Task InvokeAsync_NullThreadId_SkipsMiddleware()
    {
        var mw = CreateMiddleware();
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { ThreadId = null },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test", agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(ctx.Properties.ContainsKey("ThreadData"));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
    }

    private static ThreadDataMiddleware CreateMiddleware()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-td-{Guid.NewGuid():N}");
        var options = new SandboxModuleOptions { DataRoot = tempDir, LazyDirectoryCreation = true };
        return new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(options),
            new VirtualPathTranslator(tempDir),
            NullLogger<ThreadDataMiddleware>.Instance);
    }
}
