using Tnzi.Exceptions;
using Tnzi.AI.Sandbox.Tools;
using Tnzi.AI.Tests.Sandbox;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// 线程解析必须在环境中间件（ThreadData 50 / Sandbox 55）之前完成。
/// </summary>
/// <remarks>
/// ★ 此前线程只在 <see cref="HistoryMiddleware"/>（300）里创建并回写 <c>Request.ThreadId</c>，
/// 而 ThreadData / Sandbox 在它之前跑、遇到 <c>ThreadId == null</c> 直接跳过：每个新会话的首轮没有沙箱，
/// 五个沙箱工具一律答 "Sandbox is not available"，第二轮客户端回传 threadId 后一切正常 —— 看起来像抖动，其实是接线。
/// 出厂客户端（ui-ai）首条消息就是 <c>threadId: null</c>。
/// </remarks>
public class ThreadResolutionMiddlewareTests
{
    private static readonly Guid AgentId = Guid.NewGuid();

    private static AiMiddlewareContext CreateContext(Guid? threadId, bool ephemeral = false)
        => new()
        {
            Request = new AgentRunRequest { UserMessage = "hi", ThreadId = threadId, AgentId = AgentId, Ephemeral = ephemeral },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test", agentId: AgentId),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };

    private static Mock<IAgentThreadInternalService> ThreadServiceResolving(Guid resolved, bool isNew)
    {
        var mock = new Mock<IAgentThreadInternalService>();
        mock.Setup(s => s.GetOrCreateThreadAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new ConversationContext(), resolved, isNew));
        return mock;
    }

    [Fact]
    public void Order_IsBeforeThreadData()
    {
        var mw = new ThreadResolutionMiddleware(ThreadServiceResolving(Guid.NewGuid(), true).Object, NullLogger<ThreadResolutionMiddleware>.Instance);

        mw.Order.ShouldBe(AiMiddlewareOrders.ThreadResolution);
        mw.Order.ShouldBeLessThan(AiMiddlewareOrders.ThreadData);
    }

