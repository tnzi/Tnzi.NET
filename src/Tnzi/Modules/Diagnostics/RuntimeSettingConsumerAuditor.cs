namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// 检测「标记 [RuntimeSetting]（可热设置）的 Options 却被 IOptions&lt;T&gt; 启动快照消费」
/// 的沉默失败（admin 改了不生效）。仅扫描构造函数注入。
///
/// 判定规则：
/// - IOptionsMonitor&lt;T&gt; 与 IOptionsSnapshot&lt;T&gt; 均视为热消费 —— Snapshot 是 Scoped 服务，
///   能被注入即每请求重算（Singleton 注入 Snapshot 会被 DI 作用域校验直接拒绝，非本审计职责）。
/// - 直接命中（消费类型自身带 [RuntimeSetting] 属性）：高置信告警。
/// - 嵌套命中（消费类型的某个嵌套 Options 属性带 [RuntimeSetting]，经父聚合 IOptions 消费）：
///   低置信提示 —— 审计无法确定消费者是否读到热字段，单独分级输出避免淹没高置信告警。
/// - 参数上带 [ReadsOnlyColdSettings]：按声明的字段清单逐条核验，全是冷字段则放行。
///
/// 为什么需要最后一条：反射能看到的是「这个 Options <b>类型</b>里有没有热字段」，
/// 而真正的缺陷判据是「这个<b>消费者</b>读没读到热字段」。一个 Options 类同时装着
/// 部署机密（刻意不做成热设置）与热设置时两者就分叉，于是「只读机密、启动时解析一次」
/// 这种<b>正确</b>的用法也会被告警。把它机械改成 IOptionsMonitor 只是消掉症状，
/// 审计对下一个同形消费者照样噪音 —— 所以补的是判据的精度，不是消费者的写法。
/// </summary>
public static class RuntimeSettingConsumerAuditor
{
    /// <summary>审计结果：高置信（直接类型）告警 + 低置信（嵌套聚合）提示。</summary>
    public sealed class AuditResult
    {
        public required IReadOnlyList<string> DirectWarnings { get; init; }
        public required IReadOnlyList<string> NestedHints { get; init; }
    }

    public static IReadOnlyList<string> AuditAndReport(
        IEnumerable<ServiceDescriptor> descriptors, IEnumerable<Assembly> assemblies)
    {
        var result = AuditDetailed(descriptors, assemblies);
        return [.. result.DirectWarnings, .. result.NestedHints];
    }

    public static AuditResult AuditDetailed(
        IEnumerable<ServiceDescriptor> descriptors, IEnumerable<Assembly> assemblies)
    {
        if (descriptors == null || assemblies == null)
            return new AuditResult { DirectWarnings = [], NestedHints = [] };

        var scannedAssemblies = assemblies.Distinct().ToHashSet();
        var directTypes = new HashSet<Type>();
        var allTypes = new List<Type>();
        foreach (var asm in scannedAssemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            foreach (var t in types)
            {
                if (t == null) continue;
                allTypes.Add(t);
                if (HasDirectRuntimeSetting(t))
                    directTypes.Add(t);
            }
        }
        if (directTypes.Count == 0)
            return new AuditResult { DirectWarnings = [], NestedHints = [] };

        // 嵌套穿透：类型 T「传递包含」热设置 = 某个公共实例属性的类型（限于被扫描程序集内定义，
        // 防止图爆炸）直接或传递带 [RuntimeSetting]。经父聚合 IOptions<T> 消费同样可能断链。
        var containsCache = new Dictionary<Type, bool>();
        bool ContainsRuntimeSetting(Type t)
        {
            if (directTypes.Contains(t)) return true;
            if (containsCache.TryGetValue(t, out var cached)) return cached;
            containsCache[t] = false; // 防循环：计算中默认 false
            var result = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType)
                .Where(pt => pt.IsClass && pt != typeof(string) && scannedAssemblies.Contains(pt.Assembly))
                .Any(ContainsRuntimeSetting);
            containsCache[t] = result;
            return result;
        }

