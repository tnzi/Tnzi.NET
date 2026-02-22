namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// 模块依赖审计器
/// 在开发环境中分析模块注册的服务与 [DependsOn] 声明的依赖关系，
/// 对未声明的跨模块依赖输出警告
/// </summary>
public static class ModuleDependencyAuditor
{
    /// <summary>
    /// 审计模块依赖关系
    /// </summary>
    /// <param name="modules">已加载的模块列表</param>
    /// <param name="moduleServiceMap">每个模块注册的 ServiceDescriptor 列表</param>
    /// <param name="logger">日志记录器</param>
    public static void Audit(IReadOnlyList<IModuleDescriptor> modules, Dictionary<Type, List<ServiceDescriptor>> moduleServiceMap, ILogger? logger = null)
    {
        if (modules == null || moduleServiceMap == null || modules.Count == 0)
            return;

        // 构建服务类型 -> 注册模块的映射
        var serviceTypeToModule = BuildServiceTypeToModuleMap(moduleServiceMap);

        // 构建每个模块的完整依赖链（包括传递依赖）
        var moduleDependencyChains = BuildDependencyChains(modules);

        var warningCount = 0;

        foreach (var module in modules)
        {
            if (!moduleServiceMap.TryGetValue(module.Type, out var descriptors))
                continue;

            var declaredDependencies = moduleDependencyChains.TryGetValue(module.Type, out var deps)
                ? deps
                : new HashSet<Type>();

            foreach (var descriptor in descriptors)
            {
                var implType = descriptor.ImplementationType;
                if (implType == null) continue;

                // 检查构造函数参数
                var constructors = implType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                foreach (var ctor in constructors)
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        var paramType = param.ParameterType;

                        // 跳过系统服务和框架基础设施
                        if (IsSystemService(paramType))
                            continue;

                        // 查找该参数类型由哪个模块注册
                        if (serviceTypeToModule.TryGetValue(paramType, out var providerModuleType))
                        {
                            // 跳过自身模块
                            if (providerModuleType == module.Type)
                                continue;

                            // 检查是否在依赖链中
                            if (!declaredDependencies.Contains(providerModuleType))
                            {
                                logger?.LogWarning(
                                    "Module dependency audit: {ModuleType} uses service {ServiceType} (registered by {ProviderModule}) " +
                                    "but does not declare [DependsOn(typeof({ProviderModule}))]. " +
                                    "Consider adding the dependency declaration.",
                                    module.Type.Name, paramType.Name, providerModuleType.Name, providerModuleType.Name);
                                warningCount++;
                            }
                        }
                    }
                }
            }
        }

        if (warningCount > 0)
        {
            logger?.LogWarning("Module dependency audit completed with {WarningCount} warning(s)", warningCount);
        }
        else
        {
            logger?.LogDebug("Module dependency audit completed with no warnings");
        }
    }

    /// <summary>
    /// 构建服务类型到注册模块的映射
    /// </summary>
    private static Dictionary<Type, Type> BuildServiceTypeToModuleMap(Dictionary<Type, List<ServiceDescriptor>> moduleServiceMap)
    {
        var map = new Dictionary<Type, Type>();

        foreach (var (moduleType, descriptors) in moduleServiceMap)
        {
            foreach (var descriptor in descriptors)
            {
                // 使用 ServiceType 作为键，后注册的模块覆盖先注册的
                map[descriptor.ServiceType] = moduleType;
            }
        }

        return map;
    }

    /// <summary>
    /// 构建每个模块的完整依赖链（包括传递依赖）
    /// </summary>
    private static Dictionary<Type, HashSet<Type>> BuildDependencyChains(IReadOnlyList<IModuleDescriptor> modules)
    {
        var chains = new Dictionary<Type, HashSet<Type>>();

        foreach (var module in modules)
        {
            var allDeps = new HashSet<Type>();
            CollectTransitiveDependencies(module, allDeps);
            chains[module.Type] = allDeps;
        }

        return chains;
    }

    /// <summary>
    /// 递归收集传递依赖
    /// </summary>
    private static void CollectTransitiveDependencies(IModuleDescriptor module, HashSet<Type> collected)
    {
        foreach (var dep in module.Dependencies)
        {
            if (collected.Add(dep.Type))
            {
                CollectTransitiveDependencies(dep, collected);
            }
        }
    }

    /// <summary>
    /// 判断是否为系统/基础设施服务（无需审计）
    /// </summary>
    private static bool IsSystemService(Type type)
    {
        var ns = type.Namespace ?? "";

        // Microsoft/System 命名空间
        if (ns.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            ns.StartsWith("System.", StringComparison.Ordinal))
            return true;

        // 常见的框架基础服务
        if (type == typeof(IServiceProvider) ||
            type == typeof(IConfiguration) ||
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>) ||
            type == typeof(ILogger) ||
            type == typeof(ILoggerFactory) ||
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptions<>) ||
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptionsMonitor<>) ||
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptionsSnapshot<>))
            return true;

        return false;
    }
}
