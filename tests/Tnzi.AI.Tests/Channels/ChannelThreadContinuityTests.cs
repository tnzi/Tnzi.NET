using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Events.Handlers;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using Tnzi.AI.Channels.Store;
using Tnzi.Exceptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// 一个 IM / WebSocket 会话要一直接在同一条线程上，而那条线程用不了时要换一条，而不是从此每条消息都报错。
/// </summary>
/// <remarks>
/// 运行时替身按 <c>ThreadResolutionMiddleware</c> 的形状行事：请求不带线程时新建一条并写回 <c>request.ThreadId</c>
///（运行时从不在流块里携带线程 id），请求带的线程不存在时以 <see cref="ErrorCodes.ThreadNotFound"/> 拒绝。
/// </remarks>
public sealed class ChannelThreadContinuityTests
{
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");

    [Fact]
    public async Task Streaming_NewChat_PersistsTheResolvedThread_AndTheNextMessageContinuesIt()
    {
        var runtime = new ThreadResolvingRuntime();
        var store = new InMemoryThreadStore();
        var gateway = CreateGateway(runtime, store);

        var first = await DrainAsync(gateway.ProcessStreamingAsync(Request("web", "user-1", "My name is Bob")));

        var created = runtime.Threads.ShouldHaveSingleItem();
        (await store.GetThreadIdAsync("web", "user-1")).ShouldBe(created, "the thread the run created must be mapped to the chat");
        first.ShouldAllBe(c => c.ThreadId == created);

        await DrainAsync(gateway.ProcessStreamingAsync(Request("web", "user-1", "What is my name?")));

        runtime.RequestedThreadIds.ShouldBe([null, created], "the second message must run in the first message's thread");
        runtime.Threads.Count.ShouldBe(1, "no second thread may be created for the same chat");
    }

    [Fact]
    public async Task Streaming_TracksTheSessionWithItsThread()
    {
        var runtime = new ThreadResolvingRuntime();
        var gateway = CreateGateway(runtime, new InMemoryThreadStore());

        await DrainAsync(gateway.ProcessStreamingAsync(Request("web", "user-1", "hello")));

        (await gateway.GetSessionsAsync()).ShouldHaveSingleItem().ThreadId.ShouldBe(runtime.Threads.Single());
    }

    [Fact]
    public async Task NonStreaming_MappingToADeletedThread_IsReplacedByANewThread()
    {
        var runtime = new ThreadResolvingRuntime();
        var store = new InMemoryThreadStore();
        var deleted = Guid.NewGuid();
        await store.SetThreadIdAsync("telegram", "c1", deleted);
        var gateway = CreateGateway(runtime, store);

        var response = await gateway.ProcessAsync(Request("telegram", "c1", "hello"));

        response.Success.ShouldBeTrue();
        var created = runtime.Threads.ShouldHaveSingleItem();
        response.ThreadId.ShouldBe(created);
        runtime.RequestedThreadIds.ShouldBe([deleted, null], "the run is retried once without the rejected thread");
        (await store.GetThreadIdAsync("telegram", "c1")).ShouldBe(created);
    }

    [Fact]
    public async Task Streaming_MappingToADeletedThread_IsReplacedByANewThread()
    {
        var runtime = new ThreadResolvingRuntime();
        var store = new InMemoryThreadStore();
        var deleted = Guid.NewGuid();
        await store.SetThreadIdAsync("web", "user-1", deleted);
        var gateway = CreateGateway(runtime, store);

        var chunks = await DrainAsync(gateway.ProcessStreamingAsync(Request("web", "user-1", "hello")));

        var created = runtime.Threads.ShouldHaveSingleItem();
        chunks[^1].FinishReason.ShouldBe("stop");
        string.Concat(chunks.Select(c => c.TextDelta)).ShouldBe("Hi!");
        runtime.RequestedThreadIds.ShouldBe([deleted, null]);
        (await store.GetThreadIdAsync("web", "user-1")).ShouldBe(created);
    }

