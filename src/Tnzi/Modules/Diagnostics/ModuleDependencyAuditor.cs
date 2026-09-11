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
    /// 第二种审计：模块<b>编译期引用</b>了另一个模块所在的程序集，却没有任何 <c>[DependsOn]</c> /
    /// <c>[OptionalDependsOn]</c> 路径提到那个模块。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="AuditAndReport"/> 是两个角度，互补而不重叠：那一条查<b>服务解析</b>
    /// （构造参数要一个别人注册的服务），本条查<b>程序集引用</b>。后者更早也更便宜，
    /// 而且抓得到前者结构上看不见的一类事故 —— <b>模块的 <c>ConfigureServicesAsync</c> 自己
    /// 注册了什么、它的 <c>PreConfigure</c> 往某个静态注册表里塞了什么，都不经过任何构造参数。</b>
    /// </para>
    /// <para>
    /// ★ 这类失效的形态是<b>安静的降级</b>：某模块的包被引用了（于是类型解析得到、代码编译得过），
    /// 但没有 <c>[DependsOn]</c> 提名它，于是 <c>ModuleLoader</c> 不加载它、它的
    /// <c>PreConfigureServicesAsync</c> 一行都不跑。消费方随后按「优雅退化」的设计发现能力不可用，
    /// 于是<b>什么都不做并返回成功</b>。一个真实例子：某应用引用了 QR 生成包却没在模块图里点名，
    /// 二维码生成器从未注册，盖章器判定它不可用、在纸上什么都没盖，而每一条测试都是绿的。
    /// </para>
    /// <para>
    /// ★ <c>[OptionalDependsOn]</c> <b>算作已声明</b>：它的语义正是「我容忍它缺席」，
    /// 报它就是在惩罚一个正确的设计。本审计要抓的是<b>一个字都没提</b>的那种。
    /// </para>
    /// </remarks>
    /// <param name="modules">模块图。</param>
    /// <param name="moduleFilter">
    /// 只审计通过此断言的模块。默认审计全部。
    /// </param>
    /// <param name="assumedLoaded">
    /// 视为「宿主保证会加载」因而无需声明的模块。
    /// </param>
    /// <example>
    /// 消费应用的典型用法 —— <b>只审自己的模块</b>，并把宿主必载的框架模块交代清楚：
    /// <code>
    /// var violations = ModuleDependencyAuditor.AuditAssemblyReferences(
    ///     modules,
    ///     moduleFilter: t =&gt; t.Assembly.GetName().Name!.StartsWith("MyApp."),
    ///     assumedLoaded: [typeof(AspNetCoreModule), typeof(MapsterModule)]);
    /// </code>
    /// </example>
    /// <remarks>
    /// ★★ <b>两个参数不是可选的调优项，不给就基本只会得到误报。</b>这不是推测，是拿框架自己的
    /// 全模块图实测出来的：不带参数跑一次得 41 条，逐条核对后<b>真阳性为零</b>，两类误报各占：
    /// <list type="number">
    /// <item>
    /// <b>宿主必载的基础设施模块（40 条）。</b>每个业务模块都引用 <c>Tnzi.AspNetCore</c>
    /// （要 <c>ApiControllerBase</c>）与 <c>Tnzi.Mapster</c>（要 <c>MapTo</c>），而没有一个声明它们 ——
    /// 因为编排它们是宿主的事。用 <paramref name="assumedLoaded"/> 交代。
    /// </item>
    /// <item>
    /// <b>只用了静态助手的引用（1 条）。</b><c>Tnzi.Storage</c> 引用 <c>Tnzi.Imaging</c> 仅为一个
    /// 静态类 <c>ImageDecodeGuard</c>；静态类不需要任何注册，因此那个模块<b>本来就不必被加载</b>。
    /// 本审计只看程序集引用，天生分辨不出这一类，只能靠人看一眼。
    /// </item>
    /// </list>
    /// 所以它<b>不适合当框架级门禁</b>（会立刻退化成一张全是豁免的名单），
    /// 适合的是消费应用按上面的例子收窄之后当门禁 —— 那里「我在宿主里点名了吗」是个真问题。
    /// </remarks>
    public static IReadOnlyList<DependencyViolation> AuditAssemblyReferences(
        IReadOnlyList<IModuleDescriptor> modules,
        Func<Type, bool>? moduleFilter = null,
        IReadOnlyCollection<Type>? assumedLoaded = null)
    {
        if (modules == null || modules.Count == 0)
            return [];

        var suppressions = BuildSuppressionMap(modules);
        var violations = new List<DependencyViolation>();

        foreach (var module in modules)
        {
            if (moduleFilter != null && !moduleFilter(module.Type))
                continue;

            var declared = CollectDeclaredModules(module.Type);
            var suppression = suppressions.GetValueOrDefault(module.Type);

            foreach (var referenced in ModuleTypesReferencedBy(module.Type.Assembly))
            {
                if (referenced == module.Type || declared.Contains(referenced))
                    continue;

                // 核心程序集里的模块由框架无条件加载，任何模块都不需要声明它们。
                if (IsAlwaysLoadedCoreModule(referenced))
                    continue;

                // 宿主保证会加载的模块（见参数说明）。
                if (assumedLoaded != null && assumedLoaded.Contains(referenced))
                    continue;

                // 抑制以「被忽略的服务类型」表达；这里没有服务类型，故只认整模块抑制
                // （`[SuppressDependencyAudit]` 不带参数的那种）。
                if (suppression != null && suppression.Any(s => s.IgnoredServiceType == null))
                    continue;

                violations.Add(new DependencyViolation(
                    module.Type,
                    referenced,
                    referenced,
                    $"Module {module.Type.Name} references assembly {referenced.Assembly.GetName().Name} " +
                    $"which contains module {referenced.Name}, but no [DependsOn] / [OptionalDependsOn] " +
                    $"path reaches it. Nothing will load {referenced.Name}, so its registrations never run " +
                    $"and consumers degrade silently."));
            }
        }

        return violations;
    }

    /// <summary>
    /// 直接与传递声明的模块集合：必选 <c>[DependsOn]</c> 传递展开，
    /// <c>[OptionalDependsOn]</c> 只算直接声明（它只排序、不发现，传递不成立）。
    /// </summary>
    private static HashSet<Type> CollectDeclaredModules(Type moduleType)
    {
        var declared = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(moduleType);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var attr in current.GetCustomAttributes<DependsOnAttribute>(inherit: true))
            {
                foreach (var dep in attr.DependedModuleTypes)
                {
                    if (declared.Add(dep)) pending.Push(dep);
                }
            }

            // 可选依赖不传递：宣告「我容忍它缺席」的是当前模块，不是它的上游。
            foreach (var attr in current.GetCustomAttributes<OptionalDependsOnAttribute>(inherit: true))
            {
                foreach (var dep in attr.DependedModuleTypes) declared.Add(dep);
            }
        }

        return declared;
    }

    /// <summary>
    /// 某程序集直接引用的其它程序集里，可被显式加载的模块类型。
    /// </summary>
    /// <remarks>
    /// 抽象模块类型（<c>TnziApplicationModule</c> 这类给人继承的基类）不计入 ——
    /// 它们不是可加载的模块，把它们写进 <c>[DependsOn]</c> 反而会让加载器尝试实例化。
    /// 加载不出来的引用程序集安静跳过：本审计的失败方向必须是「少报」。
    /// </remarks>
    private static IEnumerable<Type> ModuleTypesReferencedBy(Assembly assembly)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            Assembly loaded;
            try
            {
                loaded = Assembly.Load(reference);
            }
            catch
            {
                continue;
            }

            Type[] types;
            try
            {
                types = loaded.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = [.. ex.Types.Where(t => t != null)!];
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is { IsClass: true, IsAbstract: false } && typeof(ITnziModule).IsAssignableFrom(type))
                    yield return type;
            }
        }
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
