namespace Tnzi.Tests.EventBus;

/// <summary>
/// 运行时订阅（<see cref="IEventSubscriber.Subscribe{TEvent,THandler}"/>）的解析契约。
/// 此前订阅只按具体类型 <c>GetService(handlerType)</c> 解析，而框架唯一的注册助手 <c>AddEventHandler</c> 只注册接口，
/// 于是按文档写的「插件运行时挂上处理器」一次都不执行、无日志，<c>HasHandlers</c> / <c>GetHandlerCount</c> 却报告它在。
/// 现在：DI 接口路径 → 具体类型路径 → <c>ActivatorUtilities</c> 在当前作用域内构造；三条都失败才记 Warning，
/// 且计数只报解析得出的处理器。
/// </summary>
public class LocalEventBusRuntimeSubscriptionTests
{
    public class PluginEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>
    /// 记录处理器被调用的次数与释放情况；作为单例注册进 DI，处理器经构造函数拿到它
    /// </summary>
    public class CallRecorder
    {
        private int _calls;
        private int _disposals;
        private int _constructions;

        public int Calls => Volatile.Read(ref _calls);
        public int Disposals => Volatile.Read(ref _disposals);
        public int Constructions => Volatile.Read(ref _constructions);

        public void RecordCall() => Interlocked.Increment(ref _calls);
        public void RecordDisposal() => Interlocked.Increment(ref _disposals);
        public void RecordConstruction() => Interlocked.Increment(ref _constructions);
    }

    /// <summary>
    /// 有构造依赖、未以任何形态注册进 DI 的处理器 —— 文档里的插件场景
    /// </summary>
    public class PluginHandler : IEventHandler<PluginEvent>, IDisposable
    {
        private readonly CallRecorder _recorder;

        public PluginHandler(CallRecorder recorder)
        {
            _recorder = recorder;
            recorder.RecordConstruction();
        }

        public Task HandleAsync(PluginEvent @event, CancellationToken cancellationToken = default)
        {
            _recorder.RecordCall();
            return Task.CompletedTask;
        }

        public void Dispose() => _recorder.RecordDisposal();
    }

    [BackgroundEventHandler]
    public class BackgroundPluginHandler : IEventHandler<PluginEvent>
    {
        private readonly CallRecorder _recorder;

        public BackgroundPluginHandler(CallRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(PluginEvent @event, CancellationToken cancellationToken = default)
        {
            _recorder.RecordCall();
            return Task.CompletedTask;
        }
    }

    public interface IMissingDependency { }

    /// <summary>
    /// 构造依赖在容器里不存在：既解析不到也构造不出
    /// </summary>
    public class UnconstructibleHandler : IEventHandler<PluginEvent>
    {
        public UnconstructibleHandler(IMissingDependency dependency)
        {
            _ = dependency;
        }

        public Task HandleAsync(PluginEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (LocalEventBus Bus, CallRecorder Recorder, Mock<ILogger<LocalEventBus>> Logger) Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var recorder = new CallRecorder();
        services.AddSingleton(recorder);
        services.AddLogging();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<LocalEventBus>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return (new LocalEventBus(provider, logger.Object, maxConcurrency: 10), recorder, logger);
    }

    [Fact]
    public async Task Subscribe_HandlerNotRegisteredInDI_StillDispatched()
    {
        var (bus, recorder, _) = Build();
        bus.Subscribe<PluginEvent, PluginHandler>();

        await bus.PublishAsync(new PluginEvent { Payload = "p" });

        Assert.Equal(1, recorder.Calls);
        Assert.True(bus.HasHandlers<PluginEvent>());
        Assert.Equal(1, bus.GetHandlerCount<PluginEvent>());
    }

    [Fact]
    public async Task Subscribe_ActivatorCreatedHandler_IsDisposedAfterDispatch()
    {
        var (bus, recorder, _) = Build();
        bus.Subscribe<PluginEvent, PluginHandler>();

        await bus.PublishAsync(new PluginEvent());

        Assert.Equal(1, recorder.Calls);
        Assert.Equal(1, recorder.Disposals);
    }

    [Fact]
    public async Task Subscribe_HandlerRegisteredViaAddEventHandler_RunsExactlyOnce()
    {
        var (bus, recorder, _) = Build(s => s.AddEventHandler<PluginEvent, PluginHandler>());
        bus.Subscribe<PluginEvent, PluginHandler>();

        await bus.PublishAsync(new PluginEvent());

        Assert.Equal(1, recorder.Calls);
        Assert.Equal(1, bus.GetHandlerCount<PluginEvent>());
    }

    [Fact]
    public async Task Subscribe_HandlerRegisteredAsConcreteType_ResolvesFromContainer()
    {
        var (bus, recorder, _) = Build(s => s.AddScoped<PluginHandler>());
        bus.Subscribe<PluginEvent, PluginHandler>();

        await bus.PublishAsync(new PluginEvent());

        Assert.Equal(1, recorder.Calls);
        // 容器自己创建的实例由作用域释放，总线不得再释放一次
        Assert.Equal(1, recorder.Disposals);
    }

    [Fact]
    public async Task Subscribe_BackgroundHandlerNotRegisteredInDI_StillDispatched()
    {
        var (bus, recorder, _) = Build();
        bus.Subscribe<PluginEvent, BackgroundPluginHandler>();

        await bus.PublishAsync(new PluginEvent());
        await bus.DisposeAsync(); // 排水在飞的后台任务

        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task Subscribe_UnconstructibleHandler_LogsWarningAndDoesNotCount()
    {
        var (bus, recorder, logger) = Build();
        bus.Subscribe<PluginEvent, UnconstructibleHandler>();

        Assert.False(bus.HasHandlers<PluginEvent>());
        Assert.Equal(0, bus.GetHandlerCount<PluginEvent>());

        await bus.PublishAsync(new PluginEvent());

        Assert.Equal(0, recorder.Calls);
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(nameof(UnconstructibleHandler))),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public void GetHandlerCount_DoesNotConstructRuntimeSubscribedHandlers()
    {
        // 计数只回答「解析得出来吗」，不该为此把处理器真的构造一遍（构造函数的副作用：开连接、登记、日志）
        // 再同步阻塞地释放它；判可解析性看容器登记与构造参数，不实例化
        var (bus, recorder, _) = Build();
        bus.Subscribe<PluginEvent, PluginHandler>();

        Assert.True(bus.HasHandlers<PluginEvent>());
        Assert.Equal(1, bus.GetHandlerCount<PluginEvent>());

        Assert.Equal(0, recorder.Constructions);
        Assert.Equal(0, recorder.Disposals);
    }

    [Fact]
    public void GetHandlerCount_UnresolvableConstructorParameter_IsNotCounted()
    {
        var (bus, _, _) = Build();
        bus.Subscribe<PluginEvent, UnconstructibleHandler>();
        bus.Subscribe<PluginEvent, PluginHandler>();

        Assert.Equal(1, bus.GetHandlerCount<PluginEvent>());
    }

    [Fact]
    public async Task Unsubscribe_RuntimeOnlyHandler_StopsDispatch()
    {
        var (bus, recorder, _) = Build();
        bus.Subscribe<PluginEvent, PluginHandler>();
        await bus.PublishAsync(new PluginEvent());

        bus.Unsubscribe<PluginEvent, PluginHandler>();
        await bus.PublishAsync(new PluginEvent());

        Assert.Equal(1, recorder.Calls);
        Assert.False(bus.HasHandlers<PluginEvent>());
    }
}