    [Fact]
    public async Task NonStreaming_OtherFailures_KeepTheMapping_AndAreNotRetried()
    {
        // 别的失败换一条线程也治不好：不删映射，不重跑
        var runtime = new ThreadResolvingRuntime { Failure = new BusinessException("Agent not found", ErrorCodes.AgentNotFound, 404) };
        var store = new InMemoryThreadStore();
        var existing = Guid.NewGuid();
        runtime.Threads.Add(existing);
        await store.SetThreadIdAsync("telegram", "c1", existing);
        var gateway = CreateGateway(runtime, store);

        var response = await gateway.ProcessAsync(Request("telegram", "c1", "hello"));

        response.Success.ShouldBeFalse();
        runtime.RequestedThreadIds.ShouldBe([existing]);
        (await store.GetThreadIdAsync("telegram", "c1")).ShouldBe(existing);
    }

    [Fact]
    public async Task NonStreaming_ExplicitThreadRejected_IsNotRetriedInANewThread()
    {
        // 调用方显式指定的线程被拒是调用方的错：不能悄悄换到一条新线程上去跑
        var runtime = new ThreadResolvingRuntime();
        var gateway = CreateGateway(runtime, new InMemoryThreadStore());
        var missing = Guid.NewGuid();

        var response = await gateway.ProcessAsync(new GatewayRequest
        {
            Channel = "telegram",
            ChatId = "c1",
            UserId = "u1",
            UserMessage = "hello",
            ThreadId = missing
        });

        response.Success.ShouldBeFalse();
        runtime.RequestedThreadIds.ShouldBe([missing]);
    }

    [Fact]
    public async Task Streaming_RuntimeFailure_EndsWithAnErrorChunk_InsteadOfThrowing()
    {
        // 此前异常穿出 ProcessStreamingAsync，拆掉整条 WebSocket 连接
        var runtime = new ThreadResolvingRuntime { Failure = new InvalidOperationException("boom") };
        var store = new InMemoryThreadStore();
        var gateway = CreateGateway(runtime, store);

        var chunks = await DrainAsync(gateway.ProcessStreamingAsync(Request("web", "user-1", "hello")));

        var last = chunks.ShouldHaveSingleItem();
        last.IsFinal.ShouldBeTrue();
        last.FinishReason.ShouldBe("error");
        (last.TextDelta ?? string.Empty).ShouldNotContain("boom", Case.Insensitive, "internal exception details are logged, not echoed");
        (await store.GetThreadIdAsync("web", "user-1")).ShouldBeNull();
    }

