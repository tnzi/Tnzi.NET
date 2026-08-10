namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// Module dependency auditor
/// Analyzes cross-module service dependencies and reports undeclared [DependsOn] violations
/// </summary>
public static class ModuleDependencyAuditor
{
    /// <summary>
    /// Audit module dependencies and return structured violation results
    /// </summary>
    public static IReadOnlyList<DependencyViolation> AuditAndReport(
        IReadOnlyList<IModuleDescriptor> modules,
        Dictionary<Type, List<ServiceDescriptor>> moduleServiceMap)
    {
        if (modules == null || moduleServiceMap == null || modules.Count == 0)
            return [];

        var serviceTypeToModule = BuildServiceTypeToModuleMap(moduleServiceMap);
        var moduleDependencyChains = BuildDependencyChains(modules);
        var suppressions = BuildSuppressionMap(modules);
        var violations = new List<DependencyViolation>();

        foreach (var module in modules)
        {
            if (!moduleServiceMap.TryGetValue(module.Type, out var descriptors))
                continue;

            var moduleSuppression = suppressions.GetValueOrDefault(module.Type);

            var declaredDependencies = moduleDependencyChains.TryGetValue(module.Type, out var deps)
                ? deps
                : new HashSet<Type>();

            foreach (var descriptor in descriptors)
            {
                var implType = descriptor.ImplementationType;
                if (implType == null) continue;

                var constructors = implType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                foreach (var ctor in constructors)
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        var paramType = param.ParameterType;

                        if (IsSystemService(paramType))
                            continue;

                        // 可选构造参数（`IFoo? foo = null`）是框架表达「可选依赖」的既定写法：
                        // 提供方模块没加载时注入 null、能力优雅退化。把它当硬依赖会把
                        // Storage 消费 IDocumentConverter 这类正确设计报成违规。
                        if (param.HasDefaultValue)
                            continue;

                        if (!serviceTypeToModule.TryGetValue(paramType, out var providerModules))
                            continue;

                        // 一个服务类型可能被多个模块注册（CachingModule 注册 ICache、
                        // RedisCachingModule 再 RemoveAll 后替换）。只要**任一**注册者在
                        // 依赖闭包内，这个依赖就是声明过的。
                        if (providerModules.Any(p => p == module.Type || declaredDependencies.Contains(p)))
                            continue;

                        // 核心程序集里的模块由框架无条件加载，任何模块都不需要声明它们。
                        if (providerModules.Any(IsAlwaysLoadedCoreModule))
                            continue;

                        if (moduleSuppression != null &&
                            moduleSuppression.Any(s => s.IgnoredServiceType == null || s.IgnoredServiceType == paramType))
                            continue;

                        var providerNames = string.Join(" / ", providerModules.Select(p => p.Name));
                        violations.Add(new DependencyViolation(
                            module.Type,
                            paramType,
                            providerModules[0],
                            $"Module {module.Type.Name} uses service {paramType.Name} " +
                            $"(registered by {providerNames}) " +
                            $"but does not declare [DependsOn(typeof({providerModules[0].Name}))]"));
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Audit module dependencies (delegates to AuditAndReport, logs results)
    /// </summary>
    public static void Audit(IReadOnlyList<IModuleDescriptor> modules,
        Dictionary<Type, List<ServiceDescriptor>> moduleServiceMap, ILogger? logger = null)
    {
        var violations = AuditAndReport(modules, moduleServiceMap);

        foreach (var v in violations)
        {
            logger?.LogWarning("{Message}", v.Message);
        }

        if (violations.Count > 0)
            logger?.LogWarning("Module dependency audit completed with {WarningCount} warning(s)", violations.Count);
        else
            logger?.LogDebug("Module dependency audit completed with no warnings");
    }

    /// <summary>
    /// 构建服务类型到<b>全部</b>注册模块的映射。
    /// </summary>
    /// <remarks>
    /// 曾经写作 <c>map[descriptor.ServiceType] = moduleType</c>，即「后注册的覆盖先注册的」。
    /// 那是错的：<c>CachingModule</c> 注册 <c>ICache</c>，<c>RedisCachingModule</c> 之后
    /// <c>RemoveAll&lt;ICache&gt;()</c> 再注册自己的实现，于是 <c>ICache</c> 的「提供者」
    /// 被记成 Redis —— 所有用缓存的模块都会被报成「没声明依赖 RedisCachingModule」，
    /// 而 Redis 只是个可替换实现，没人应该依赖它。保留全部候选，判定时任一命中即放行。
    /// </remarks>
    private static Dictionary<Type, List<Type>> BuildServiceTypeToModuleMap(
        Dictionary<Type, List<ServiceDescriptor>> moduleServiceMap)
    {
        var map = new Dictionary<Type, List<Type>>();

        foreach (var (moduleType, descriptors) in moduleServiceMap)
        {
            foreach (var descriptor in descriptors)
            {
                if (!map.TryGetValue(descriptor.ServiceType, out var providers))
                {
                    providers = [];
                    map[descriptor.ServiceType] = providers;
                }

                if (!providers.Contains(moduleType))
                    providers.Add(moduleType);
            }
        }

        return map;
    }

    /// <summary>
    /// 是否为核心程序集里那批由框架无条件加载的模块。
    /// </summary>
    /// <remarks>
    /// <c>CoreServicesModule</c> / <c>CachingModule</c> / <c>EventBusModule</c> /
    /// <c>ResilienceModule</c> / <c>DependencyInjectionModule</c> 随 <c>TnziApplication</c>
    /// 一起加载，不在任何模块的 <c>[DependsOn]</c> 里，也不该要求写 —— 每个模块都必然
    /// 引用核心程序集，它们的服务（<c>ICache</c>、<c>IEventBus</c>、<c>TimeProvider</c>…）
    /// 是无条件可用的基线。
    /// </remarks>
    private static bool IsAlwaysLoadedCoreModule(Type moduleType)
        => moduleType.Assembly == typeof(ITnziModule).Assembly;

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

    private static Dictionary<Type, List<SuppressDependencyAuditAttribute>> BuildSuppressionMap(
        IReadOnlyList<IModuleDescriptor> modules)
    {
        var map = new Dictionary<Type, List<SuppressDependencyAuditAttribute>>();
        foreach (var module in modules)
        {
            var attrs = module.Type.GetCustomAttributes<SuppressDependencyAuditAttribute>().ToList();
            if (attrs.Count > 0)
                map[module.Type] = attrs;
        }
        return map;
    }
}