        var directWarnings = new List<string>();
        var nestedHints = new List<string>();
        var seen = new HashSet<(Type, Type)>();
        foreach (var d in descriptors)
        {
            var impl = d.ImplementationType;
            if (impl == null) continue;
            foreach (var ctor in impl.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            foreach (var param in ctor.GetParameters())
            {
                var pt = param.ParameterType;
                if (!pt.IsGenericType) continue;
                if (pt.GetGenericTypeDefinition() != typeof(IOptions<>)) continue;
                var opt = pt.GetGenericArguments()[0];
                if (!seen.Add((impl, opt))) continue;

                var isDirect = directTypes.Contains(opt);
                var isNested = !isDirect
                    && scannedAssemblies.Contains(opt.Assembly)
                    && ContainsRuntimeSetting(opt);
                if (!isDirect && !isNested) continue;

                var declared = param.GetCustomAttribute<ReadsOnlyColdSettingsAttribute>();
                if (declared != null)
                {
                    var problem = DescribeDeclarationProblem(opt, declared);
                    if (problem == null) continue; // 声明核验通过：读的确实全是冷字段

                    // 声明有问题一律进高置信告警，哪怕命中只是嵌套级：一份说谎或写错的声明
                    // 关掉的正是这条审计存在的理由，它必须比「没有声明」更响而不是更轻。
                    directWarnings.Add(
                        $"{impl.Name} declares [ReadsOnlyColdSettings] on its IOptions<{opt.Name}> parameter " +
                        $"but {problem}. List only cold properties of {opt.Name} using nameof(...), or switch " +
                        $"to IOptionsMonitor<{opt.Name}>.");
                    continue;
                }

                if (isDirect)
                {
                    directWarnings.Add(
                        $"{impl.Name} consumes {opt.Name} via IOptions<{opt.Name}> but {opt.Name} has " +
                        $"[RuntimeSetting] fields (hot-settable). Use IOptionsMonitor<{opt.Name}> (or " +
                        $"IOptionsSnapshot<{opt.Name}> in scoped services) so admin changes take effect. " +
                        $"If this consumer only ever reads cold fields, declare them with " +
                        $"[ReadsOnlyColdSettings(nameof({opt.Name}.SomeColdField))].");
                }
                else
                {
                    nestedHints.Add(
                        $"{impl.Name} consumes {opt.Name} via IOptions<{opt.Name}>; {opt.Name} nests option " +
                        $"type(s) with [RuntimeSetting] fields. If this consumer reads any hot-settable nested " +
                        $"field, switch to IOptionsMonitor<{opt.Name}>.");
                }
            }
        }
        return new AuditResult { DirectWarnings = directWarnings, NestedHints = nestedHints };
    }

    public static void Audit(IEnumerable<ServiceDescriptor> descriptors, IEnumerable<Assembly> assemblies, ILogger? logger = null)
    {
        var result = AuditDetailed(descriptors, assemblies);
        foreach (var w in result.DirectWarnings) logger?.LogWarning("{Message}", w);
        if (result.DirectWarnings.Count > 0)
            logger?.LogWarning("Runtime setting consumer audit completed with {Count} warning(s)", result.DirectWarnings.Count);
        // 嵌套命中是低置信提示（消费者未必读热字段），聚合成单条 Information 避免刷屏。
        if (result.NestedHints.Count > 0)
            logger?.LogInformation(
                "Runtime setting consumer audit: {Count} aggregate options consumer(s) hold hot-settable nested " +
                "options via IOptions<T> (verify they do not read hot fields): {Details}",
                result.NestedHints.Count, string.Join(" | ", result.NestedHints));
    }

    private static bool HasDirectRuntimeSetting(Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttribute<RuntimeSettingAttribute>() != null);

    /// <summary>
    /// 核验 <see cref="ReadsOnlyColdSettingsAttribute"/> 的清单。
    /// </summary>
    /// <returns>
    /// <c>null</c> 表示声明有效（列出的属性都存在且都是冷字段）；否则返回可直接拼进告警的问题描述。
    /// </returns>
    /// <remarks>
    /// 能核验的只有「列出的确实是冷的」，核验不了「有没有列漏」—— 后者需要跨方法、跨类型的
    /// 数据流分析，而分析一旦有盲区就会把「看不出来」读成「没问题」，那正是本审计要防的形态。
    /// 所以这里的默认值是拒绝：没有声明就告警，声明不合格就告警。
    /// </remarks>
    private static string? DescribeDeclarationProblem(Type optionsType, ReadsOnlyColdSettingsAttribute declared)
    {
        if (declared.PropertyPaths.Count == 0)
            return "lists no property";

        var invalid = new List<string>();
        foreach (var path in declared.PropertyPaths)
        {
            var property = ResolvePropertyPath(optionsType, path);
            if (property == null)
                invalid.Add($"'{path}' (no such property)");
            else if (property.GetCustomAttribute<RuntimeSettingAttribute>() != null)
                invalid.Add($"'{path}' (is itself [RuntimeSetting])");
        }

        return invalid.Count == 0 ? null : $"lists {string.Join(", ", invalid)}";
    }

    /// <summary>按点号路径解析嵌套属性（<c>"Gateway.Timeout"</c>），任一段解析不到即返回 null。</summary>
    private static PropertyInfo? ResolvePropertyPath(Type root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var owner = root;
        PropertyInfo? property = null;
        foreach (var segment in path.Split('.'))
        {
            property = owner.GetProperty(segment.Trim(), BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return null;
            owner = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return property;
    }
}