    [Fact]
    public async Task DirectRuntimePath_MappingToADeletedThread_IsReplacedByANewThread()
    {
        // 没装 Gateway 的部署走 ChannelManager 直连运行时，同一自愈
        var runtime = new ThreadResolvingRuntime();
        var store = new InMemoryThreadStore();
        var deleted = Guid.NewGuid();
        await store.SetThreadIdAsync("telegram", "c1", deleted);

        var services = new ServiceCollection();
        services.AddSingleton<IAgentRuntime>(runtime);
        services.AddSingleton<IChannelThreadStore>(store);
        services.AddSingleton(Mock.Of<IAgentThreadService>());
        var provider = services.BuildServiceProvider();
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance, bus, provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ChannelsModuleOptions { Enabled = true, MaxConcurrency = 1, DefaultAgentId = AgentId }));

        var reply = new TaskCompletionSource<OutboundMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await bus.SubscribeOutboundAsync(m => { reply.TrySetResult(m); return Task.CompletedTask; });

        await manager.StartAsync();
        try
        {
            await bus.PublishInboundAsync(new InboundMessage("telegram", "c1", "u1", "hello"));
            var outbound = await reply.Task.WaitAsync(TimeSpan.FromSeconds(10));

            outbound.Text.ShouldBe("Hi!");
            var created = runtime.Threads.ShouldHaveSingleItem();
            outbound.ThreadId.ShouldBe(created);
            (await store.GetThreadIdAsync("telegram", "c1")).ShouldBe(created);
        }
        finally
        {
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task FileStore_ThreadDeleted_RemovesEveryMappingThatPointsAtTheThread()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tnzi-channel-continuity-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileChannelThreadStore(NullLogger<FileChannelThreadStore>.Instance, path);
            var deleted = Guid.NewGuid();
            var kept = Guid.NewGuid();
            await store.SetThreadIdAsync("telegram", "c1", deleted);
            await store.SetThreadIdAsync("telegram", "c1", deleted, topicId: "42");
            await store.SetThreadIdAsync("slack", "D9", kept);

            await new ChannelThreadMappingCleanupHandler(store).HandleAsync(new ThreadDeletedEvent { ThreadId = deleted });

            (await store.GetThreadIdAsync("telegram", "c1")).ShouldBeNull();
            (await store.GetThreadIdAsync("telegram", "c1", "42")).ShouldBeNull();
            (await store.GetThreadIdAsync("slack", "D9")).ShouldBe(kept);

            // 落盘的也是删过的那份：换一个实例从文件读回
            var reloaded = new FileChannelThreadStore(NullLogger<FileChannelThreadStore>.Instance, path);
            (await reloaded.GetThreadIdAsync("telegram", "c1")).ShouldBeNull();
            (await reloaded.GetThreadIdAsync("slack", "D9")).ShouldBe(kept);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GatewayRequest Request(string channel, string chatId, string text) => new()
    {
        Channel = channel,
        ChatId = chatId,
        UserId = chatId,
        UserMessage = text
    };

    private static DefaultGateway CreateGateway(IAgentRuntime runtime, IChannelThreadStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runtime);
        services.AddSingleton(store);
        var provider = services.BuildServiceProvider();
        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = AgentId, StreamingThrottleMs = 0 });

        return new DefaultGateway(new DefaultSessionBinder([], options),
            provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<DefaultGateway>.Instance);
    }

    private static async Task<List<GatewayStreamChunk>> DrainAsync(IAsyncEnumerable<GatewayStreamChunk> stream)
    {
        var chunks = new List<GatewayStreamChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>按线程解析中间件的形状行事的运行时替身（见类注释）。</summary>
    private sealed class ThreadResolvingRuntime : IAgentRuntime
    {
        public List<Guid> Threads { get; } = [];

        public List<Guid?> RequestedThreadIds { get; } = [];

        /// <summary>线程解析之后抛出的失败（模拟线程之外的错误）。</summary>
        public Exception? Failure { get; init; }

        public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            Resolve(request);
            return Task.FromResult(new AgentRunResult { Response = "Hi!", ThreadId = request.ThreadId, FinishReason = "stop" });
        }

        public async IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
            AgentRunRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Resolve(request);
            yield return new AgentStreamChunk { Text = "Hi" };
            yield return new AgentStreamChunk { Text = "!", FinishReason = "stop" };
        }

        public Task<AgentRunResult> ResumeAsync(Guid runId, ResumeRunInput? input = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private void Resolve(AgentRunRequest request)
        {
            RequestedThreadIds.Add(request.ThreadId);

            if (request.ThreadId is { } requested && !Threads.Contains(requested))
            {
                throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
            }

            if (request.ThreadId is null)
            {
                var created = Guid.NewGuid();
                Threads.Add(created);
                request.ThreadId = created;
            }

            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    /// <summary>进程内映射存储（键与 <c>FileChannelThreadStore</c> 同形）。</summary>
    private sealed class InMemoryThreadStore : IChannelThreadStore
    {
        private readonly ConcurrentDictionary<string, Guid> _mappings = new(StringComparer.Ordinal);

        public Task<Guid?> GetThreadIdAsync(string channelName, string chatId, string? topicId = null)
            => Task.FromResult(_mappings.TryGetValue(Key(channelName, chatId, topicId), out var id) ? id : (Guid?)null);

        public Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null)
        {
            _mappings[Key(channelName, chatId, topicId)] = threadId;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string channelName, string chatId, string? topicId = null)
        {
            _mappings.TryRemove(Key(channelName, chatId, topicId), out _);
            return Task.CompletedTask;
        }

        private static string Key(string channelName, string chatId, string? topicId) => $"{channelName}:{chatId}:{topicId}";
    }
}
