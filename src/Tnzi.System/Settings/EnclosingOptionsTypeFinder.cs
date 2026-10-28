namespace Tnzi.System.Settings;

/// <summary>
/// 找出把某个 Options 类型当作自己一段来绑定的外层 Options 类型（逐级向上）。
/// </summary>
/// <remarks>
/// <para>
/// 配置中心的组常常只是某个根 Options 的一段：<c>web-ratelimit</c> 的 <c>RateLimitOptions</c>
/// 绑定在 <c>AspNetCore:RateLimit</c>，运行时真正注册、重绑定并验证的是 <c>AspNetCoreOptions</c>。
/// 组类型自己没有验证器，所以保存前的预检必须连外层类型一起验，否则根验证器里
/// 「开关打开才检查」的规则恰好在这次保存之后才生效，而那时已经落库。
/// </para>
/// <para>
/// 判定是结构性的：外层类型有一个类型正是该 Options 的公共属性，且外层的配置段 + <c>:</c> + 属性名
/// 等于该 Options 的配置段（不分大小写）。只扫描本身就是、或引用了该 Options 所在程序集的程序集
/// （外层类型要声明这个属性就必须引用它），结果按类型缓存。
/// </para>
/// </remarks>
public static class EnclosingOptionsTypeFinder
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> Cache = new();

    /// <summary>
    /// 返回所有外层 Options 类型，由近及远（直接外层在前），不含 <paramref name="optionsType"/> 本身。
    /// </summary>
    public static IReadOnlyList<Type> Find(Type optionsType)
    {
        Check.NotNull(optionsType);
        return Cache.GetOrAdd(optionsType, Scan);
    }

    private static IReadOnlyList<Type> Scan(Type optionsType)
    {
        var result = new List<Type>();
        var seen = new HashSet<Type> { optionsType };
        var pending = new Queue<Type>();
        pending.Enqueue(optionsType);

        while (pending.TryDequeue(out var inner))
        {
            foreach (var outer in FindDirectlyEnclosing(inner))
            {
                if (!seen.Add(outer)) continue;
                result.Add(outer);
                pending.Enqueue(outer);
            }
        }

        return result;
    }

    private static IEnumerable<Type> FindDirectlyEnclosing(Type inner)
    {
        var innerSection = ConfigSectionResolver.Resolve(inner);
        var innerAssemblyName = inner.Assembly.GetName().Name;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            if (assembly != inner.Assembly
                && !assembly.GetReferencedAssemblies().Any(r => string.Equals(r.Name, innerAssemblyName, StringComparison.Ordinal)))
                continue;

            foreach (var candidate in LoadableTypes(assembly))
            {
                if (!candidate.IsClass || candidate.IsAbstract || candidate.ContainsGenericParameters || candidate == inner) continue;

                var bindsInner = candidate
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(p => p.PropertyType == inner
                              && string.Equals(
                                  $"{ConfigSectionResolver.Resolve(candidate)}:{p.Name}",
                                  innerSection,
                                  StringComparison.OrdinalIgnoreCase));
                if (bindsInner) yield return candidate;
            }
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
