namespace Tnzi.AI.Tests;

/// <summary>
/// 续跑（管理端 resume / send_agent_input）期间的取消。
/// </summary>
/// <remarks>
/// kill_agent 与管理端取消先把行写成 Cancelled，再经取消注册表触发运行的令牌。此前续跑不进注册表：
/// 取消只改了行，续跑照样把工具跑完，收尾再用开始时读到的那份实体把行写回 Completed。
/// </remarks>
public class AgentRuntimeResumeCancellationTests
{
    [Fact]
    public async Task ResumeAsync_KilledWhileResuming_StopsTheRunAndRecordsTheKill()
    {
        var harness = new Harness();
        using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        harness.Middleware.OnInvoke = async ct =>
        {
            harness.Middleware.Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new AgentRunResult { Response = "unreachable" };
        };

        var resume = harness.Runtime.ResumeAsync(harness.Run.Id, null, safetyNet.Token);
        await harness.Middleware.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 与 AgentRunSignalDispatcher.CancelAsync 同序：先写行，再触发令牌
        harness.StoredStatus = AgentRunStatus.Cancelled;
        var cancelled = harness.Registry.TryCancel(harness.Run.Id, "Cancelled by the operator");
        if (!cancelled)
        {
            // 续跑不在注册表里时 kill 打不到它；让测试按断言失败而不是挂住
            await safetyNet.CancelAsync();
        }

        cancelled.ShouldBeTrue("a resumed run must be registered so a kill reaches it");
        await Should.ThrowAsync<OperationCanceledException>(() => resume);
        harness.Run.Status.ShouldBe(AgentRunStatus.Cancelled);
        harness.Run.Error.ShouldBe("Cancelled by the operator");
        harness.Registry.GetCancellation(harness.Run.Id).ShouldBeNull("the resume must unregister itself when it ends");
    }

    [Fact]
    public async Task ResumeAsync_CancelledWhileFinishing_DoesNotOverwriteTheCancellation()
    {
        // 取消落在续跑已经不再看令牌的那一刻：管线照常返回结果，行却已被写成 Cancelled
        var harness = new Harness();
        harness.Middleware.OnInvoke = _ =>
        {
            harness.StoredStatus = AgentRunStatus.Cancelled;
            return Task.FromResult(new AgentRunResult { Response = "done", FinishReason = FinishReasons.Stop });
        };

        var result = await harness.Runtime.ResumeAsync(harness.Run.Id);

        harness.Run.Status.ShouldBe(AgentRunStatus.Cancelled);
        result.Status.ShouldBe(AgentRunStatus.Cancelled);
    }

    [Fact]
    public async Task ResumeAsync_CompletesNormally_FinalizesAsCompletedAndUnregisters()
    {
        var harness = new Harness();
        harness.Middleware.OnInvoke = _ =>
            Task.FromResult(new AgentRunResult { Response = "done", FinishReason = FinishReasons.Stop });

        var result = await harness.Runtime.ResumeAsync(harness.Run.Id);

        result.Status.ShouldBe(AgentRunStatus.Completed);
        harness.Run.Status.ShouldBe(AgentRunStatus.Completed);
        harness.Run.OutputSummary.ShouldBe("done");
        harness.Registry.TryCancel(harness.Run.Id, "late").ShouldBeFalse("nothing may stay registered after the resume ends");
    }

    private sealed class Harness
    {
        public Harness()
        {
            Run = new AgentRun
            {
                Id = Guid.NewGuid(),
                AgentId = Guid.NewGuid(),
                Status = AgentRunStatus.RequiresClarification,
                Nodes = []
            };
            StoredStatus = Run.Status;

            var runStore = new Mock<IRunStore>();
            runStore.Setup(x => x.GetWithNodesAsync(Run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Run);
            runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
                .Callback<AgentRun, CancellationToken>((run, _) => StoredStatus = run.Status)
                .Returns(Task.CompletedTask);
            // 存储里的状态独立于手里那份实体：别的作用域写进去的 Cancelled 只有读存储才看得见
            runStore.Setup(x => x.GetStatusAsync(Run.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (AgentRunStatus?)StoredStatus);

            var traceStore = new Mock<ITraceStore>();
            traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

            var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()))
                .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "test", "gpt-5", Run.AgentId));

            Middleware = new ScriptedMiddleware();
            var sp = new ServiceCollection().AddSingleton<IAiMiddleware>(Middleware).BuildServiceProvider();

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                runTracker,
                Mock.Of<IWorkflowDelegator>(),
                new AgentExecutionContextAccessor(),
                sp,
                new StaticOptionsMonitor<AIOptions>(new AIOptions()),
                Mock.Of<IEventPublisher>(),
                Mock.Of<ILogger<AgentRuntime>>(),
                cancellationRegistry: Registry);
        }

        public AgentRun Run { get; }
        public AgentRunStatus StoredStatus { get; set; }
        public SubAgentRunCancellationRegistry Registry { get; } = new();
        public ScriptedMiddleware Middleware { get; }
        public AgentRuntime Runtime { get; }
    }

    private sealed class ScriptedMiddleware : IAiMiddleware
    {
        public Func<CancellationToken, Task<AgentRunResult>> OnInvoke { get; set; } =
            _ => Task.FromResult(new AgentRunResult { Response = "ok" });

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => OnInvoke(cancellationToken);

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
