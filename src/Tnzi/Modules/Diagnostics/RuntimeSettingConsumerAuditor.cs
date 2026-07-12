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

                if (directTypes.Contains(opt))
                {
                    directWarnings.Add(
                        $"{impl.Name} consumes {opt.Name} via IOptions<{opt.Name}> but {opt.Name} has " +
                        $"[RuntimeSetting] fields (hot-settable). Use IOptionsMonitor<{opt.Name}> (or " +
                        $"IOptionsSnapshot<{opt.Name}> in scoped services) so admin changes take effect.");
                }
                else if (scannedAssemblies.Contains(opt.Assembly) && ContainsRuntimeSetting(opt))
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
}
