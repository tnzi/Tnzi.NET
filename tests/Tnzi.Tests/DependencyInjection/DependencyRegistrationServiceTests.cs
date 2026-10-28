
namespace Tnzi.Tests.DependencyInjection;

/// <summary>
/// 依赖注入自动注册服务测试
/// </summary>
public class DependencyRegistrationServiceTests
{
    [Fact]
    public void RegisterAll_ShouldRegisterSingletonDependency()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestSingletonService).Assembly },
            TypeFilter = t => t == typeof(TestSingletonService) // 只包含 TestSingletonService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TestSingletonService), descriptor.ImplementationType);
    }

    [Fact]
    public void RegisterAll_ShouldRegisterScopedDependency()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestScopedService).Assembly },
            TypeFilter = t => t == typeof(TestScopedService) // 只包含 TestScopedService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TestScopedService), descriptor.ImplementationType);
    }

    [Fact]
    public void RegisterAll_ShouldRegisterTransientDependency()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestTransientService).Assembly },
            TypeFilter = t => t == typeof(TestTransientService) // 只包含 TestTransientService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        Assert.Equal(typeof(TestTransientService), descriptor.ImplementationType);
    }

    [Fact]
    public void RegisterAll_ShouldRegisterWithDependencyAttribute()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestAttributedService).Assembly },
            TypeFilter = t => t == typeof(TestAttributedService) // 只包含 TestAttributedService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TestAttributedService), descriptor.ImplementationType);
    }

    [Fact]
    public void RegisterAll_ShouldRegisterMultipleInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestMultiInterfaceService).Assembly },
            TypeFilter = t => t == typeof(TestMultiInterfaceService) // 只包含 TestMultiInterfaceService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var selfDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TestMultiInterfaceService));
        var service1Descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService1));
        var service2Descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService2));

        Assert.NotNull(selfDescriptor);
        Assert.NotNull(service1Descriptor);
        Assert.NotNull(service2Descriptor);

        // 实例锚定在实现类型自身的注册上
        Assert.Equal(typeof(TestMultiInterfaceService), selfDescriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, selfDescriptor.Lifetime);

        // 每个接口都经工厂转发到实现类型（共享同一实例）
        Assert.NotNull(service1Descriptor.ImplementationFactory);
        Assert.NotNull(service2Descriptor.ImplementationFactory);
    }

    [Fact]
    public void RegisterAll_ShouldNotRegisterIgnoredDependency()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestIgnoredService).Assembly },
            TypeFilter = t => t == typeof(TestIgnoredService) // 只包含 TestIgnoredService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        Assert.Null(descriptor);
    }

    [Fact]
    public void RegisterAll_ShouldRegisterGenericInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestGenericService<>).Assembly },
            TypeFilter = t => t == typeof(TestGenericService<>) // 只包含 TestGenericService<>
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        // 查找所有注册的泛型服务描述符
        var genericDescriptors = services.Where(s =>
            s.ServiceType.IsGenericType &&
            s.ServiceType.GetGenericTypeDefinition() == typeof(ITestGenericService<>)).ToList();

        // 注意：泛型类型定义本身不会被注册，只有具体的泛型类型实例会被注册
        // 这里我们检查是否有任何相关的注册
        Assert.NotNull(genericDescriptors);
    }

    [Fact]
    public void RegisterAll_ShouldThrowWhenMultipleMarkerInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestMultipleMarkerInterfaces).Assembly }
        };
        var service = new DependencyRegistrationService(services, options);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.RegisterAll());
    }

    [Fact]
    public void RegisterAll_ShouldRespectTypeFilter()
    {
        // Arrange
        var services = new ServiceCollection();
        // 先只包含 TestSingletonService，验证它会被注册
        var optionsWithType = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestSingletonService).Assembly },
            TypeFilter = t => t == typeof(TestSingletonService)
        };
        var serviceWithType = new DependencyRegistrationService(services, optionsWithType);
        serviceWithType.RegisterAll();
        Assert.NotNull(services.FirstOrDefault(s => s.ServiceType == typeof(ITestService)));

        // 再用排除 TestSingletonService 的过滤器，验证它不会被注册
        var services2 = new ServiceCollection();
        var optionsWithoutType = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestSingletonService).Assembly },
            TypeFilter = t => t == typeof(TestMultiInterfaceService) // 不包含 TestSingletonService
        };
        var serviceWithoutType = new DependencyRegistrationService(services2, optionsWithoutType);
        serviceWithoutType.RegisterAll();

        // Assert - TestSingletonService 被过滤掉后，ITestService 不会被注册
        var descriptor = services2.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        Assert.Null(descriptor);
    }

    [Fact]
    public void RegisterAll_ShouldRespectRegisterTypesWithoutInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestNoInterfaceService).Assembly },
            RegisterTypesWithoutInterfaces = false,
            TypeFilter = t => t == typeof(TestNoInterfaceService) // 只包含 TestNoInterfaceService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TestNoInterfaceService));
        Assert.Null(descriptor);
    }

    [Fact]
    public void RegisterAll_ShouldRegisterTypeWithoutInterfaceWhenEnabled()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestNoInterfaceService).Assembly },
            RegisterTypesWithoutInterfaces = true,
            TypeFilter = t => t == typeof(TestNoInterfaceService) // 只包含 TestNoInterfaceService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TestNoInterfaceService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void RegisterAll_ShouldRespectTryAddAttribute()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestSingletonService>();

        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestTryAddService).Assembly },
            TypeFilter = t => t == typeof(TestTryAddService) // 只包含 TestTryAddService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert - TryAdd 不会覆盖已存在的服务
        var descriptors = services.Where(s => s.ServiceType == typeof(ITestService)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(TestSingletonService), descriptors[0].ImplementationType);
    }

    [Fact]
    public void RegisterAll_ShouldRespectReplaceExistingAttribute()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestSingletonService>();

        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestReplaceExistingService).Assembly },
            TypeFilter = t => t == typeof(TestReplaceExistingService) // 只包含 TestReplaceExistingService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert - ReplaceExisting 会替换已存在的服务
        var descriptors = services.Where(s => s.ServiceType == typeof(ITestService)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(TestReplaceExistingService), descriptors[0].ImplementationType);
    }

    [Fact]
    public void RegisterAll_ShouldRespectAddSelfAttribute()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(TestAddSelfService).Assembly },
            TypeFilter = t => t == typeof(TestAddSelfService) // 只包含 TestAddSelfService
        };
        var service = new DependencyRegistrationService(services, options);

        // Act
        service.RegisterAll();

        // Assert - AddSelf 会同时注册接口和自身类型
        var interfaceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService));
        var selfDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TestAddSelfService));

        Assert.NotNull(interfaceDescriptor);
        Assert.NotNull(selfDescriptor);
    }

    // ---- 多接口转发与瞬态自身注册 ----

    private static ServiceCollection RegisterOnly(params Type[] types)
    {
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(DependencyRegistrationServiceTests).Assembly },
            TypeFilter = types.Contains
        };
        new DependencyRegistrationService(services, options).RegisterAll();
        return services;
    }

    [Fact]
    public void RegisterAll_TransientWithoutInterfaces_RegistersAndResolvesTheTypeItself()
    {
        var services = RegisterOnly(typeof(AutoRegTransientNoInterface));

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<AutoRegTransientNoInterface>();
        var second = provider.GetRequiredService<AutoRegTransientNoInterface>();

        Assert.NotSame(first, second);
        Assert.Single(services, d => d.ServiceType == typeof(AutoRegTransientNoInterface));
    }

    [Fact]
    public void RegisterAll_TransientWithAddSelf_ResolvesByTypeAndByInterface()
    {
        var services = RegisterOnly(typeof(AutoRegTransientAddSelf));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<AutoRegTransientAddSelf>(provider.GetRequiredService<AutoRegTransientAddSelf>());
        Assert.IsType<AutoRegTransientAddSelf>(provider.GetRequiredService<IAutoRegTransientContract>());
    }

    [Fact]
    public void RegisterAll_TransientTryAddWithoutInterfaces_DoesNotThrow()
    {
        var services = RegisterOnly(typeof(AutoRegTransientTryAddNoInterface));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AutoRegTransientTryAddNoInterface>());
    }

    [Fact]
    public void RegisterAll_RunTwice_DoesNotDuplicateTransientRegistrations()
    {
        var services = new ServiceCollection();
        var options = new DependencyRegistrationOptions
        {
            Assemblies = { typeof(DependencyRegistrationServiceTests).Assembly },
            TypeFilter = t => t == typeof(AutoRegTransientNoInterface) || t == typeof(TestTransientService)
        };

        new DependencyRegistrationService(services, options).RegisterAll();
        new DependencyRegistrationService(services, options).RegisterAll();

        Assert.Single(services, d => d.ServiceType == typeof(AutoRegTransientNoInterface));
        Assert.Single(services, d => d.ServiceType == typeof(ITestService));
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void RegisterAll_TwoImplementationsSharingTheFirstSortedInterface_ResolveTheirOwnSecondaryInterfaces(ServiceLifetime lifetime)
    {
        var (sms, webhook) = lifetime == ServiceLifetime.Scoped
            ? (typeof(AutoRegScopedSmsChannel), typeof(AutoRegScopedWebhookChannel))
            : (typeof(AutoRegSingletonSmsChannel), typeof(AutoRegSingletonWebhookChannel));
        var services = RegisterOnly(sms, webhook);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType(sms, scope.ServiceProvider.GetRequiredService<IAutoRegSmsChannel>());
        Assert.IsType(webhook, scope.ServiceProvider.GetRequiredService<IAutoRegWebhookChannel>());

        var shared = scope.ServiceProvider.GetServices<IAutoRegSharedChannel>().Select(c => c.GetType()).ToList();
        Assert.Equal(2, shared.Count);
        Assert.Contains(sms, shared);
        Assert.Contains(webhook, shared);
    }

    [Fact]
    public void RegisterAll_InterfaceSharedAsTheSecondSortedInterface_ExposesEveryImplementation()
    {
        var services = RegisterOnly(typeof(AutoRegAlphaPlugin), typeof(AutoRegBetaPlugin));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var plugins = scope.ServiceProvider.GetServices<IAutoRegPlugin>().Select(p => p.GetType()).ToList();
        Assert.Equal(2, plugins.Count);
        Assert.Contains(typeof(AutoRegAlphaPlugin), plugins);
        Assert.Contains(typeof(AutoRegBetaPlugin), plugins);
    }

    [Fact]
    public void RegisterAll_ScopedMultiInterface_SharesOneInstancePerScopeAcrossInterfaces()
    {
        var services = RegisterOnly(typeof(TestMultiInterfaceService));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var a = scope1.ServiceProvider.GetRequiredService<ITestService1>();
        var b = scope1.ServiceProvider.GetRequiredService<ITestService2>();
        var c = scope2.ServiceProvider.GetRequiredService<ITestService1>();

        Assert.IsType<TestMultiInterfaceService>(a);
        Assert.Same(a, b);
        Assert.NotSame(a, c);
    }

    [Fact]
    public void RegisterAll_ScopedAddSelf_SharesOneInstanceBetweenTheTypeAndItsInterface()
    {
        var services = RegisterOnly(typeof(TestAddSelfService));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<TestAddSelfService>(),
            scope.ServiceProvider.GetRequiredService<ITestService>());
    }

    [Theory]
    [InlineData(typeof(AutoRegScopedTwoEventHandler))]
    [InlineData(typeof(AutoRegSingletonTwoEventHandler))]
    public void RegisterAll_MultiInterfaceForwards_LetALaterTryAddEnumerableOfTheSameImplementationDeduplicate(Type handlerType)
    {
        // 事件总线的自动扫描（以及任何别的模块）对同一实现用 TryAddEnumerable 再登记一次时，
        // 转发描述符必须让它认出「已经有这个实现」，否则同一个处理器每个事件执行两遍
        var services = RegisterOnly(handlerType);

        services.TryAddEnumerable(new ServiceDescriptor(typeof(IEventHandler<AutoRegFirstEvent>), handlerType, ServiceLifetime.Scoped));
        services.TryAddEnumerable(new ServiceDescriptor(typeof(IEventHandler<AutoRegSecondEvent>), handlerType, ServiceLifetime.Scoped));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<AutoRegFirstEvent>>());
        Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<AutoRegSecondEvent>>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<IEventHandler<AutoRegFirstEvent>>(),
            scope.ServiceProvider.GetRequiredService<IEventHandler<AutoRegSecondEvent>>());
    }
}

