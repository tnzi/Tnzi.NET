namespace Tnzi.AI.Tests;

/// <summary>
/// 运行摘要（<c>AgentRun.InputSummary</c>）取模型实际收到的用户文本：输入守卫遮蔽过的内容不留在管理端的运行列表里。
/// </summary>
/// <remarks>
/// 运行行在管道之前建好，那时只有请求原文。守卫把卡号脱敏之后，线程历史与标题都只见到脱敏文本，
/// 运行摘要却仍是原文，管理端的运行列表与详情原样显示卡号。完成、失败、流式三种收尾都要写成脱敏后的文本；
/// 没人改写时摘要保持原样；续跑用的请求快照仍是原文（续跑会再过一遍守卫）。
/// </remarks>
public class AgentRuntimeInputSummaryTests
{
    private const string RawMessage = "my card is 4111 1111 1111 1111";
    private const string RedactedMessage = "my card is [REDACTED]";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_redacted_user_turn_is_what_the_run_summary_keeps(bool streaming)
    {
        var harness = new Harness();
        harness.Middleware.OnBefore = context => context.EffectiveUserMessage = RedactedMessage;

        await harness.RunAsync(streaming);

        harness.PersistedSummaries.ShouldNotBeEmpty();
        harness.PersistedSummaries[^1].ShouldBe(RedactedMessage);
        harness.PersistedSummaries[^1].ShouldNotContain("4111");
    }

    [Fact]
    public async Task A_failed_run_also_keeps_the_redacted_text()
    {
        var harness = new Harness();
        harness.Middleware.OnBefore = context => context.EffectiveUserMessage = RedactedMessage;
        harness.Middleware.Throw = true;

        await Should.ThrowAsync<InvalidOperationException>(() => harness.RunAsync(streaming: false));

        harness.PersistedSummaries[^1].ShouldBe(RedactedMessage);
    }

    [Fact]
    public async Task Redacted_content_parts_are_summarised_by_their_text()
    {
        var harness = new Harness(new AgentRunRequest
        {
            AgentId = Guid.NewGuid(),
            ContentParts = [new TextContentPartDto { Text = RawMessage }],
            EnableRunTracking = true
        });
        harness.Middleware.OnBefore = context =>
        {
            context.EffectiveUserMessage = RedactedMessage;
            context.EffectiveContentParts = [new TextContentPartDto { Text = RedactedMessage }];
        };

        await harness.RunAsync(streaming: false);

        harness.PersistedSummaries[^1].ShouldBe(RedactedMessage);
    }

    /// <summary>对照：没人改写本轮输入时，摘要就是建行时记下的请求原文。</summary>
    [Fact]
    public async Task Without_a_rewrite_the_summary_is_the_request_message()
    {
        var harness = new Harness();

        await harness.RunAsync(streaming: false);

        harness.PersistedSummaries.ShouldAllBe(s => s == RawMessage);
    }

    /// <summary>续跑按建行时的请求快照重建请求：快照里仍是原文，守卫在续跑时再过一遍。</summary>
    [Fact]
    public async Task The_resume_snapshot_keeps_the_original_request()
    {
        var harness = new Harness();
        harness.Middleware.OnBefore = context => context.EffectiveUserMessage = RedactedMessage;

        await harness.RunAsync(streaming: false);

        AgentRunRequestSnapshot.Parse(harness.CreatedRun!.RequestSnapshot)!.UserMessage.ShouldBe(RawMessage);
    }

    private sealed class Harness
    {
        public AgentRunRequest Request { get; }
        public AgentRuntime Runtime { get; }
        public ProbeMiddleware Middleware { get; } = new();
        public List<string> PersistedSummaries { get; } = [];
        public AgentRun? CreatedRun { get; private set; }

        public Harness(AgentRunRequest? request = null)
        {
            Request = request ?? new AgentRunRequest { AgentId = Guid.NewGuid(), UserMessage = RawMessage, EnableRunTracking = true };
            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(Request.AgentId, null, null, null, It.IsAny<CancellationToken>(), null))
                .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "test", "gpt-5", Request.AgentId));

            // 记下每一次落库时摘要的值：实体是同一个实例，事后读只能看到最后的状态。
            var runStore = new Mock<IRunStore>();
            runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
                .Callback<AgentRun, CancellationToken>((run, _) =>
                {
                    CreatedRun = run;
                    PersistedSummaries.Add(run.InputSummary);
                })
                .ReturnsAsync((AgentRun run, CancellationToken _) => run);
            runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
                .Callback<AgentRun, CancellationToken>((run, _) => PersistedSummaries.Add(run.InputSummary))
                .Returns(Task.CompletedTask);
            runStore.Setup(x => x.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AgentRunStatus.Running);

            var sp = new ServiceCollection()
                .AddSingleton<IAiMiddleware>(Middleware)
                .BuildServiceProvider();

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                new RunTracker(runStore.Object, Mock.Of<ITraceStore>(), Mock.Of<ILogger<RunTracker>>()),
                Mock.Of<IWorkflowDelegator>(),
                new AgentExecutionContextAccessor(),
                sp,
                Mock.Of<IOptionsMonitor<AIOptions>>(),
                Mock.Of<IEventPublisher>(),
                Mock.Of<ILogger<AgentRuntime>>());
        }

        public async Task RunAsync(bool streaming)
        {
            if (!streaming)
            {
                await Runtime.RunAsync(Request);
                return;
            }

            await foreach (var _ in Runtime.RunStreamingAsync(Request))
            {
            }
        }
    }

    /// <summary>在管道里改写本轮输入（守卫的形状），可选随后抛出。</summary>
    private sealed class ProbeMiddleware : IAiMiddleware
    {
        public Action<AiMiddlewareContext> OnBefore { get; set; } = _ => { };

        public bool Throw { get; set; }

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            OnBefore(context);
            if (Throw)
                throw new InvalidOperationException("the model provider is unavailable");

            return Task.FromResult(new AgentRunResult { Response = "noted", FinishReason = FinishReasons.Stop });
        }

        public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            OnBefore(context);
            return Chunks();
        }

#pragma warning disable CS1998 // 迭代器只按需 yield
        private static async IAsyncEnumerable<AgentStreamChunk> Chunks()
        {
            yield return new AgentStreamChunk { Text = "noted" };
            yield return new AgentStreamChunk { FinishReason = FinishReasons.Stop };
        }
#pragma warning restore CS1998
    }
}
