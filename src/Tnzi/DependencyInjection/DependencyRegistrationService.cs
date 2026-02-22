
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
                System.Diagnostics.Debug.WriteLine($"Failed to scan assembly '{assembly.GetName().Name}' for type '{typeof(TInterface).Name}': {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"Failed to scan assembly '{assembly.GetName().Name}' for DependencyAttribute types: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"WARNING: {message}");
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

        // 如果没有接口，注册自身
        if (serviceTypes.Count == 0)
        {
            RegisterService(implementationType, implementationType, lifetime.Value, dependencyAttribute);
            return;
        }

        // 如果要求注册自身
        if (dependencyAttribute?.AddSelf == true)
        {
            RegisterService(implementationType, implementationType, lifetime.Value, dependencyAttribute);
        }

        // 注册接口
        // 对接口进行排序：优先注册业务接口（非 Tnzi 命名空间），排除框架接口
        var orderedServiceTypes = serviceTypes
            .OrderBy(t => t.Namespace?.StartsWith("Tnzi") ?? false)  // Tnzi 命名空间的接口排在后面
            .ThenBy(t => t.FullName ?? t.Name)
            .ToList();

        // 先注册第一个接口（主要接口，通常是业务接口）
        var firstServiceType = orderedServiceTypes[0];
        RegisterService(firstServiceType, implementationType, lifetime.Value, dependencyAttribute);

        // 注册后续接口，使用第一个接口的实例（保证同一实现类的多个接口获得同一实例）
        for (int i = 1; i < orderedServiceTypes.Count; i++)
        {
            var serviceType = orderedServiceTypes[i];
            var descriptor = new ServiceDescriptor(
                serviceType,
                provider => provider.GetRequiredService(firstServiceType),
                lifetime.Value);

            // 后续接口使用 TryAdd，避免覆盖第一个接口的注册
            // 注意：这里不使用 dependencyAttribute，因为第一个接口已经应用了
            _services.TryAdd(descriptor);
        }
    }

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