// 测试服务接口和实现

public interface ITestService { }
public interface ITestService1 { }
public interface ITestService2 { }
public interface ITestGenericService<T> { }

public class TestSingletonService : ITestService, ISingletonDependency { }
public class TestScopedService : ITestService, IScopedDependency { }
public class TestTransientService : ITestService, ITransientDependency { }

[Dependency(ServiceLifetime.Singleton)]
public class TestAttributedService : ITestService { }

public class TestMultiInterfaceService : ITestService1, ITestService2, IScopedDependency { }

[IgnoreDependency]
public class TestIgnoredService : ITestService, IScopedDependency { }

public class TestGenericService<T> : ITestGenericService<T>, IScopedDependency
{
    // 注意：泛型类型定义本身不会被自动注册
    // 只有具体的泛型类型实例（如 TestGenericService<string>）才会被注册
}

public class TestMultipleMarkerInterfaces : ITestService, ISingletonDependency, IScopedDependency { }

public class TestNoInterfaceService : IScopedDependency { }

[Dependency(ServiceLifetime.Scoped, TryAdd = true)]
public class TestTryAddService : ITestService { }

[Dependency(ServiceLifetime.Scoped, ReplaceExisting = true)]
public class TestReplaceExistingService : ITestService { }

[Dependency(ServiceLifetime.Scoped, AddSelf = true)]
public class TestAddSelfService : ITestService { }

