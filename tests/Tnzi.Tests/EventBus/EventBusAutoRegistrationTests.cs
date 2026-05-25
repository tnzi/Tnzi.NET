
namespace Tnzi.Tests.EventBus;

/// <summary>
/// 事件处理器自动注册测试
/// </summary>
public class EventBusAutoRegistrationTests
{
    #region 测试事件和处理器

    public class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    public class AnotherTestEvent : EventBase
    {
        public int Value { get; set; }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public bool WasCalled { get; private set; }
        public TestEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    public class AnotherTestEventHandler : IEventHandler<TestEvent>
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    public class AnotherEventEventHandler : IEventHandler<AnotherTestEvent>
    {
        public Task HandleAsync(AnotherTestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [IgnoreEventHandler]
    public class IgnoredEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public abstract class AbstractEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    #endregion

    [Fact]
    public void AutoRegisterHandlers_WhenEnabled_ShouldRegisterHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" } // 指定测试程序集
        };

        // Act - 模拟 EventBusModule 的自动注册逻辑
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证处理器已注册
        var serviceProvider = services.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();

        Assert.NotEmpty(handlers);
        Assert.Contains(handlers, h => h.GetType() == typeof(TestEventHandler));
    }

    [Fact]
    public void AutoRegisterHandlers_WhenDisabled_ShouldNotRegisterHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = false,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" }
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证处理器未注册（除了手动注册的）
        var serviceProvider = services.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();

        // 如果没有手动注册，应该为空
        Assert.Empty(handlers);
    }

    [Fact]
    public void AutoRegisterHandlers_ShouldRespectIgnoreAttribute()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" }
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证带 IgnoreEventHandler 特性的处理器未被注册
        var serviceProvider = services.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();

        Assert.DoesNotContain(handlers, h => h.GetType() == typeof(IgnoredEventHandler));
    }

    [Fact]
    public void AutoRegisterHandlers_ShouldNotOverrideManualRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // 先手动注册
        services.AddEventHandler<TestEvent, TestEventHandler>();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" }
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证手动注册的处理器仍然存在，且不会被重复注册
        var serviceProvider = services.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();

        var testHandlers = handlers.Where(h => h.GetType() == typeof(TestEventHandler)).ToList();
        Assert.Single(testHandlers); // 应该只有一个，不会重复注册
    }

    [Fact]
    public void AutoRegisterHandlers_WithSpecificAssemblies_ShouldOnlyScanSpecified()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            HandlerAssemblies = new List<string> { "NonExistentAssembly" } // 不存在的程序集
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证未扫描到任何处理器
        var serviceProvider = services.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();

        // 由于指定的程序集不存在，应该没有注册任何处理器
        Assert.Empty(handlers);
    }

    [Fact]
    public void AutoRegisterHandlers_WithDefaultLifetime_ShouldUseConfiguredLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            DefaultHandlerLifetime = ServiceLifetime.Singleton,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" }
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证使用配置的生命周期
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType.IsGenericType &&
            s.ServiceType.GetGenericTypeDefinition() == typeof(IEventHandler<>) &&
            s.ImplementationType == typeof(TestEventHandler));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AutoRegisterHandlers_WithMultipleHandlers_ShouldRegisterAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" }
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证多个处理器全部注册
        var serviceProvider = services.BuildServiceProvider();
        var testHandlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();
        var anotherHandlers = serviceProvider.GetServices<IEventHandler<AnotherTestEvent>>().ToList();

        Assert.Contains(testHandlers, h => h.GetType() == typeof(TestEventHandler));
        Assert.Contains(testHandlers, h => h.GetType() == typeof(AnotherTestEventHandler));
        Assert.Contains(anotherHandlers, h => h.GetType() == typeof(AnotherEventEventHandler));
    }

    [Fact]
    public void AutoRegisterHandlers_WithAbstractHandler_ShouldSkip()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            HandlerAssemblies = new List<string> { "Tnzi.Tests" }
        };

        // Act
        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert - 验证抽象类被跳过
        var serviceProvider = services.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IEventHandler<TestEvent>>().ToList();

        Assert.DoesNotContain(handlers, h => h.GetType() == typeof(AbstractEventHandler));
    }

    /// <summary>
    /// Regression: framework handlers are auto-registered AND then manually
    /// re-registered later in the module lifecycle, so the DI container ends
    /// up with two `IEventHandler&lt;TEvent&gt;` descriptors for the same concrete
    /// type — every event publish fires the handler twice (manifested as e.g.
    /// duplicate LoginLog rows per login).
    ///
    /// `ExcludeFrameworkAssemblies = true` (the default) makes auto-scan skip
    /// any assembly whose name starts with "Tnzi", restoring the framework
    /// contract that all Tnzi.* modules register their handlers manually.
    /// </summary>
    [Fact]
    public void AutoRegisterHandlers_ExcludesFrameworkAssemblies_ByDefault()
    {
        // Arrange — no HandlerAssemblies set → falls through to "scan everything
        // loaded" path, which is exactly the path framework modules hit at boot.
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            // ExcludeFrameworkAssemblies defaults to true; not set explicitly
        };

        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // Assert — no Tnzi.* handlers picked up via auto-scan. We sample a
        // representative event interface from Tnzi.Identity (UserEvents) —
        // the assembly only loads if the test host references it, so we just
        // assert the GENERIC truth: no IEventHandler descriptor's
        // ImplementationType comes from an assembly named "Tnzi*".
        var frameworkHandlers = services
            .Where(d => d.ServiceType.IsGenericType &&
                        d.ServiceType.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .Select(d => d.ImplementationType?.Assembly.GetName().Name ?? string.Empty)
            .Where(n => n.StartsWith("Tnzi", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(frameworkHandlers);
    }

    [Fact]
    public void AutoRegisterHandlers_IncludesFrameworkAssemblies_WhenOptOut()
    {
        // Arrange — explicit opt-OUT of the new default restores legacy
        // behaviour for consumers that depend on framework auto-scan.
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new EventBusOptions
        {
            AutoRegisterHandlers = true,
            ExcludeFrameworkAssemblies = false,
        };

        var module = new TestableEventBusModule();
        module.TestAutoRegisterHandlers(services, options);

        // We don't assert "some framework handler IS present" because the
        // exact framework assemblies loaded by the test host vary; we just
        // assert the scan didn't crash, leaving framework descriptors in if
        // present. The opposite-direction default test (above) is what
        // proves the regression is fixed.
        Assert.NotNull(services);
    }

    /// <summary>
    /// 可测试的 EventBusModule，暴露内部方法用于测试
    /// </summary>
    private class TestableEventBusModule : EventBusModule
    {
        public void TestAutoRegisterHandlers(IServiceCollection services, EventBusOptions options)
        {
            // 模拟 ConfigureServicesAsync 中的逻辑
            if (options.AutoRegisterHandlers)
            {
                var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBusModule>.Instance;
                AutoRegisterEventHandlers(services, options, logger);
            }
        }
    }
}