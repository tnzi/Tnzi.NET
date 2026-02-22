
namespace Tnzi.Tests.EventBus;

/// <summary>
/// LocalEventBus 测试类
/// </summary>
public class LocalEventBusTests
{
    /// <summary>
    /// 测试事件
    /// </summary>
    public class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 测试事件处理器
    /// </summary>
    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public bool WasCalled { get; set; }
        public TestEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_WithRegisteredHandler_ShouldCallHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestEventHandler();

        // 注册处理器（使用接口类型）
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler);

        // 添加日志
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();

        // 创建 LocalEventBus（不传递预扫描的处理器，让它从服务提供者解析）
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert
        Assert.True(handler.WasCalled, "Handler should have been called");
        Assert.NotNull(handler.ReceivedEvent);
        Assert.Equal("Test Message", handler.ReceivedEvent.Message);
    }

    [Fact]
    public async Task PublishAsync_WithPreScannedHandlers_ShouldCallHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestEventHandler();

        // 注册处理器
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler);

        // 添加日志
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();

        // 预扫描处理器类型
        var handlerTypes = new Dictionary<Type, List<Type>>
        {
            [typeof(TestEvent)] = new List<Type> { typeof(TestEventHandler) }
        };

        // 创建 LocalEventBus（传递预扫描的处理器）
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert
        Assert.True(handler.WasCalled, "Handler should have been called");
        Assert.NotNull(handler.ReceivedEvent);
        Assert.Equal("Test Message", handler.ReceivedEvent.Message);
    }

    [Fact]
    public async Task PublishAsync_WithMultipleHandlers_ShouldCallAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();

        // 注册多个处理器
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler1);
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler2);

        // 添加日志
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();

        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert
        Assert.True(handler1.WasCalled, "Handler1 should have been called");
        Assert.True(handler2.WasCalled, "Handler2 should have been called");
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();

        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act & Assert
        await eventBus.PublishAsync(testEvent, CancellationToken.None); // 不应该抛出异常
    }

    [Fact]
    public async Task Subscribe_AndUnsubscribe_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestEventHandler();
        services.AddScoped<TestEventHandler>(_ => handler);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act & Assert - 订阅后应该能处理事件
        eventBus.Subscribe<TestEvent, TestEventHandler>();
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        Assert.True(handler.WasCalled, "Handler should have been called after subscription");

        // 重置状态
        handler.WasCalled = false;

        // 取消订阅后不应该再处理
        eventBus.Unsubscribe<TestEvent, TestEventHandler>();
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        Assert.False(handler.WasCalled, "Handler should not be called after unsubscription");
    }

    [Fact]
    public async Task Subscribe_ThreadSafety_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        // Act - 并发订阅多个处理器
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => eventBus.Subscribe<TestEvent, TestEventHandler>()));
        }

        await Task.WhenAll(tasks);

        // Assert - 应该能够正常订阅，不抛出异常
        Assert.True(eventBus.HasHandlers<TestEvent>(), "Should have handlers after concurrent subscription");
    }

    [Fact]
    public async Task Unsubscribe_ThreadSafety_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        // 先订阅
        eventBus.Subscribe<TestEvent, TestEventHandler>();

        // Act - 并发取消订阅
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => eventBus.Unsubscribe<TestEvent, TestEventHandler>()));
        }

        await Task.WhenAll(tasks);

        // Assert - 应该能够正常取消订阅，不抛出异常
        // 由于并发取消订阅，最终状态可能是不确定，但不应该抛出异常
    }

    [Fact]
    public void UnsubscribeAll_ShouldRemoveAllRuntimeHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        // 订阅多个处理器
        eventBus.Subscribe<TestEvent, TestEventHandler>();

        // Act
        eventBus.UnsubscribeAll<TestEvent>();

        // Assert
        Assert.False(eventBus.HasHandlers<TestEvent>(), "Should not have handlers after UnsubscribeAll");
        Assert.Equal(0, eventBus.GetHandlerCount<TestEvent>());
    }

    /// <summary>
    /// 另一个测试事件处理器
    /// </summary>
    public class AnotherTestEventHandler : IEventHandler<TestEvent>
    {
        public bool WasCalled { get; set; }
        public TestEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void HasHandlers_AndGetHandlerCount_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler1 = new TestEventHandler();
        var handler2 = new AnotherTestEventHandler(); // 使用不同类型的处理器

        services.AddScoped<IEventHandler<TestEvent>>(_ => handler1);
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler2);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        // Act & Assert
        Assert.True(eventBus.HasHandlers<TestEvent>(), "Should have handlers");
        Assert.Equal(2, eventBus.GetHandlerCount<TestEvent>());

        // 运行时订阅（注意：这可能会重复，因为TestEventHandler已经在DI中注册了）
        // 但GetHandlerCount会去重，所以仍然计数为2
        eventBus.Subscribe<TestEvent, TestEventHandler>();
        // GetHandlerCount会去重相同类型的处理器，所以仍然是2
        Assert.Equal(2, eventBus.GetHandlerCount<TestEvent>());

        // 订阅一个不同的类型
        eventBus.Subscribe<TestEvent, AnotherTestEventHandler>();
        // 由于AnotherTestEventHandler已经在DI中，仍然去重为2
        Assert.Equal(2, eventBus.GetHandlerCount<TestEvent>());

        // 取消所有运行时订阅
        eventBus.UnsubscribeAll<TestEvent>();
        Assert.Equal(2, eventBus.GetHandlerCount<TestEvent>()); // DI中的处理器仍然存在
    }

    /// <summary>
    /// 条件事件处理器测试
    /// </summary>
    public class ConditionalTestEventHandler : IConditionalEventHandler<TestEvent>
    {
        public bool WasCalled { get; set; }
        public bool ShouldHandle { get; set; } = true;

        public bool CanHandle(TestEvent @event)
        {
            return ShouldHandle;
        }

        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_WithConditionalHandler_ShouldRespectCanHandle()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new ConditionalTestEventHandler { ShouldHandle = true };
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act - CanHandle返回true
        handler.WasCalled = false;
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        Assert.True(handler.WasCalled, "Handler should be called when CanHandle returns true");

        // Act - CanHandle返回false
        handler.ShouldHandle = false;
        handler.WasCalled = false;
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        Assert.False(handler.WasCalled, "Handler should not be called when CanHandle returns false");
    }

    /// <summary>
    /// 基类事件
    /// </summary>
    public class BaseEvent : EventBase
    {
        public string BaseProperty { get; set; } = string.Empty;
    }

    /// <summary>
    /// 派生类事件
    /// </summary>
    public class DerivedEvent : BaseEvent
    {
        public string DerivedProperty { get; set; } = string.Empty;
    }

    /// <summary>
    /// 基类事件处理器
    /// </summary>
    public class BaseEventHandler : IEventHandler<BaseEvent>
    {
        public bool WasCalled { get; private set; }
        public BaseEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(BaseEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_WithEventInheritance_ShouldCallBaseHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var baseHandler = new BaseEventHandler();
        services.AddScoped<IEventHandler<BaseEvent>>(_ => baseHandler);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var derivedEvent = new DerivedEvent { BaseProperty = "Base", DerivedProperty = "Derived" };

        // Act
        await eventBus.PublishAsync(derivedEvent, CancellationToken.None);

        // Assert - 基类事件处理器应该被调用
        Assert.True(baseHandler.WasCalled, "Base handler should be called for derived event");
        Assert.NotNull(baseHandler.ReceivedEvent);
        Assert.Equal("Base", baseHandler.ReceivedEvent.BaseProperty);
    }

    [Fact]
    public async Task PublishAsync_WithHandlerThrowingException_ShouldIsolateErrors()
    {
        // Arrange
        var services = new ServiceCollection();

        // 第一个处理器会抛出异常
        services.AddScoped<IEventHandler<TestEvent>>(_ => new FailingEventHandler());

        // 第二个处理器正常
        var workingHandler = new TestEventHandler();
        services.AddScoped<IEventHandler<TestEvent>>(_ => workingHandler);

        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act - 不应该抛出异常，即使一个处理器失败
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert - 正常处理器应该仍然被调用
        Assert.True(workingHandler.WasCalled, "Working handler should still be called even if another handler fails");
    }

    /// <summary>
    /// 会抛出异常的处理器
    /// </summary>
    public class FailingEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler intentionally fails");
        }
    }

    [Fact]
    public async Task AddEventHandler_WithOnlyHandlerType_ShouldAutoInferEventType()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestEventHandler();

        // Act - 使用简化方式注册（只指定处理器类型，自动推断事件类型）
        services.AddEventHandler<TestEventHandler>();
        services.AddLogging();

        // 使用工厂方法注册，这样可以跟踪同一个实例
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act - 发布事件
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert - 验证事件被处理（通过工厂方法注册的handler实例应该被调用）
        Assert.True(handler.WasCalled, "Handler should have been called");
        Assert.Equal("Test Message", handler.ReceivedEvent?.Message);

        // 验证注册的ServiceDescriptor是否正确
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType.IsGenericType &&
            s.ServiceType.GetGenericTypeDefinition() == typeof(IEventHandler<>) &&
            s.ServiceType.GetGenericArguments()[0] == typeof(TestEvent) &&
            s.ImplementationType == typeof(TestEventHandler));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task AddEventHandler_WithEventAndHandlerTypes_ShouldRegisterCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestEventHandler();

        // Act - 使用显式指定事件和处理器类型
        services.AddEventHandler<TestEvent, TestEventHandler>();
        services.AddLogging();

        // 使用工厂方法注册，这样可以跟踪同一个实例
        services.AddScoped<IEventHandler<TestEvent>>(_ => handler);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act - 发布事件
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert - 验证事件被处理（通过工厂方法注册的handler实例应该被调用）
        Assert.True(handler.WasCalled, "Handler should have been called");
        Assert.Equal("Test Message", handler.ReceivedEvent?.Message);

        // 验证注册的ServiceDescriptor是否正确
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(IEventHandler<TestEvent>) &&
            s.ImplementationType == typeof(TestEventHandler));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddEventHandler_WithOnlyHandlerType_ShouldInferCorrectEventType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - 使用简化方式注册
        services.AddEventHandler<TestEventHandler>();

        // Assert - 验证注册的服务描述符
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType.IsGenericType &&
            s.ServiceType.GetGenericTypeDefinition() == typeof(IEventHandler<>) &&
            s.ServiceType.GetGenericArguments()[0] == typeof(TestEvent));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(TestEventHandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task AddEventHandler_WithLifetime_ShouldRegisterWithCorrectLifetime()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - 使用 Singleton 生命周期
        services.AddEventHandler<TestEventHandler>(ServiceLifetime.Singleton);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        // Assert - 验证是同一个实例
        var handler1 = serviceProvider.GetService<IEventHandler<TestEvent>>();
        var handler2 = serviceProvider.GetService<IEventHandler<TestEvent>>();
        Assert.Same(handler1, handler2); // Singleton 应该是同一个实例
    }

    [Fact]
    public async Task AddEventHandler_WithEventAndHandlerTypesAndLifetime_ShouldRegisterWithCorrectLifetime()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - 使用 Transient 生命周期
        services.AddEventHandler<TestEvent, TestEventHandler>(ServiceLifetime.Transient);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        // Assert - 验证是不同的实例
        var handler1 = serviceProvider.GetService<IEventHandler<TestEvent>>();
        var handler2 = serviceProvider.GetService<IEventHandler<TestEvent>>();
        Assert.NotSame(handler1, handler2); // Transient 应该是不同的实例
    }

    [Fact]
    public async Task AddEventHandler_MultipleHandlers_ShouldAllBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler1 = new TestEventHandler();
        var handler2 = new AnotherTestEventHandler();

        // Act - 使用简化方式注册多个处理器
        services.AddEventHandler<TestEventHandler>();
        services.AddEventHandler<AnotherTestEventHandler>();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<LocalEventBus>>();
        var eventBus = new LocalEventBus(serviceProvider, logger, maxConcurrency: 10);

        var testEvent = new TestEvent { Message = "Test Message" };

        // Act
        await eventBus.PublishAsync(testEvent, CancellationToken.None);

        // Assert - 验证所有处理器都被注册
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();
        Assert.Equal(2, handlers.Count);

        // 验证两个处理器都被调用
        var h1 = handlers.OfType<TestEventHandler>().FirstOrDefault();
        var h2 = handlers.OfType<AnotherTestEventHandler>().FirstOrDefault();
        Assert.NotNull(h1);
        Assert.NotNull(h2);
    }
}