using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 沙箱中间件：发布的是一个<b>按需创建</b>的环境。运行里没有工具用到沙箱，provider 一次都不被调用、
/// 磁盘上什么都不发生；第一个用到它的工具调用才布置目录并创建沙箱；运行结束释放。
/// </summary>
public class SandboxMiddlewareTests : IDisposable
{
    private readonly string _root = NewTempRoot("tnzi-mw-test");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Order_ReturnsSandboxOrder()
    {
        var mw = CreateSandboxMiddleware(SandboxOptions(_root), new CountingSandboxProvider(), new AgentExecutionContextAccessor());
        Assert.Equal(AiMiddlewareOrders.Sandbox, mw.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoThreadData_DoesNotPublishEnvironment()
    {
        var accessor = new AgentExecutionContextAccessor();
        var provider = new CountingSandboxProvider();
        var mw = CreateSandboxMiddleware(SandboxOptions(_root), provider, accessor);
        var context = TestHelpers.CreateMinimalContext();

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(accessor.Properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
        Assert.Equal(0, provider.CreateCalls);
    }

    [Fact]
    public async Task InvokeAsync_WithThreadData_PublishesEnvironmentDuringNext_AndRemovesAfter()
    {
        var accessor = new AgentExecutionContextAccessor();
        var provider = new CountingSandboxProvider();
        var mw = CreateSandboxMiddleware(SandboxOptions(_root), provider, accessor);
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);

        // 在测试帧物化属性字典，让中间件对同一实例的修改在 InvokeAsync 返回后仍可见
        var properties = accessor.Properties;
        context.Properties[SandboxPropertyKeys.ThreadData] = ThreadDataState.FromThreadDirectory(Path.Combine(_root, threadId.ToString("N")));

        SandboxToolEnvironment? observed = null;
        ISandbox? sandbox = null;
        var result = await mw.InvokeAsync(context,
            async (ctx, ct) =>
            {
                observed = accessor.Properties.GetValueOrDefault(SandboxPropertyKeys.ToolEnvironment) as SandboxToolEnvironment;
                sandbox = await observed!.GetSandboxAsync(ct);
                return new AgentRunResult { Response = "ok" };
            });

        Assert.Equal("ok", result.Response);

        // next() 期间环境可见，且绑定请求的 ThreadId；取沙箱时才创建
        Assert.NotNull(observed);
        Assert.Equal(threadId, observed!.ThreadId);
        Assert.NotNull(sandbox);
        Assert.Equal(1, provider.CreateCalls);

        // 运行结束后环境被移除、沙箱已释放 - 绝不允许悬挂引用
        Assert.False(properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));
        Assert.True(provider.Created.Single().Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await observed.GetSandboxAsync());
    }

    /// <summary>
    /// 没有沙箱工具的 Agent（或这一轮没用到沙箱）：provider 不被调用、线程目录不建、技能不复制。
    /// 此前每一次带线程的运行在入口就做完这三件事；某消费方 6 个没有任何工具的 bot 一天 863 次运行
    /// 留下 863 个线程目录、92,000 个文件，Production 下的 Local 守卫还让每一次都失败。
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NothingUsesTheSandbox_CreatesNothing()
    {
        var accessor = new AgentExecutionContextAccessor();
        var provider = new CountingSandboxProvider();
        var store = new CountingSkillStore();
        var mw = CreateSandboxMiddleware(SandboxOptions(_root), provider, accessor, store);
        var threadId = Guid.NewGuid();
        var threadDir = Path.Combine(_root, threadId.ToString("N"));
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);
        context.Properties[SandboxPropertyKeys.ThreadData] = ThreadDataState.FromThreadDirectory(threadDir);

        SandboxToolEnvironment? observed = null;
        await mw.InvokeAsync(context, (ctx, ct) =>
        {
            observed = accessor.Properties.GetValueOrDefault(SandboxPropertyKeys.ToolEnvironment) as SandboxToolEnvironment;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        Assert.NotNull(observed);
        Assert.False(observed!.IsSandboxCreated);
        Assert.Equal(0, provider.CreateCalls);
        Assert.Equal(0, store.GetAllCalls);
        Assert.False(Directory.Exists(threadDir));
        Assert.False(Directory.Exists(_root));
    }

    /// <summary>
    /// Production 下未开 AllowInProduction 的 Local provider：守卫只在真的要用沙箱的那次调用上触发，
    /// 没用到沙箱的运行照常完成。这正是消费方在生产上整天不可用的那条路径。
    /// </summary>
    [Fact]
    public async Task InvokeAsync_LocalProviderInProduction_OnlyFailsWhenTheSandboxIsActuallyUsed()
    {
        var accessor = new AgentExecutionContextAccessor();
        var options = SandboxOptions(_root);
        var provider = CreateLocalProvider(options, environmentName: "Production");
        var mw = CreateSandboxMiddleware(options, provider, accessor);
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);
        context.Properties[SandboxPropertyKeys.ThreadData] = ThreadDataState.FromThreadDirectory(Path.Combine(_root, threadId.ToString("N")));

        var untouched = await mw.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));
        Assert.Equal("ok", untouched.Response);

