namespace Tnzi.Tests.EventBus;

/// <summary>
/// <c>MaxConcurrency</c> 与处理器里的再发布：嵌套事件沿用外层已持有的名额。
/// </summary>
/// <remarks>
/// 名额在处理器执行期间一直持有，而处理器经服务再发布事件是常态（支付完成 → 续订 → 续订事件）。
/// 嵌套事件的同步处理器若再去排队，外层处理器占满全部名额时谁也等不到谁：这些请求永远挂起，
/// 之后每一次带同步处理器的发布都排在它们后面，整条本地总线卡死到重启，没有异常也没有日志。
/// </remarks>
public class LocalEventBusNestedPublishTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    public class OuterEvent : EventBase
    {
        public TaskCompletionSource? Gate { get; init; }
        public Action? OnEnter { get; init; }
    }

    public class InnerEvent : EventBase;

    public class InnerRecorder
    {
        public int Calls;
    }

    public class OuterHandler(IEventBus bus) : IEventHandler<OuterEvent>
    {
        public async Task HandleAsync(OuterEvent @event, CancellationToken cancellationToken = default)
        {
            @event.OnEnter?.Invoke();
            if (@event.Gate != null)
                await @event.Gate.Task;

            await bus.PublishAsync(new InnerEvent(), cancellationToken);
        }
    }

    public class InnerHandler(InnerRecorder recorder) : IEventHandler<InnerEvent>
    {
        public Task HandleAsync(InnerEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref recorder.Calls);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HandlerPublishingANestedEvent_CompletesWithASinglePermit()
    {
        var (provider, bus, recorder) = Build(maxConcurrency: 1);
        await using var _ = provider;

        await bus.PublishAsync(new OuterEvent()).WaitAsync(Deadline);

        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task AsManyConcurrentPublishesAsPermits_AllPublishingNestedEvents_AllComplete()
    {
        const int maxConcurrency = 2;
        const int publishes = 5;
        var (provider, bus, recorder) = Build(maxConcurrency);
        await using var _ = provider;

        // 等到外层处理器占满全部名额之后才一起再发布：这正是会互相等死的那个时刻
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        void OnEnter()
        {
            if (Interlocked.Increment(ref entered) == maxConcurrency)
                gate.TrySetResult();
        }

        var tasks = Enumerable.Range(0, publishes)
            .Select(_ => Task.Run(() => bus.PublishAsync(new OuterEvent { Gate = gate, OnEnter = OnEnter })))
            .ToArray();

        await Task.WhenAll(tasks).WaitAsync(Deadline);

        Assert.Equal(publishes, recorder.Calls);
    }

    [Fact]
    public async Task TopLevelPublishes_StillShareTheConfiguredLimit()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var tracker = new ConcurrencyTracker();
        services.AddSingleton(tracker);
        services.AddEventHandler<TrackedEvent, TrackedHandler>();
        await using var provider = services.BuildServiceProvider();
        var bus = new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>(), new EventBusOptions(), maxConcurrency: 1);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => bus.PublishAsync(new TrackedEvent())))).WaitAsync(Deadline);

        Assert.Equal(4, tracker.Completed);
        Assert.Equal(1, tracker.MaxObserved);
    }

    [Fact]
    public async Task BackgroundHandlerFlow_DoesNotInheritThePublishersPermit()
    {
        // 后台处理器是另一条执行流：它的再发布与别的顶层发布一样排队，
        // 不能借着发布方（此刻仍占着名额）的身份绕过上限
        var services = new ServiceCollection();
        services.AddLogging();
        var probe = new BackgroundProbe();
        services.AddSingleton(probe);
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<LocalEventBus>());
        services.AddSingleton(sp => new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>(), new EventBusOptions(), maxConcurrency: 1));
        services.AddEventHandler<HoldingEvent, HoldingHandler>();
        services.AddEventHandler<SpawnEvent, BackgroundSpawnHandler>();
        services.AddEventHandler<LateEvent, LateHandler>();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IEventBus>();

        var publish = bus.PublishAsync(new HoldingEvent());

        // 后台处理器已经发出了它的嵌套发布，而发布方的同步处理器还占着唯一的名额
        await probe.BackgroundPublishing.Task.WaitAsync(Deadline);
        await Task.Delay(300);
        Assert.False(probe.LateStarted.Task.IsCompleted, "the background flow's nested handler ran while the publisher still held the only permit");

        probe.Release.TrySetResult();
        await publish.WaitAsync(Deadline);
        await probe.LateStarted.Task.WaitAsync(Deadline);
    }

    private static (ServiceProvider Provider, IEventBus Bus, InnerRecorder Recorder) Build(int maxConcurrency)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var recorder = new InnerRecorder();
        services.AddSingleton(recorder);
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<LocalEventBus>());
        services.AddSingleton(sp => new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>(), new EventBusOptions(), maxConcurrency: maxConcurrency));
        services.AddEventHandler<OuterEvent, OuterHandler>();
        services.AddEventHandler<InnerEvent, InnerHandler>();
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<IEventBus>(), recorder);
    }

    public class TrackedEvent : EventBase;

    public class ConcurrencyTracker
    {
        private int _current;
        public int MaxObserved;
        public int Completed;

        public void Enter()
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref MaxObserved)) && Interlocked.CompareExchange(ref MaxObserved, now, seen) != seen)
            {
            }
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _current);
            Interlocked.Increment(ref Completed);
        }
    }

    public class TrackedHandler(ConcurrencyTracker tracker) : IEventHandler<TrackedEvent>
    {
        public async Task HandleAsync(TrackedEvent @event, CancellationToken cancellationToken = default)
        {
            tracker.Enter();
            try
            {
                await Task.Delay(50, cancellationToken);
            }
            finally
            {
                tracker.Exit();
            }
        }
    }

    public class HoldingEvent : EventBase;

    public class SpawnEvent : EventBase;

    public class LateEvent : EventBase;

    public class BackgroundProbe
    {
        public TaskCompletionSource BackgroundPublishing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>同步处理器：发出一个只有后台处理器的事件，然后占着名额等测试放行。</summary>
    public class HoldingHandler(IEventBus bus, BackgroundProbe probe) : IEventHandler<HoldingEvent>
    {
        public async Task HandleAsync(HoldingEvent @event, CancellationToken cancellationToken = default)
        {
            await bus.PublishAsync(new SpawnEvent(), cancellationToken);
            await probe.Release.Task;
        }
    }

    [BackgroundEventHandler]
    public class BackgroundSpawnHandler(IEventBus bus, BackgroundProbe probe) : IEventHandler<SpawnEvent>
    {
        public async Task HandleAsync(SpawnEvent @event, CancellationToken cancellationToken = default)
        {
            probe.BackgroundPublishing.TrySetResult();
            await bus.PublishAsync(new LateEvent(), cancellationToken);
        }
    }

    public class LateHandler(BackgroundProbe probe) : IEventHandler<LateEvent>
    {
        public Task HandleAsync(LateEvent @event, CancellationToken cancellationToken = default)
        {
            probe.LateStarted.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
