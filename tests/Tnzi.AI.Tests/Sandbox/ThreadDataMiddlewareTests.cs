using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 线程数据中间件只算路径、不碰磁盘：目录与技能副本由 <c>ThreadDataProvisioner</c> 在沙箱首次被用到时布置
/// （见 <see cref="ThreadDataProvisionerTests"/>）。
/// </summary>
public class ThreadDataMiddlewareTests : IDisposable
{
    private readonly string _root = NewTempRoot("tnzi-td");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Order_ReturnsThreadDataOrder()
    {
        var mw = CreateThreadDataMiddleware(SandboxOptions(_root));
        Assert.Equal(AiMiddlewareOrders.ThreadData, mw.Order);
    }

    [Fact]
    public async Task InvokeAsync_SetsThreadDataInProperties()
    {
        var mw = CreateThreadDataMiddleware(SandboxOptions(_root));
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);

        await mw.InvokeAsync(context,
            (ctx, ct) =>
            {
                Assert.True(ctx.Properties.ContainsKey("ThreadData"));
                var data = ctx.Properties["ThreadData"] as ThreadDataState;
                Assert.NotNull(data);
                Assert.Contains(threadId.ToString("N"), data.WorkspacePath);
                Assert.Equal(Path.Combine(data.ThreadDirectory, "uploads"), data.UploadsPath);
                Assert.Equal(Path.Combine(data.ThreadDirectory, "skills"), data.SkillsPath);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });
    }

    /// <summary>
    /// 发布布局不建目录：没有工具用到沙箱的运行在磁盘上不留任何痕迹（此前 <c>LazyDirectoryCreation=false</c>
    /// 在这里就建四个子目录，技能复制更是无条件执行）。
    /// </summary>
    [Fact]
    public async Task InvokeAsync_DoesNotTouchTheDisk_EvenWhenDirectoryCreationIsEager()
    {
        var mw = CreateThreadDataMiddleware(SandboxOptions(_root, lazyDirectoryCreation: false));
        var threadId = Guid.NewGuid();
        var context = TestHelpers.CreateMinimalContext(threadId: threadId);

        await mw.InvokeAsync(context, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        var state = (ThreadDataState)context.Properties[SandboxPropertyKeys.ThreadData];
        Assert.False(Directory.Exists(state.ThreadDirectory));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task InvokeAsync_UnresolvedThreadId_SkipsMiddleware()
    {
        // 只有管线没装 ThreadResolutionMiddleware（40）时才会走到这里：正常管线里线程在本中间件之前就已解析。
        // 这里跳过是「没有线程就没有目录」的兜底，不是新会话首轮的期望行为。
        var mw = CreateThreadDataMiddleware(SandboxOptions(_root));
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
}
