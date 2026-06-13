using Tnzi.AI.Sandbox.Middleware;

namespace Tnzi.AI.Tests.Sandbox;

public class SandboxMiddlewareTests
{
    [Fact]
    public void Order_ReturnsSandboxOrder()
    {
        var mw = CreateMiddleware(new AgentExecutionContextAccessor());
        Assert.Equal(AiMiddlewareOrders.Sandbox, mw.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoThreadData_DoesNotPublishEnvironment()
    {
        var accessor = new AgentExecutionContextAccessor();
        var mw = CreateMiddleware(accessor);
        var context = TestHelpers.CreateMinimalContext();

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(accessor.Properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
    }

    [Fact]
    public async Task InvokeAsync_WithThreadData_PublishesEnvironmentDuringNext_AndRemovesAfter()
    {
        var accessor = new AgentExecutionContextAccessor();
        var mw = CreateMiddleware(accessor);
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);

        // 在测试帧物化属性字典，让中间件对同一实例的修改在 InvokeAsync 返回后仍可见
        var properties = accessor.Properties;

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

            SandboxToolEnvironment? observed = null;
            var result = await mw.InvokeAsync(context,
                (ctx, ct) =>
                {
                    observed = accessor.Properties.GetValueOrDefault(SandboxPropertyKeys.ToolEnvironment)
                        as SandboxToolEnvironment;
                    return Task.FromResult(new AgentRunResult { Response = "ok" });
                });

            Assert.Equal("ok", result.Response);

            // next() 期间环境可见，且绑定请求的 ThreadId
            Assert.NotNull(observed);
            Assert.NotNull(observed!.Sandbox);
            Assert.Equal(threadId, observed.ThreadId);

            // 运行结束后环境被移除 — 沙箱已释放，绝不允许悬挂引用
            Assert.False(properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));
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
        var accessor = new AgentExecutionContextAccessor();
        var options = new SandboxModuleOptions { Enabled = false };
        var provider = new LocalSandboxProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            new TestHostEnvironment(),
            NullLogger<LocalSandboxProvider>.Instance);
        var mw = new SandboxMiddleware(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            accessor,
            NullLogger<SandboxMiddleware>.Instance);

        var context = TestHelpers.CreateMinimalContext();
        context.Properties[SandboxPropertyKeys.ThreadData] = new ThreadDataState("t", "x", "y", "z", "s");

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(accessor.Properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
    }

    private static SandboxMiddleware CreateMiddleware(AgentExecutionContextAccessor accessor)
    {
        var options = new SandboxModuleOptions();
        var provider = new LocalSandboxProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            new TestHostEnvironment(),
            NullLogger<LocalSandboxProvider>.Instance);
        return new SandboxMiddleware(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            accessor,
            NullLogger<SandboxMiddleware>.Instance);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tnzi.AI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
