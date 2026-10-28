using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// 同一会话的消息按到达顺序逐条处理，不同会话之间照常并发。
/// </summary>
/// <remarks>
/// 此前每条入站消息各自 <c>Task.Run</c>，只受全局并发上限约束：一个用户连发两条，两次运行读同一份历史、
/// 回复按完成先后送达；新会话的两条首发消息各建一条线程，映射后写者胜，前一条线程成了孤儿。
/// </remarks>
public sealed class ChannelManagerOrderingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SameChat_SecondMessageWaitsForTheFirst_AndContinuesItsThread_OtherChatsRunMeanwhile()
    {
        var runtime = new GatedRuntime();
        var store = new MemoryStore();
        var (manager, bus) = CreateManager(runtime, store, maxConcurrency: 3);
        var gateA1 = runtime.Gate("a1");

        await manager.StartAsync();
        try
        {
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-a", "u1", "a1"));
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-a", "u1", "a2"));
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-b", "u2", "b1"));

            await runtime.WaitStartedAsync("a1");
            await runtime.WaitStartedAsync("b1");

            // a1 还卡着：同一会话的 a2 不能开跑
            await Task.Delay(300);
            runtime.HasStarted("a2").ShouldBeFalse("a message must not run while an earlier one of the same chat is still running");

            gateA1.SetResult();
            await runtime.WaitStartedAsync("a2");

            var a1Thread = runtime.ResultThread("a1");
            runtime.RequestThread("a2").ShouldBe(a1Thread, "the second message of a new chat must run in the thread the first one created");
            (await store.GetThreadIdAsync("telegram", "chat-a")).ShouldBe(a1Thread);
        }
        finally
        {
            gateA1.TrySetResult();
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task OneChattyChat_TakesOneSlot_AndDoesNotStarveOtherChats()
    {
        // 并发上限 2：连发三条的会话只占一个槽，另一个会话不必等它三条都处理完
        var runtime = new GatedRuntime();
        var (manager, bus) = CreateManager(runtime, new MemoryStore(), maxConcurrency: 2);
        var gateA1 = runtime.Gate("a1");

        await manager.StartAsync();
        try
        {
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-a", "u1", "a1"));
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-a", "u1", "a2"));
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-a", "u1", "a3"));
            await bus.PublishInboundAsync(new InboundMessage("telegram", "chat-b", "u2", "b1"));

            await runtime.WaitStartedAsync("a1");
            await runtime.WaitStartedAsync("b1");
            runtime.HasStarted("a2").ShouldBeFalse();

            gateA1.SetResult();
            await runtime.WaitStartedAsync("a3");
            runtime.StartOrder.Where(t => t.StartsWith('a')).ShouldBe(["a1", "a2", "a3"]);
        }
        finally
        {
            gateA1.TrySetResult();
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task SameChat_DifferentTopics_RunConcurrently()
    {
        // 话题是线程映射键的一部分：同一个群里的两个论坛话题是两条线程，互不等待
        var runtime = new GatedRuntime();
        var (manager, bus) = CreateManager(runtime, new MemoryStore(), maxConcurrency: 2);
        var gate = runtime.Gate("t1");

        await manager.StartAsync();
        try
        {
            await bus.PublishInboundAsync(new InboundMessage("telegram", "group", "u1", "t1", TopicId: "1"));
            await bus.PublishInboundAsync(new InboundMessage("telegram", "group", "u1", "t2", TopicId: "2"));

            await runtime.WaitStartedAsync("t1");
            await runtime.WaitStartedAsync("t2");
        }
        finally
        {
            gate.TrySetResult();
            await manager.StopAsync();
        }
    }

    private static (ChannelManager Manager, InMemoryChannelMessageBus Bus) CreateManager(
        IAgentRuntime runtime, IChannelThreadStore store, int maxConcurrency)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runtime);
        services.AddSingleton(store);
        services.AddSingleton(Mock.Of<IAgentThreadService>());
        var provider = services.BuildServiceProvider();

        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance, bus, provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ChannelsModuleOptions { Enabled = true, MaxConcurrency = maxConcurrency }));
        return (manager, bus);
    }

    /// <summary>按消息文本放行的运行时：登记过闸门的消息卡在闸门上，其余立刻完成；新会话时新建线程。</summary>
    private sealed class GatedRuntime : IAgentRuntime
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new();
        private readonly ConcurrentDictionary<string, Guid?> _requestThreads = new();
        private readonly ConcurrentDictionary<string, Guid> _resultThreads = new();
        private readonly ConcurrentQueue<string> _startOrder = new();

        public IReadOnlyList<string> StartOrder => [.. _startOrder];

        public TaskCompletionSource Gate(string text)
            => _gates.GetOrAdd(text, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public bool HasStarted(string text) => _started.TryGetValue(text, out var tcs) && tcs.Task.IsCompleted;

        public Task WaitStartedAsync(string text) => Started(text).Task.WaitAsync(Timeout);

        public Guid? RequestThread(string text) => _requestThreads[text];

        public Guid ResultThread(string text) => _resultThreads[text];

        public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            var text = request.UserMessage!;
            _requestThreads[text] = request.ThreadId;
            _startOrder.Enqueue(text);
            Started(text).TrySetResult();

            if (_gates.TryGetValue(text, out var gate))
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            var threadId = request.ThreadId ?? Guid.NewGuid();
            _resultThreads[text] = threadId;
            return new AgentRunResult { Response = "ok:" + text, ThreadId = threadId };
        }

        public IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentRunResult> ResumeAsync(Guid runId, ResumeRunInput? input = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private TaskCompletionSource Started(string text)
            => _started.GetOrAdd(text, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private sealed class MemoryStore : IChannelThreadStore
    {
        private readonly ConcurrentDictionary<string, Guid> _mappings = new(StringComparer.Ordinal);

        public Task<Guid?> GetThreadIdAsync(string channelName, string chatId, string? topicId = null)
            => Task.FromResult(_mappings.TryGetValue($"{channelName}:{chatId}:{topicId}", out var id) ? id : (Guid?)null);

        public Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null)
        {
            _mappings[$"{channelName}:{chatId}:{topicId}"] = threadId;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string channelName, string chatId, string? topicId = null)
        {
            _mappings.TryRemove($"{channelName}:{chatId}:{topicId}", out _);
            return Task.CompletedTask;
        }
    }
}
