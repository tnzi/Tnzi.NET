using Tnzi.AI.Sandbox.Middleware;

namespace Tnzi.AI.Tests.Sandbox;

public class SandboxMiddlewareTests
{
    [Fact]
    public void Order_ReturnsSandboxOrder()
    {
        var mw = CreateMiddleware();
        Assert.Equal(AiMiddlewareOrders.Sandbox, mw.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoThreadData_PassesThrough()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(ctx.Properties.ContainsKey(SandboxPropertyKeys.Sandbox));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
    }

    [Fact]
    public async Task InvokeAsync_WithThreadData_CreatesSandbox()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();

        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-mw-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            context.Properties[SandboxPropertyKeys.ThreadData] = new ThreadDataState(
                ThreadDirectory: tempDir,
                WorkspacePath: Path.Combine(tempDir, "workspace"),
                UploadsPath: Path.Combine(tempDir, "uploads"),
                OutputsPath: Path.Combine(tempDir, "outputs"),
                SkillsPath: Path.Combine(tempDir, "skills"));

            var result = await mw.InvokeAsync(context,
                (ctx, ct) =>
                {
                    Assert.True(ctx.Properties.ContainsKey(SandboxPropertyKeys.Sandbox));
                    Assert.True(ctx.Properties.ContainsKey(SandboxPropertyKeys.SandboxId));
                    return Task.FromResult(new AgentRunResult { Response = "ok" });
                });

            Assert.Equal("ok", result.Response);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThrough()
    {
        var options = new SandboxModuleOptions { Enabled = false };
        var provider = new LocalSandboxProvider(Microsoft.Extensions.Options.Options.Create(options));
        var mw = new SandboxMiddleware(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<SandboxMiddleware>.Instance);

        var context = TestHelpers.CreateMinimalContext();
        context.Properties[SandboxPropertyKeys.ThreadData] = new ThreadDataState("t", "x", "y", "z", "s");

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(ctx.Properties.ContainsKey(SandboxPropertyKeys.Sandbox));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
    }

    private static SandboxMiddleware CreateMiddleware()
    {
        var options = new SandboxModuleOptions();
        var provider = new LocalSandboxProvider(Microsoft.Extensions.Options.Options.Create(options));
        return new SandboxMiddleware(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<SandboxMiddleware>.Instance);
    }
}
