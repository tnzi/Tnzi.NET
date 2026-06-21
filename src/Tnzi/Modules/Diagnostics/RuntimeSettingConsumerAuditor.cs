namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// 检测「标记 [RuntimeSetting]（可热设置）的 Options 却被 IOptions&lt;T&gt;/IOptionsSnapshot&lt;T&gt; 消费」
/// 的沉默失败（admin 改了不生效）。仅扫描构造函数注入。
/// </summary>
public static class RuntimeSettingConsumerAuditor
{
    public static IReadOnlyList<string> AuditAndReport(
        IEnumerable<ServiceDescriptor> descriptors, IEnumerable<Assembly> assemblies)
    {
        if (descriptors == null || assemblies == null) return [];

        var runtimeTypes = new HashSet<Type>();
        foreach (var asm in assemblies.Distinct())
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            foreach (var t in types)
            {
                if (t == null) continue;
                if (t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Any(p => p.GetCustomAttribute<RuntimeSettingAttribute>() != null))
                    runtimeTypes.Add(t);
            }
        }
        if (runtimeTypes.Count == 0) return [];

        var warnings = new List<string>();
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
                var def = pt.GetGenericTypeDefinition();
                if (def != typeof(IOptions<>) && def != typeof(IOptionsSnapshot<>)) continue;
                var opt = pt.GetGenericArguments()[0];
                if (!runtimeTypes.Contains(opt)) continue;
                if (!seen.Add((impl, opt))) continue;
                var wrapper = def.Name.Split('`')[0];
                warnings.Add(
                    $"{impl.Name} consumes {opt.Name} via {wrapper}<{opt.Name}> but {opt.Name} has " +
                    $"[RuntimeSetting] fields (hot-settable). Use IOptionsMonitor<{opt.Name}> so admin changes take effect.");
            }
        }
        return warnings;
    }

    public static void Audit(IEnumerable<ServiceDescriptor> descriptors, IEnumerable<Assembly> assemblies, ILogger? logger = null)
    {
        var warnings = AuditAndReport(descriptors, assemblies);
        foreach (var w in warnings) logger?.LogWarning("{Message}", w);
        if (warnings.Count > 0)
            logger?.LogWarning("Runtime setting consumer audit completed with {Count} warning(s)", warnings.Count);
    }
}
