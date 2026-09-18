namespace Tnzi.AI.Tests;

/// <summary>
/// 非流式 <c>AgentRuntime.RunAsync</c> 被取消时运行记录的收尾状态。
/// </summary>
/// <remarks>
/// kill_agent / 管理端 cancel 先把行写成 Cancelled，再触发后台任务的 CTS；取消从管线一路以
/// <see cref="OperationCanceledException"/> 抛到 RunAsync。此前那里只有 <c>catch (Exception)</c>，
/// 经 <c>UpdateRunOnFailureAsync</c> 把 Cancelled 覆盖成 Failed("The operation was canceled.") 并发
/// RunFailed 事件 —— 管理端看到的是 Failed、CanResume=true，kill / 超时 / 真失败三者的 Error 文本一字不差。
/// 流式路径早有 <c>FinalizeStreamingCancelledAsync</c>，只是非流式没接。
/// </remarks>
public class AgentRuntimeCancellationTests
{
    [Fact]
    public async Task RunAsync_CancelledByCaller_FinalizesRunAsCancelledAndDoesNotPublishFailed()
    {
        var harness = new Harness();
        using var cts = new CancellationTokenSource();
        harness.Middleware.OnInvoke = async ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return new AgentRunResult { Response = "unreachable" };
        };

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Runtime.RunAsync(harness.Request, cts.Token));

        harness.Run.Status.ShouldBe(AgentRunStatus.Cancelled);
        harness.Run.Error.ShouldNotBeNullOrWhiteSpace();
        harness.Run.Error.ShouldNotContain("The operation was canceled");
        harness.EventPublisher.Verify(x => x.PublishRunFailedEventAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRun?>(), It.IsAny<Exception>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
        harness.EventPublisher.Verify(x => x.PublishRunCancelledEventAsync(
            It.IsAny<AgentRunRequest>(), harness.Run, It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Once);
        harness.Traces.ShouldContain(t => t.EventType == AgentTraceEventTypes.RunCancelled);
        harness.Traces.ShouldNotContain(t => t.EventType == AgentTraceEventTypes.Error);
    }

    [Fact]
    public async Task RunAsync_KilledThroughRegistry_RecordsTheKillReason()
    {
        var registry = new SubAgentRunCancellationRegistry();
        var harness = new Harness(registry);
        var cts = new CancellationTokenSource();
        registry.Register(harness.Run.Id, cts);
        harness.Middleware.OnInvoke = async ct =>
        {
            registry.TryCancel(harness.Run.Id, "Cancelled by the operator").ShouldBeTrue();
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return new AgentRunResult { Response = "unreachable" };
        };

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Runtime.RunAsync(harness.Request, cts.Token));

        harness.Run.Status.ShouldBe(AgentRunStatus.Cancelled);
        harness.Run.Error.ShouldBe("Cancelled by the operator");
    }

    /// <summary>
    /// 超时不是用户的决定，是运行没干完：按 Failed 收尾（可续跑 / 重试），但 Error 必须说清是超时。
    /// </summary>
    [Fact]
    public async Task RunAsync_TimedOutThroughRegistry_FinalizesAsFailedWithTimeoutReason()
    {
        var registry = new SubAgentRunCancellationRegistry();
        var harness = new Harness(registry);
        var cts = new CancellationTokenSource();
        registry.Register(harness.Run.Id, cts, TimeSpan.FromMilliseconds(50));
        harness.Middleware.OnInvoke = async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new AgentRunResult { Response = "unreachable" };
        };

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Runtime.RunAsync(harness.Request, cts.Token));

        harness.Run.Status.ShouldBe(AgentRunStatus.Failed);
        harness.Run.Error.ShouldNotBeNull();
        harness.Run.Error.ShouldContain("timed out", Case.Insensitive);
        harness.EventPublisher.Verify(x => x.PublishRunFailedEventAsync(
            It.IsAny<AgentRunRequest>(), harness.Run, It.IsAny<Exception>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// 令牌没被取消却抛 OperationCanceledException（内部 HTTP 客户端超时之类）：那是失败，不是取消。
    /// </summary>
    [Fact]
    public async Task RunAsync_OperationCanceledWithoutCancelledToken_StillFinalizesAsFailed()
    {
        var harness = new Harness();
        harness.Middleware.OnInvoke = _ => throw new OperationCanceledException("inner http timeout");

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Runtime.RunAsync(harness.Request, CancellationToken.None));

        harness.Run.Status.ShouldBe(AgentRunStatus.Failed);
        harness.Run.Error.ShouldBe("inner http timeout");
        harness.EventPublisher.Verify(x => x.PublishRunFailedEventAsync(
            It.IsAny<AgentRunRequest>(), harness.Run, It.IsAny<Exception>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// 注册表：TryCancel 先记原因再触发；超时由注册表自己的计时器经同一条路走，收尾时分得清是哪一种。
    /// </summary>
    [Fact]
    public void Registry_RecordsWhyARunWasCancelled()
    {
        var registry = new SubAgentRunCancellationRegistry();
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        registry.Register(runId, cts);

        registry.GetCancellation(runId).ShouldBeNull();
        registry.TryCancel(runId, "Cancelled by user").ShouldBeTrue();

        cts.IsCancellationRequested.ShouldBeTrue();
        var cancellation = registry.GetCancellation(runId);
        cancellation.ShouldNotBeNull();
        cancellation.Kind.ShouldBe(RunCancellationKind.Killed);
        cancellation.Reason.ShouldBe("Cancelled by user");

        registry.Unregister(runId).ShouldBe(cts);
        registry.GetCancellation(runId).ShouldBeNull();
    }

    [Fact]
    public async Task Registry_TimeoutFiresThroughTheRegistryAndIsMarkedTimedOut()
    {
        var registry = new SubAgentRunCancellationRegistry();
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        registry.Register(runId, cts, TimeSpan.FromMilliseconds(30));

        var tcs = new TaskCompletionSource();
        cts.Token.Register(() => tcs.TrySetResult());
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancellation = registry.GetCancellation(runId);
        cancellation.ShouldNotBeNull();
        cancellation.Kind.ShouldBe(RunCancellationKind.TimedOut);
        cancellation.Reason.ShouldContain("timed out", Case.Insensitive);
    }

    private sealed class Harness
    {
        public AgentRun Run { get; }
        public AgentRunRequest Request { get; }
        public AgentRuntime Runtime { get; }
        public Mock<IEventPublisher> EventPublisher { get; } = new();
        public List<AgentRunTrace> Traces { get; } = [];
        public ProbeMiddleware Middleware { get; } = new();

        public Harness(ISubAgentRunCancellationRegistry? registry = null)
        {
            var agentId = Guid.NewGuid();
            Run = new AgentRun { Id = Guid.NewGuid(), AgentId = agentId, Status = AgentRunStatus.Pending, Nodes = [] };
            Request = new AgentRunRequest { AgentId = agentId, UserMessage = "work", EnableRunTracking = true, ExistingRunId = Run.Id };

            var runStore = new Mock<IRunStore>();
            runStore.Setup(x => x.GetAsync(Run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Run);
            runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var traceStore = new Mock<ITraceStore>();
            traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentRunTrace trace, CancellationToken _) =>
                {
                    Traces.Add(trace);
                    return trace;
                });

            var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(agentId, null, null, null, It.IsAny<CancellationToken>(), null))
                .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "test", "gpt-5", agentId));

            var sp = new ServiceCollection()
                .AddSingleton<IAiMiddleware>(Middleware)
                .BuildServiceProvider();

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                runTracker,
                Mock.Of<IWorkflowDelegator>(),
                new AgentExecutionContextAccessor(),
                sp,
                Mock.Of<IOptionsMonitor<AIOptions>>(),
                EventPublisher.Object,
                Mock.Of<ILogger<AgentRuntime>>(),
                cancellationRegistry: registry);
        }
    }

    private sealed class ProbeMiddleware : IAiMiddleware
    {
        public Func<CancellationToken, Task<AgentRunResult>> OnInvoke { get; set; } = _ => Task.FromResult(new AgentRunResult { Response = "unreachable" });

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => OnInvoke(cancellationToken);

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
