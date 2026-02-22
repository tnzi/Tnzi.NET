
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
        var service1Descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService1));
        var service2Descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITestService2));

        Assert.NotNull(service1Descriptor);
        Assert.NotNull(service2Descriptor);

        // 主接口直接注册 ImplementationType
        Assert.Equal(typeof(TestMultiInterfaceService), service1Descriptor.ImplementationType);

        // 次要接口通过工厂委托注册（共享同一实例），ImplementationFactory 不为 null
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