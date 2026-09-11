namespace Tnzi.Tests.EventBus;

/// <summary>
/// 一个处理器类实现多个 <see cref="IEventHandler{TEvent}"/> 时的派发正确性。
/// </summary>
/// <remarks>
/// <para>
/// 回归背景：<c>EventHandlerInvoker</c> 早先按 <c>handlerType</c> 单键缓存元数据，且用
/// <c>GetInterfaces().FirstOrDefault(...)</c> 取<b>第一个</b> <c>IEventHandler&lt;&gt;</c> 接口
/// 来编译委托。于是一个实现了两个事件接口的处理器只会编译出一个委托，绑死在反射恰好先返回的
/// 那个接口上；派发另一个事件时 <c>Expression.Convert</c> 转换到错误的事件类型，
/// 抛 <c>InvalidCastException</c>。
/// </para>
/// <para>
/// 症状之所以隐蔽，是因为总线的错误隔离把异常收进日志：<b>处理器方法压根没跑</b>，
/// 但发布方一切正常返回。三个总线（Local / Kafka / RabbitMQ）共用这个调用器，
/// 所以这条缺陷不属于任何单个模块。
/// </para>
/// <para>
/// 这些用例刻意<b>走真实的 <see cref="LocalEventBus"/> 派发</b>而不是直接调处理器方法：
/// 直接调用的单元测试对本缺陷完全免疫（方法本身没毛病，坏的是派发选错了接口）。
/// </para>
/// </remarks>
public class MultiEventHandlerDispatchTests
{
    #region 测试事件

    public class AlphaEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    public class BetaEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    public class BaseNotification : EventBase { }

    public class DerivedNotification : BaseNotification { }

    #endregion

    #region 测试处理器

    /// <summary>同时处理两个互不相关事件的处理器（本缺陷的最小复现形态）。</summary>
    public class TwoEventHandler : IEventHandler<AlphaEvent>, IEventHandler<BetaEvent>
    {
        public AlphaEvent? ReceivedAlpha { get; private set; }
        public BetaEvent? ReceivedBeta { get; private set; }

        public Task HandleAsync(AlphaEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedAlpha = @event;
            return Task.CompletedTask;
        }

        public Task HandleAsync(BetaEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedBeta = @event;
            return Task.CompletedTask;
        }
    }

    /// <summary>声明顺序与 <see cref="TwoEventHandler"/> 相反，用来排除"恰好第一个是对的"这种假绿。</summary>
    public class TwoEventHandlerReversed : IEventHandler<BetaEvent>, IEventHandler<AlphaEvent>
    {
        public AlphaEvent? ReceivedAlpha { get; private set; }
        public BetaEvent? ReceivedBeta { get; private set; }

        public Task HandleAsync(AlphaEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedAlpha = @event;
            return Task.CompletedTask;
        }

        public Task HandleAsync(BetaEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedBeta = @event;
            return Task.CompletedTask;
        }
    }

    /// <summary>只对 Alpha 有条件，对 Beta 无条件：CanHandle 不得越界去闸 Beta。</summary>
    public class ConditionalOnAlphaOnlyHandler : IConditionalEventHandler<AlphaEvent>, IEventHandler<BetaEvent>
    {
        public bool AllowAlpha { get; set; }
        public int AlphaCalls { get; private set; }
        public int BetaCalls { get; private set; }
        public int CanHandleCalls { get; private set; }

        public bool CanHandle(AlphaEvent @event)
        {
            CanHandleCalls++;
            return AllowAlpha;
        }

        public Task HandleAsync(AlphaEvent @event, CancellationToken cancellationToken = default)
        {
            AlphaCalls++;
            return Task.CompletedTask;
        }

