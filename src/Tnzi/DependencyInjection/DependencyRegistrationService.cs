
namespace Tnzi.DependencyInjection;

/// <summary>
/// 依赖注入自动注册服务
/// </summary>
public class DependencyRegistrationService
{
    private readonly IServiceCollection _services;
    private readonly DependencyRegistrationOptions _options;

    // 排除的接口类型
    private static readonly Type[] ExcludedInterfaces =
    {
        typeof(IDisposable),
        typeof(IAsyncDisposable),
        typeof(IServiceProvider),
        typeof(IServiceScopeFactory),
        typeof(ISingletonDependency),
        typeof(IScopedDependency),
        typeof(ITransientDependency),
        typeof(IApplicationService)  // ApplicationService 基类接口，不应作为服务接口注册
    };

    public DependencyRegistrationService(
        IServiceCollection services,
        DependencyRegistrationOptions options)
    {
        _services = Check.NotNull(services);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 注册所有符合条件的类型
    /// </summary>
    public void RegisterAll()
    {
        var dependencyTypes = FindDependencyTypes();

        foreach (var type in dependencyTypes)
        {
            RegisterType(type);
        }
    }

    /// <summary>
    /// 查找所有需要自动注册的依赖类型
    /// </summary>
    private List<Type> FindDependencyTypes()
    {
        var types = new List<Type>();

        // 扫描标记接口类型
        var singletonTypes = ScanTypes<ISingletonDependency>();
        var scopedTypes = ScanTypes<IScopedDependency>();
        var transientTypes = ScanTypes<ITransientDependency>();

        types.AddRange(singletonTypes);
        types.AddRange(scopedTypes);
        types.AddRange(transientTypes);

        // 扫描带 DependencyAttribute 的类型
        var attributedTypes = ScanAttributedTypes();
        types.AddRange(attributedTypes);

        // 去重
        return types.Distinct().ToList();
    }

    /// <summary>
    /// 扫描实现指定接口的类型
    /// </summary>
    private List<Type> ScanTypes<TInterface>()
    {
        var assemblies = GetTargetAssemblies();
        var types = new List<Type>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var matchingTypes = assembly.GetExportedTypes()
                    .Where(t => IsValidDependencyType(t) &&
                               typeof(TInterface).IsAssignableFrom(t) &&
                               !t.HasAttribute<IgnoreDependencyAttribute>());

                types.AddRange(matchingTypes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to scan assembly '{assembly.GetName().Name}' for type '{typeof(TInterface).Name}': {ex.Message}");
            }
        }

        return types;
    }

    /// <summary>
    /// 扫描带 DependencyAttribute 的类型
    /// </summary>
    private List<Type> ScanAttributedTypes()
    {
        var assemblies = GetTargetAssemblies();
        var types = new List<Type>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var matchingTypes = assembly.GetExportedTypes()
                    .Where(t => IsValidDependencyType(t) &&
                               t.HasAttribute<DependencyAttribute>() &&
                               !t.HasAttribute<IgnoreDependencyAttribute>());

                types.AddRange(matchingTypes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to scan assembly '{assembly.GetName().Name}' for DependencyAttribute types: {ex.Message}");
            }
        }

