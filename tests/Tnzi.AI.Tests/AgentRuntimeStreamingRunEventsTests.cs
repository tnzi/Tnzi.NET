namespace Tnzi.AI.Tests;

/// <summary>
/// 流式运行的终态事件：每次运行都发，与是否开运行追踪无关。
/// </summary>
/// <remarks>
/// 聊天与管理端的流式运行都不开运行追踪（<c>run</c> 为 null）。此前 <c>FinalizeStreamingAsync</c> 把
/// Completed / Cancelled / Failed 三个事件都放在 <c>run != null</c> 里，而 Started 无条件发出，
/// 订阅终态事件做统计与告警的宿主看到的是一批永远没结束的运行，主路径上的失败也从不告警。
/// </remarks>
public class AgentRuntimeStreamingRunEventsTests
{
    [Fact]
    public async Task Untracked_CompletedStream_PublishesCompletedOnce()
    {
        var harness = new Harness(trackRun: false);
        harness.Middleware.Stream = (_, _) => Chunks(new AgentStreamChunk { Text = "hi" }, new AgentStreamChunk { FinishReason = FinishReasons.Stop });

        await DrainAsync(harness.Runtime.RunStreamingAsync(harness.Request));

        harness.EventPublisher.Verify(x => x.PublishRunCompletedEventAsync(
            harness.Request, It.Is<AgentRunResult>(r => r.Response == "hi"), null, It.IsAny<long>(), true, It.IsAny<string?>()), Times.Once);
        VerifyNoCancelledOrFailed(harness);
    }

    [Fact]
    public async Task Untracked_CallerCancelsStream_PublishesCancelledOnce()
    {
        var harness = new Harness(trackRun: false);
        using var cts = new CancellationTokenSource();
        harness.Middleware.Stream = (_, ct) => CancelMidStream(cts, ct);

        await Should.ThrowAsync<OperationCanceledException>(() => DrainAsync(harness.Runtime.RunStreamingAsync(harness.Request, cts.Token)));

        harness.EventPublisher.Verify(x => x.PublishRunCancelledEventAsync(
            harness.Request, null, It.IsAny<string>(), It.IsAny<long>(), true), Times.Once);
        harness.EventPublisher.Verify(x => x.PublishRunCompletedEventAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRunResult>(), It.IsAny<AgentRun?>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        harness.EventPublisher.Verify(x => x.PublishRunFailedEventAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRun?>(), It.IsAny<Exception>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Untracked_PipelineThrows_PublishesFailedOnce()
    {
        var harness = new Harness(trackRun: false);
        harness.Middleware.Stream = (_, _) => ThrowAfterFirstChunk();

        await Should.ThrowAsync<InvalidOperationException>(() => DrainAsync(harness.Runtime.RunStreamingAsync(harness.Request)));

        harness.EventPublisher.Verify(x => x.PublishRunFailedEventAsync(
            harness.Request, null, It.IsAny<Exception>(), It.IsAny<long>(), true), Times.Once);
        harness.EventPublisher.Verify(x => x.PublishRunCompletedEventAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRunResult>(), It.IsAny<AgentRun?>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Tracked_CompletedStream_PublishesCompletedOnceAndPersistsTheRun()
    {
        var harness = new Harness(trackRun: true);
        harness.Middleware.Stream = (_, _) => Chunks(new AgentStreamChunk { Text = "hi" }, new AgentStreamChunk { FinishReason = FinishReasons.Stop });

        await DrainAsync(harness.Runtime.RunStreamingAsync(harness.Request));

        harness.EventPublisher.Verify(x => x.PublishRunCompletedEventAsync(
            harness.Request, It.IsAny<AgentRunResult>(), harness.Run, It.IsAny<long>(), true, It.IsAny<string?>()), Times.Once);
        harness.Run.Status.ShouldBe(AgentRunStatus.Completed);
        VerifyNoCancelledOrFailed(harness);
    }

    private static void VerifyNoCancelledOrFailed(Harness harness)
    {
        harness.EventPublisher.Verify(x => x.PublishRunCancelledEventAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRun?>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
        harness.EventPublisher.Verify(x => x.PublishRunFailedEventAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRun?>(), It.IsAny<Exception>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
    }

    private static async Task DrainAsync(IAsyncEnumerable<AgentStreamChunk> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

#pragma warning disable CS1998 // 迭代器只按需 yield
    private static async IAsyncEnumerable<AgentStreamChunk> Chunks(params AgentStreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<AgentStreamChunk> CancelMidStream(CancellationTokenSource cts, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new AgentStreamChunk { Text = "partial" };
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        yield return new AgentStreamChunk { Text = "unreachable" };
    }

    private static async IAsyncEnumerable<AgentStreamChunk> ThrowAfterFirstChunk()
    {
        yield return new AgentStreamChunk { Text = "partial" };
        throw new InvalidOperationException("provider exploded");
    }
#pragma warning restore CS1998

    private sealed class Harness
    {
        public AgentRun Run { get; }
        public AgentRunRequest Request { get; }
        public AgentRuntime Runtime { get; }
        public Mock<IEventPublisher> EventPublisher { get; } = new();
        public StreamProbeMiddleware Middleware { get; } = new();

        public Harness(bool trackRun)
        {
            var agentId = Guid.NewGuid();
            Run = new AgentRun { Id = Guid.NewGuid(), AgentId = agentId, Status = AgentRunStatus.Pending, Nodes = [] };
            Request = trackRun
                ? new AgentRunRequest { AgentId = agentId, UserMessage = "work", EnableRunTracking = true, ExistingRunId = Run.Id }
                : new AgentRunRequest { AgentId = agentId, UserMessage = "work" };

            var runStore = new Mock<IRunStore>();
            runStore.Setup(x => x.GetAsync(Run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Run);
            runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var runTracker = new RunTracker(runStore.Object, Mock.Of<ITraceStore>(), Mock.Of<ILogger<RunTracker>>());

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
                Mock.Of<ILogger<AgentRuntime>>());
        }
    }

    private sealed class StreamProbeMiddleware : IAiMiddleware
    {
        public Func<AiMiddlewareContext, CancellationToken, IAsyncEnumerable<AgentStreamChunk>> Stream { get; set; }
            = (_, _) => Chunks();

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRunResult { Response = "unused" });

        public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => Stream(context, cancellationToken);
    }
}