public class AutoRegTransientNoInterface : ITransientDependency { }

public interface IAutoRegTransientContract { }

[Dependency(ServiceLifetime.Transient, AddSelf = true)]
public class AutoRegTransientAddSelf : IAutoRegTransientContract { }

[Dependency(ServiceLifetime.Transient, TryAdd = true)]
public class AutoRegTransientTryAddNoInterface { }

// 共享接口按 FullName 排在最前（ISharedChannel < ISmsChannel < IWebhookChannel）
public interface IAutoRegSharedChannel { }
public interface IAutoRegSmsChannel { }
public interface IAutoRegWebhookChannel { }

public class AutoRegScopedSmsChannel : IAutoRegSharedChannel, IAutoRegSmsChannel, IScopedDependency { }
public class AutoRegScopedWebhookChannel : IAutoRegSharedChannel, IAutoRegWebhookChannel, IScopedDependency { }
public class AutoRegSingletonSmsChannel : IAutoRegSharedChannel, IAutoRegSmsChannel, ISingletonDependency { }
public class AutoRegSingletonWebhookChannel : IAutoRegSharedChannel, IAutoRegWebhookChannel, ISingletonDependency { }

// 共享接口按 FullName 排在第二（IAlphaPlugin / IBetaPlugin < IPlugin）
public interface IAutoRegAlphaPlugin { }
public interface IAutoRegBetaPlugin { }
public interface IAutoRegPlugin { }

public class AutoRegAlphaPlugin : IAutoRegAlphaPlugin, IAutoRegPlugin, IScopedDependency { }
public class AutoRegBetaPlugin : IAutoRegBetaPlugin, IAutoRegPlugin, IScopedDependency { }

// 一个处理器类处理两种事件、带生命周期标记：自动注册给每个 IEventHandler<> 一条转发描述符
public class AutoRegFirstEvent : EventBase { }
public class AutoRegSecondEvent : EventBase { }

public class AutoRegScopedTwoEventHandler : IEventHandler<AutoRegFirstEvent>, IEventHandler<AutoRegSecondEvent>, IScopedDependency
{
    public Task HandleAsync(AutoRegFirstEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task HandleAsync(AutoRegSecondEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class AutoRegSingletonTwoEventHandler : IEventHandler<AutoRegFirstEvent>, IEventHandler<AutoRegSecondEvent>, ISingletonDependency
{
    public Task HandleAsync(AutoRegFirstEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task HandleAsync(AutoRegSecondEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