        return types;
    }

    /// <summary>
    /// 验证是否为有效的依赖类型
    /// </summary>
    private bool IsValidDependencyType(Type type)
    {
        // 必须是类且非抽象
        if (!type.IsClass || type.IsAbstract)
            return false;

        // 不能是泛型类型定义
        if (type.IsGenericTypeDefinition)
            return false;

        // 应用类型过滤器
        if (_options.TypeFilter != null && !_options.TypeFilter(type))
            return false;

        return true;
    }

    /// <summary>
    /// 获取目标程序集列表
    /// </summary>
    private Assembly[] GetTargetAssemblies()
    {
        if (_options.Assemblies.Count > 0)
            return _options.Assemblies.ToArray();

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a =>
            {
                var name = a.GetName().Name ?? "";
                // 跳过系统程序集
                return !name.StartsWith("System") &&
                       !name.StartsWith("Microsoft") &&
                       !name.StartsWith("netstandard");
            })
            .ToArray();
    }

    /// <summary>
    /// 检测类型是否来自框架程序集
    /// </summary>
    private static bool IsFrameworkAssembly(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? "";
        return assemblyName.StartsWith("Tnzi", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 注册单个类型
    /// </summary>
    private void RegisterType(Type implementationType)
    {
        // 应用类型过滤器（如果已配置）
        if (_options.TypeFilter != null && !_options.TypeFilter(implementationType))
            return;

        // 检测框架程序集类型
        if (IsFrameworkAssembly(implementationType))
        {
            var assemblyName = implementationType.Assembly.GetName().Name ?? "";
            var message = $"Type '{implementationType.FullName}' from framework assembly '{assemblyName}' is using automatic registration. " +
                         "Framework modules should use manual registration in ConfigureServicesAsync. " +
                         "Consider removing marker interfaces (IScopedDependency, ISingletonDependency, ITransientDependency) or DependencyAttribute.";

            if (_options.StrictFrameworkRegistration)
            {
                throw new InvalidOperationException(message);
            }
            else
            {
                // 输出警告但不阻止注册
                // 注意：这里无法使用 ILogger，因为此时服务还未完全注册
                // 可以通过 Console 输出或使用其他方式记录警告
                Debug.WriteLine($"WARNING: {message}");
            }
        }

        var lifetime = GetLifetime(implementationType);
        if (lifetime == null)
            return;

        var dependencyAttribute = implementationType.GetAttribute<DependencyAttribute>();
        var serviceTypes = GetServiceTypes(implementationType);

        // 如果没有接口且不允许注册无接口类型，跳过
        if (serviceTypes.Count == 0 && !_options.RegisterTypesWithoutInterfaces)
            return;

        // 没有接口时注册自身；有接口时按 AddSelf 决定是否额外注册自身
        var registerSelf = serviceTypes.Count == 0 || dependencyAttribute?.AddSelf == true;
        var orderedServiceTypes = serviceTypes
            .OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal)
            .ToList();
        var exposedCount = orderedServiceTypes.Count + (registerSelf ? 1 : 0);

        // 瞬态没有「同一实例」可言，只暴露一个服务时也无需共享：每个服务类型直接映射到实现类型
        if (lifetime.Value == ServiceLifetime.Transient || exposedCount == 1)
        {
            if (registerSelf)
            {
                RegisterService(implementationType, implementationType, lifetime.Value, dependencyAttribute);
            }

            foreach (var serviceType in orderedServiceTypes)
            {
                RegisterService(serviceType, implementationType, lifetime.Value, dependencyAttribute);
            }

            return;
        }

        // 单例 / 作用域且暴露多个服务：实例锚定在实现类型自身的注册上，每个接口都转发到它，
        // 同一作用域内各接口拿到同一实例。
        // ★ 锚点必须是实现类型，不能是「排第一的接口」：两个实现共享同一个首位接口时，
        // 按接口解析取的是最后注册的那个实现，次要接口会被转发到别的类型（解析即 InvalidCastException，
        // 或两类恰好互相实现对方接口时静默拿到错误实现）。
        var anchor = new ServiceDescriptor(implementationType, implementationType, lifetime.Value);
        if (registerSelf)
        {
            AddServiceDescriptor(anchor, dependencyAttribute);
        }
        else
        {
            _services.TryAdd(anchor);
        }

        var forwardFactory = CreateForwardFactory(implementationType);
        foreach (var serviceType in orderedServiceTypes)
        {
            var forward = new ServiceDescriptor(serviceType, forwardFactory, lifetime.Value);
            AddServiceDescriptor(forward, dependencyAttribute);
        }
    }

    private static readonly MethodInfo CreateTypedForwardMethod = typeof(DependencyRegistrationService)
        .GetMethod(nameof(CreateTypedForward), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// 转发到实现类型自身注册的工厂，委托的返回类型就是实现类型。
    /// </summary>
    /// <remarks>
    /// ★ 委托类型是 <c>Func&lt;IServiceProvider, 实现类型&gt;</c> 而不是 <c>Func&lt;IServiceProvider, object&gt;</c>：
    /// 容器对工厂描述符的「实现类型」就从委托类型的返回值读出来，<c>TryAddEnumerable</c> 靠它判断同一实现是否已登记。
    /// 写成 <c>object</c> 时，事件总线的自动扫描（或任何模块）对同一实现再 <c>TryAddEnumerable</c> 一次会认不出、
    /// 多加一条：一个同时处理两种事件、带生命周期标记的处理器类，每个事件执行两遍。
    /// </remarks>
    private static Func<IServiceProvider, object> CreateForwardFactory(Type implementationType)
        => (Func<IServiceProvider, object>)CreateTypedForwardMethod.MakeGenericMethod(implementationType).Invoke(null, null)!;

    private static Func<IServiceProvider, TImplementation> CreateTypedForward<TImplementation>()
        where TImplementation : class
        => provider => provider.GetRequiredService<TImplementation>();

    /// <summary>
    /// 获取服务生命周期
    /// </summary>
    private ServiceLifetime? GetLifetime(Type type)
    {
        // 优先检查 DependencyAttribute
        var attribute = type.GetAttribute<DependencyAttribute>();
        if (attribute != null)
            return attribute.Lifetime;

        // 检查标记接口（检测多个标记接口冲突）
        var hasSingleton = typeof(ISingletonDependency).IsAssignableFrom(type);
        var hasScoped = typeof(IScopedDependency).IsAssignableFrom(type);
        var hasTransient = typeof(ITransientDependency).IsAssignableFrom(type);

        var count = (hasSingleton ? 1 : 0) + (hasScoped ? 1 : 0) + (hasTransient ? 1 : 0);
        if (count > 1)
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' implements multiple dependency marker interfaces. " +
                "A type should implement only one of ISingletonDependency, IScopedDependency, or ITransientDependency. " +
                "Use DependencyAttribute for more control.");
        }

        if (hasSingleton)
            return ServiceLifetime.Singleton;

        if (hasScoped)
            return ServiceLifetime.Scoped;

        if (hasTransient)
            return ServiceLifetime.Transient;

        return null;
    }

    /// <summary>
    /// 获取服务类型列表（实现的接口）
    /// </summary>
    private List<Type> GetServiceTypes(Type implementationType)
    {
        var interfaces = implementationType.GetInterfaces()
            .Where(i => !ExcludedInterfaces.Contains(i) &&
                       !i.HasAttribute<IgnoreDependencyAttribute>())
            .ToList();

        // 注意：DI容器需要具体的泛型类型实例（如 IRepository<User, Guid>），
        // 不能注册泛型类型定义（如 IRepository<,>），所以直接使用原始接口类型

        return interfaces.Distinct().ToList();
    }

    /// <summary>
    /// 注册服务
    /// </summary>
    private void RegisterService(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime,
        DependencyAttribute? attribute)
    {
        var descriptor = new ServiceDescriptor(serviceType, implementationType, lifetime);
        AddServiceDescriptor(descriptor, attribute);
    }

    /// <summary>
    /// 添加服务描述符
    /// </summary>
    private void AddServiceDescriptor(ServiceDescriptor descriptor, DependencyAttribute? attribute)
    {
        if (attribute?.ReplaceExisting == true)
        {
            // 移除所有已注册的实现，然后添加新实现
            _services.RemoveAll(descriptor.ServiceType);
            _services.Add(descriptor);
        }
        else if (descriptor.ServiceType == descriptor.ImplementationType)
        {
            // 自身注册只可能有一个实现，多条描述符没有意义；
            // 而 TryAddEnumerable 对「实现类型 = 服务类型」直接抛 ArgumentException（瞬态无接口类型曾因此启动即崩）
            _services.TryAdd(descriptor);
        }
        else if (attribute?.TryAdd == true)
        {
            // TryAdd 标记：使用 TryAdd 或 TryAddEnumerable
            if (descriptor.Lifetime == ServiceLifetime.Transient)
            {
                _services.TryAddEnumerable(descriptor);
            }
            else
            {
                _services.TryAdd(descriptor);
            }
        }
        else if (descriptor.Lifetime == ServiceLifetime.Transient)
        {
            // Transient 生命周期默认使用 TryAddEnumerable 以支持多个实现
            _services.TryAddEnumerable(descriptor);
        }
        else
        {
            _services.Add(descriptor);
        }
    }
}