        public Task HandleAsync(BetaEvent @event, CancellationToken cancellationToken = default)
        {
            BetaCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>两个事件各自带条件：每个事件必须走自己的那个 CanHandle 重载。</summary>
    public class TwoConditionalHandler : IConditionalEventHandler<AlphaEvent>, IConditionalEventHandler<BetaEvent>
    {
        public bool AllowAlpha { get; set; }
        public bool AllowBeta { get; set; }
        public int AlphaCalls { get; private set; }
        public int BetaCalls { get; private set; }

        public bool CanHandle(AlphaEvent @event) => AllowAlpha;

        public bool CanHandle(BetaEvent @event) => AllowBeta;

        public Task HandleAsync(AlphaEvent @event, CancellationToken cancellationToken = default)
        {
            AlphaCalls++;
            return Task.CompletedTask;
        }

        public Task HandleAsync(BetaEvent @event, CancellationToken cancellationToken = default)
        {
            BetaCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>只处理基类事件：派生事件应经继承匹配落到这里。</summary>
    public class BaseOnlyHandler : IEventHandler<BaseNotification>
    {
        public BaseNotification? Received { get; private set; }

        public Task HandleAsync(BaseNotification @event, CancellationToken cancellationToken = default)
        {
            Received = @event;
            return Task.CompletedTask;
        }
    }

    /// <summary>基类与派生类都处理：派发派生事件时必须选<b>更具体</b>的那个重载。</summary>
    public class BaseAndDerivedHandler : IEventHandler<BaseNotification>, IEventHandler<DerivedNotification>
    {
        public int BaseCalls { get; private set; }
        public int DerivedCalls { get; private set; }

        public Task HandleAsync(BaseNotification @event, CancellationToken cancellationToken = default)
        {
            BaseCalls++;
            return Task.CompletedTask;
        }

        public Task HandleAsync(DerivedNotification @event, CancellationToken cancellationToken = default)
        {
            DerivedCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 供推断式注册用例：实例由容器创建（且两个事件接口各自一个实例），
    /// 故用静态计数观察"两个事件都真的派发到了"。
    /// </summary>
    public class CountingTwoEventHandler : IEventHandler<AlphaEvent>, IEventHandler<BetaEvent>
    {
        private static int _alphaCalls;
        private static int _betaCalls;

        public static int AlphaCalls => _alphaCalls;
        public static int BetaCalls => _betaCalls;

        public static void Reset()
        {
            Interlocked.Exchange(ref _alphaCalls, 0);
            Interlocked.Exchange(ref _betaCalls, 0);
        }

        public Task HandleAsync(AlphaEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _alphaCalls);
            return Task.CompletedTask;
        }

        public Task HandleAsync(BetaEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _betaCalls);
            return Task.CompletedTask;
        }
    }

    #endregion

    private static LocalEventBus CreateBus(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        register(services);

        var provider = services.BuildServiceProvider();
        return new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>());
    }

    [Fact]
    public async Task Publish_TwoEventHandler_RunsBothHandleMethods()
    {
        var handler = new TwoEventHandler();
        var bus = CreateBus(s =>
        {
            s.AddScoped<IEventHandler<AlphaEvent>>(_ => handler);
            s.AddScoped<IEventHandler<BetaEvent>>(_ => handler);
        });

        await bus.PublishAsync(new AlphaEvent { Payload = "a" });
        await bus.PublishAsync(new BetaEvent { Payload = "b" });

        Assert.NotNull(handler.ReceivedAlpha);
        Assert.Equal("a", handler.ReceivedAlpha!.Payload);
        Assert.NotNull(handler.ReceivedBeta);
        Assert.Equal("b", handler.ReceivedBeta!.Payload);
    }

    /// <summary>
    /// 与上一条同构，只是接口声明顺序相反。缺陷期两条必有一条红：
    /// 反射先返回哪个接口决定了哪个事件能派发成功。
    /// </summary>
    [Fact]
    public async Task Publish_TwoEventHandlerWithReversedInterfaceOrder_RunsBothHandleMethods()
    {
        var handler = new TwoEventHandlerReversed();
        var bus = CreateBus(s =>
        {
            s.AddScoped<IEventHandler<AlphaEvent>>(_ => handler);
            s.AddScoped<IEventHandler<BetaEvent>>(_ => handler);
        });

        await bus.PublishAsync(new AlphaEvent { Payload = "a" });
        await bus.PublishAsync(new BetaEvent { Payload = "b" });

        Assert.NotNull(handler.ReceivedAlpha);
        Assert.NotNull(handler.ReceivedBeta);
    }

    [Fact]
    public async Task Publish_ConditionalOnOneEvent_DoesNotGateTheOtherEvent()
    {
        var handler = new ConditionalOnAlphaOnlyHandler { AllowAlpha = false };
        var bus = CreateBus(s =>
        {
            s.AddScoped<IEventHandler<AlphaEvent>>(_ => handler);
            s.AddScoped<IEventHandler<BetaEvent>>(_ => handler);
        });

        await bus.PublishAsync(new AlphaEvent());
        await bus.PublishAsync(new BetaEvent());

        // Alpha 被自己的 CanHandle 拦下
        Assert.Equal(0, handler.AlphaCalls);
        Assert.Equal(1, handler.CanHandleCalls);

        // Beta 没有条件接口，不该被 Alpha 的 CanHandle 闸住，更不该拿 Beta 去调它
        Assert.Equal(1, handler.BetaCalls);
        Assert.Equal(1, handler.CanHandleCalls);
    }

    [Fact]
    public async Task Publish_TwoConditionalHandler_UsesTheMatchingCanHandleOverload()
    {
        var handler = new TwoConditionalHandler { AllowAlpha = false, AllowBeta = true };
        var bus = CreateBus(s =>
        {
            s.AddScoped<IEventHandler<AlphaEvent>>(_ => handler);
            s.AddScoped<IEventHandler<BetaEvent>>(_ => handler);
        });

        await bus.PublishAsync(new AlphaEvent());
        await bus.PublishAsync(new BetaEvent());

        Assert.Equal(0, handler.AlphaCalls);
        Assert.Equal(1, handler.BetaCalls);
    }

    /// <summary>基类事件处理器接住派生事件——修复不得破坏既有的事件继承派发。</summary>
    [Fact]
    public async Task Publish_DerivedEvent_StillReachesBaseOnlyHandler()
    {
        var handler = new BaseOnlyHandler();
        var bus = CreateBus(s => s.AddScoped<IEventHandler<BaseNotification>>(_ => handler));

        await bus.PublishAsync(new DerivedNotification());

        Assert.NotNull(handler.Received);
        Assert.IsType<DerivedNotification>(handler.Received);
    }

    /// <summary>基类与派生类都实现时，派生事件必须落到更具体的那个重载。</summary>
    [Fact]
    public async Task Publish_DerivedEvent_PrefersTheMostDerivedHandlerInterface()
    {
        var handler = new BaseAndDerivedHandler();
        var bus = CreateBus(s =>
        {
            s.AddScoped<IEventHandler<BaseNotification>>(_ => handler);
            s.AddScoped<IEventHandler<DerivedNotification>>(_ => handler);
        });

        await bus.PublishAsync(new DerivedNotification());

        Assert.Equal(1, handler.DerivedCalls);
        Assert.Equal(0, handler.BaseCalls);
    }

    /// <summary>基类事件本身仍然只能落到基类重载。</summary>
    [Fact]
    public async Task Publish_BaseEvent_UsesTheBaseHandlerInterface()
    {
        var handler = new BaseAndDerivedHandler();
        var bus = CreateBus(s =>
        {
            s.AddScoped<IEventHandler<BaseNotification>>(_ => handler);
            s.AddScoped<IEventHandler<DerivedNotification>>(_ => handler);
        });

        await bus.PublishAsync(new BaseNotification());

        Assert.Equal(1, handler.BaseCalls);
        Assert.Equal(0, handler.DerivedCalls);
    }

    /// <summary>
    /// 委托缓存的存在理由：每个（处理器, 事件）组合只编译一次，不是每次派发都编译。
    /// 键从 handlerType 扩到 (handlerType, eventType) 是修复的一部分，
    /// 这条守住"扩键没有把缓存变成每次新建"。
    /// </summary>
    [Fact]
    public void GetMetadata_IsCachedPerHandlerAndEventTypePair()
    {
        var alpha1 = EventHandlerInvoker.GetMetadata(typeof(TwoEventHandler), typeof(AlphaEvent));
        var alpha2 = EventHandlerInvoker.GetMetadata(typeof(TwoEventHandler), typeof(AlphaEvent));
        var beta = EventHandlerInvoker.GetMetadata(typeof(TwoEventHandler), typeof(BetaEvent));

        // 同一组合：同一个实例（含同一个已编译委托），证明没有每次重编译
        Assert.Same(alpha1, alpha2);
        Assert.Same(alpha1.HandleDelegate, alpha2.HandleDelegate);

        // 不同事件：各自的元数据，且各自绑定到正确的接口
        Assert.NotSame(alpha1, beta);
        Assert.Equal(typeof(IEventHandler<AlphaEvent>), alpha1.HandlerInterface);
        Assert.Equal(typeof(IEventHandler<BetaEvent>), beta.HandlerInterface);
    }

    /// <summary>
    /// 推断式注册 <c>AddEventHandler&lt;THandler&gt;()</c> 必须订阅处理器声明的<b>每一个</b>事件，
    /// 否则第二个事件根本到不了派发环节（同一"取第一个接口"根因的注册侧形态）。
    /// </summary>
    [Fact]
    public async Task AddEventHandlerByInference_SubscribesEveryDeclaredEvent()
    {
        CountingTwoEventHandler.Reset();

        var bus = CreateBus(s => s.AddEventHandler<CountingTwoEventHandler>());

        await bus.PublishAsync(new AlphaEvent { Payload = "a" });
        await bus.PublishAsync(new BetaEvent { Payload = "b" });

        Assert.Equal(1, CountingTwoEventHandler.AlphaCalls);
        Assert.Equal(1, CountingTwoEventHandler.BetaCalls);
    }
}
