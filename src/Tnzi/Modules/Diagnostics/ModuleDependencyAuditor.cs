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

                        if (serviceTypeToModule.TryGetValue(paramType, out var providerModuleType))
                        {
                            if (providerModuleType == module.Type)
                                continue;

                            if (!declaredDependencies.Contains(providerModuleType))
                            {
                                // Check suppression
                                if (moduleSuppression != null &&
                                    moduleSuppression.Any(s => s.IgnoredServiceType == null || s.IgnoredServiceType == paramType))
                                    continue;

                                violations.Add(new DependencyViolation(
                                    module.Type,
                                    paramType,
                                    providerModuleType,
                                    $"Module {module.Type.Name} uses service {paramType.Name} " +
                                    $"(registered by {providerModuleType.Name}) " +
                                    $"but does not declare [DependsOn(typeof({providerModuleType.Name}))]"));
                            }
                        }
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