        var used = await Assert.ThrowsAsync<InvalidOperationException>(() => mw.InvokeAsync(context, async (ctx, ct) =>
        {
            var env = (SandboxToolEnvironment)accessor.Properties[SandboxPropertyKeys.ToolEnvironment];
            await env.GetSandboxAsync(ct);
            return new AgentRunResult { Response = "unreachable" };
        }));
        Assert.Contains("disabled in Production", used.Message);
    }

    [Fact]
    public async Task InvokeAsync_FirstUse_ProvisionsThreadDirectoryAndSkillsBeforeCreatingTheSandbox()
    {
        var accessor = new AgentExecutionContextAccessor();
        var provider = new CountingSandboxProvider();
        var store = new CountingSkillStore();
        var mw = CreateSandboxMiddleware(SandboxOptions(_root), provider, accessor, store);
        var threadId = Guid.NewGuid();
        var threadDir = Path.Combine(_root, threadId.ToString("N"));
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);
        context.Properties[SandboxPropertyKeys.ThreadData] = ThreadDataState.FromThreadDirectory(threadDir);

        await mw.InvokeAsync(context, async (ctx, ct) =>
        {
            var env = (SandboxToolEnvironment)accessor.Properties[SandboxPropertyKeys.ToolEnvironment];
            var sandbox = await env.GetSandboxAsync(ct);
            Assert.Equal(threadDir, ((CountingSandbox)sandbox).WorkspacePath);
            return new AgentRunResult { Response = "ok" };
        });

        Assert.Equal(1, provider.CreateCalls);
        Assert.Equal(1, store.GetAllCalls);
        Assert.True(File.Exists(Path.Combine(threadDir, "skills", store.Slug, "scripts", "run.py")));
        Assert.True(File.Exists(Path.Combine(threadDir, ".skills_wired")));
    }

    [Fact]
    public async Task InvokeStreamingAsync_ReleasesTheEnvironment_WhenTheStreamCompletes()
    {
        var accessor = new AgentExecutionContextAccessor();
        var provider = new CountingSandboxProvider();
        var mw = CreateSandboxMiddleware(SandboxOptions(_root), provider, accessor);
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);
        var properties = accessor.Properties;
        context.Properties[SandboxPropertyKeys.ThreadData] = ThreadDataState.FromThreadDirectory(Path.Combine(_root, threadId.ToString("N")));

        var chunks = 0;
        await foreach (var _ in mw.InvokeStreamingAsync(context, Stream))
        {
            chunks++;
        }

        Assert.Equal(1, chunks);
        Assert.Equal(1, provider.CreateCalls);
        Assert.True(provider.Created.Single().Disposed);
        Assert.False(properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));

        async IAsyncEnumerable<AgentStreamChunk> Stream(AiMiddlewareContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            var env = (SandboxToolEnvironment)accessor.Properties[SandboxPropertyKeys.ToolEnvironment];
            await env.GetSandboxAsync(ct);
            yield return new AgentStreamChunk { Text = "chunk" };
        }
    }

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThrough()
    {
        var accessor = new AgentExecutionContextAccessor();
        var provider = new CountingSandboxProvider();
        var options = SandboxOptions(_root);
        options.Enabled = false;
        var mw = CreateSandboxMiddleware(options, provider, accessor);

        var context = TestHelpers.CreateMinimalContext();
        context.Properties[SandboxPropertyKeys.ThreadData] = new ThreadDataState("t", "x", "y", "z", "s");

        var result = await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.False(accessor.Properties.ContainsKey(SandboxPropertyKeys.ToolEnvironment));
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        Assert.Equal("ok", result.Response);
        Assert.Equal(0, provider.CreateCalls);
    }
}