    [Fact]
    public async Task InvokeAsync_NullThreadId_CreatesThreadAndWritesItBackBeforeNext()
    {
        var resolved = Guid.NewGuid();
        var mw = new ThreadResolutionMiddleware(ThreadServiceResolving(resolved, isNew: true).Object, NullLogger<ThreadResolutionMiddleware>.Instance);
        var context = CreateContext(threadId: null);

        Guid? seenByNext = null;
        await mw.InvokeAsync(context, (ctx, _) =>
        {
            seenByNext = ctx.Request.ThreadId;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        seenByNext.ShouldBe(resolved);
        context.IsNewThread.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeStreamingAsync_NullThreadId_CreatesThreadBeforeNext()
    {
        var resolved = Guid.NewGuid();
        var mw = new ThreadResolutionMiddleware(ThreadServiceResolving(resolved, isNew: true).Object, NullLogger<ThreadResolutionMiddleware>.Instance);
        var context = CreateContext(threadId: null);

        Guid? seenByNext = null;
        await foreach (var _ in mw.InvokeStreamingAsync(context, (ctx, _) =>
                       {
                           seenByNext = ctx.Request.ThreadId;
                           return Empty();
                       }))
        {
        }

        seenByNext.ShouldBe(resolved);
    }

    [Fact]
    public async Task InvokeAsync_ClientSuppliedForeignThreadId_StillRejected()
    {
        // 归属校验的唯一所在地是 GetOrCreateThreadAsync：它的拒绝必须原样传播，前移不能变成放行
        var threadService = new Mock<IAgentThreadInternalService>();
        threadService
            .Setup(s => s.GetOrCreateThreadAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Thread not found"));
        var mw = new ThreadResolutionMiddleware(threadService.Object, NullLogger<ThreadResolutionMiddleware>.Instance);
        var nextCalled = false;

        await Should.ThrowAsync<ResourceNotFoundException>(() => mw.InvokeAsync(CreateContext(Guid.NewGuid()), (_, _) =>
        {
            nextCalled = true;
            return Task.FromResult(new AgentRunResult { Response = "unreachable" });
        }));

        nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task HistoryMiddleware_EnsureThread_IdempotentWhenAlreadyResolved()
    {
        // 两个中间件各调一次 GetOrCreateThreadAsync 会让新线程被建两次
        var resolved = Guid.NewGuid();
        var threadService = ThreadServiceResolving(resolved, isNew: true);
        var resolution = new ThreadResolutionMiddleware(threadService.Object, NullLogger<ThreadResolutionMiddleware>.Instance);
        var history = new HistoryMiddleware(threadService.Object,
            new StaticOptionsMonitor<AIOptions>(new AIOptions()), NullLogger<HistoryMiddleware>.Instance);
        var context = CreateContext(threadId: null);

        var pipeline = new AiMiddlewarePipeline().Use(resolution).Use(history);
        var run = pipeline.Build((_, _) => Task.FromResult(new AgentRunResult { Response = "ok" }));
        await run(context, CancellationToken.None);

        threadService.Verify(s => s.GetOrCreateThreadAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        context.Request.ThreadId.ShouldBe(resolved);
        context.IsNewThread.ShouldBeTrue("History must keep the IsNewThread flag the resolver set");
    }

    [Fact]
    public async Task HistoryMiddleware_WithoutResolver_StillResolvesThread()
    {
        // 自定义管线没装 ThreadResolution 时，History 仍自己解析（向后兼容）
        var resolved = Guid.NewGuid();
        var threadService = ThreadServiceResolving(resolved, isNew: true);
        var history = new HistoryMiddleware(threadService.Object,
            new StaticOptionsMonitor<AIOptions>(new AIOptions()), NullLogger<HistoryMiddleware>.Instance);
        var context = CreateContext(threadId: null);

        await history.InvokeAsync(context, (_, _) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        context.Request.ThreadId.ShouldBe(resolved);
        threadService.Verify(s => s.GetOrCreateThreadAsync(null, AgentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 新会话首轮（ThreadId 为 null）：线程先解析，环境随即可用，第一个沙箱工具调用就能建出沙箱并写进
    /// 解析出来的那个线程目录。沙箱是按需创建的，所以这里必须真的调一次工具，只看环境非空证明不了首轮有沙箱。
    /// </summary>
    [Fact]
    public async Task Pipeline_NullThreadId_ThreadResolvedBeforeThreadDataAndSandbox()
    {
        var resolved = Guid.NewGuid();
        var threadService = ThreadServiceResolving(resolved, isNew: true);
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-tr-{Guid.NewGuid():N}");
        var sandboxOptions = SandboxTestSupport.SandboxOptions(tempDir);
        var accessor = new AgentExecutionContextAccessor();
        var provider = new SandboxTestSupport.CountingSandboxProvider();
        var translator = new VirtualPathTranslator(tempDir);

        var pipeline = new AiMiddlewarePipeline()
            .Use(new ThreadResolutionMiddleware(threadService.Object, NullLogger<ThreadResolutionMiddleware>.Instance))
            .Use(SandboxTestSupport.CreateThreadDataMiddleware(sandboxOptions))
            .Use(SandboxTestSupport.CreateSandboxMiddleware(sandboxOptions, provider, accessor))
            .Use(new HistoryMiddleware(threadService.Object, new StaticOptionsMonitor<AIOptions>(new AIOptions()), NullLogger<HistoryMiddleware>.Instance));

        var context = CreateContext(threadId: null);
        var properties = accessor.Properties;
        ThreadDataState? threadData = null;
        SandboxToolEnvironment? environment = null;
        object? written = null;

        try
        {
            var run = pipeline.Build(async (ctx, ct) =>
            {
                threadData = ctx.Properties.GetValueOrDefault(SandboxPropertyKeys.ThreadData) as ThreadDataState;
                environment = accessor.Properties.GetValueOrDefault(SandboxPropertyKeys.ToolEnvironment) as SandboxToolEnvironment;
                var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance, accessor);
                written = await tools.WriteFileAsync("/mnt/workspace/first-turn.txt", "hello", ct: ct);
                return new AgentRunResult { Response = "ok" };
            });
            await run(context, CancellationToken.None);

            threadData.ShouldNotBeNull("the first turn of a new conversation must have thread data");
            threadData!.WorkspacePath.ShouldContain(resolved.ToString("N"));
            environment.ShouldNotBeNull("the first turn of a new conversation must have a sandbox environment");
            environment!.ThreadId.ShouldBe(resolved);
            provider.CreateCalls.ShouldBe(1, "the first tool call of the first turn must create the sandbox");
            JsonSerializer.Serialize(written).ShouldContain("\"success\":true");
            File.Exists(Path.Combine(translator.GetThreadDirectory(resolved), "workspace", "first-turn.txt")).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Pipeline_EphemeralRequest_NeverTouchesThreadService()
    {
        // 评估用例逐条跑在评估者名下（Ephemeral=true）：History 早已跳过建线程，
        // 但本中间件在它之前跑，漏掉同一道守卫就等于每个用例又留下一条空 AgentThread，
        // 而且 ThreadData / Sandbox 会为每个一次性用例各开一份目录与沙箱。
        var threadService = new Mock<IAgentThreadInternalService>(MockBehavior.Strict);
        var pipeline = new AiMiddlewarePipeline()
            .Use(new ThreadResolutionMiddleware(threadService.Object, NullLogger<ThreadResolutionMiddleware>.Instance))
            .Use(new HistoryMiddleware(threadService.Object, new StaticOptionsMonitor<AIOptions>(new AIOptions()), NullLogger<HistoryMiddleware>.Instance));
        var context = CreateContext(threadId: null, ephemeral: true);

        Guid? seenByNext = Guid.Empty;
        var run = pipeline.Build((ctx, _) =>
        {
            seenByNext = ctx.Request.ThreadId;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });
        await run(context, CancellationToken.None);

        seenByNext.ShouldBeNull("an ephemeral run must reach the executor without a thread");
        context.IsNewThread.ShouldBeFalse();
        threadService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeStreamingAsync_EphemeralRequest_NeverTouchesThreadService()
    {
        var threadService = new Mock<IAgentThreadInternalService>(MockBehavior.Strict);
        var mw = new ThreadResolutionMiddleware(threadService.Object, NullLogger<ThreadResolutionMiddleware>.Instance);
        var context = CreateContext(threadId: null, ephemeral: true);

        await foreach (var _ in mw.InvokeStreamingAsync(context, (_, _) => Empty()))
        {
        }

        context.Request.ThreadId.ShouldBeNull();
        threadService.VerifyNoOtherCalls();
    }

    private static async IAsyncEnumerable<AgentStreamChunk> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
