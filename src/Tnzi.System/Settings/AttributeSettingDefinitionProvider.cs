namespace Tnzi.System.Settings;

/// <summary>扫描已加载模块程序集中带 [RuntimeSetting] 的 Options，自动派生配置中心分组定义。</summary>
public sealed class AttributeSettingDefinitionProvider : ISettingDefinitionProvider
{
    private readonly ITnziApplication? _application;
    private readonly ILogger<AttributeSettingDefinitionProvider>? _logger;
    private IReadOnlyList<SettingDefinitionGroup>? _cache;

    public AttributeSettingDefinitionProvider(
        ITnziApplication? application = null,
        ILogger<AttributeSettingDefinitionProvider>? logger = null)
    {
        _application = application;
        _logger = logger;
    }

    public IReadOnlyList<SettingDefinitionGroup> GetGroups()
    {
        if (_cache is { } cached) return cached;
        var computed = Build();
        Interlocked.CompareExchange(ref _cache, computed, null);
        return _cache!;
    }

    private IReadOnlyList<SettingDefinitionGroup> Build()
    {
        var rawGroups = new List<SettingDefinitionGroup>();
        foreach (var asm in CollectAssemblies())
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            foreach (var t in types)
            {
                if (t == null) continue;
                try
                {
                    var group = RuntimeSettingMetadataExtractor.Extract(t);
                    if (group != null) rawGroups.Add(group);
                }
                catch (InvalidOperationException)
                {
                    // Developer misuse (e.g. [RuntimeSetting] on a non-scalar property) → fail fast, do not swallow.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to extract runtime settings from {Type}", t.FullName);
                }
            }
        }
        var merged = MergeByGroupKey(rawGroups);
        ValidateNoConflicts(merged);
        return merged;
    }

    private IEnumerable<Assembly> CollectAssemblies()
        => _application != null
            ? _application.Modules.Select(m => m.Type.Assembly).Distinct()
            : AppDomain.CurrentDomain.GetAssemblies();

    /// <summary>
    /// 将共享相同 Key（OrdinalIgnoreCase）的多个原始分组合并为单个分组。
    /// 合并语义：
    /// - 贡献者排序：Order 升序，同 Order 再按 DisplayName 字典序，再按首个字段 Key 字典序。
    /// - 主分组（排序首位）决定 Key/ModuleName/DisplayName/Icon/I18nKey/Description 的默认值；
    ///   空/null 值由后续贡献者依序补齐（first non-null/non-empty wins）。
    /// - Order = 各贡献者 Order 的最小值。
    /// - Fields = 各贡献者的字段列表按贡献者排序顺序拼接（保留每个贡献者的内部顺序）。
    /// - 单贡献者分组原样透传（pass-through）。
    /// </summary>
    public static IReadOnlyList<SettingDefinitionGroup> MergeByGroupKey(IReadOnlyList<SettingDefinitionGroup> rawGroups)
    {
        Check.NotNull(rawGroups);

        // Group by key (case-insensitive), preserving overall first-seen key ordering for stable output.
        var clusters = new Dictionary<string, List<SettingDefinitionGroup>>(StringComparer.OrdinalIgnoreCase);
        var keyOrder = new List<string>(); // track first-seen order of distinct keys

        foreach (var g in rawGroups)
        {
            if (!clusters.TryGetValue(g.Key, out var bucket))
            {
                bucket = [];
                clusters[g.Key] = bucket;
                keyOrder.Add(g.Key);
            }
            bucket.Add(g);
        }

        var result = new List<SettingDefinitionGroup>(clusters.Count);
        foreach (var key in keyOrder)
        {
            var contributors = clusters[key];

            if (contributors.Count == 1)
            {
                result.Add(contributors[0]);
                continue;
            }

            // Sort contributors: Order asc, then DisplayName ordinal, then first field Key ordinal.
            contributors.Sort((a, b) =>
            {
                var cmp = a.Order.CompareTo(b.Order);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                var aFirst = a.Fields.Count > 0 ? a.Fields[0].Key : string.Empty;
                var bFirst = b.Fields.Count > 0 ? b.Fields[0].Key : string.Empty;
                return string.Compare(aFirst, bFirst, StringComparison.Ordinal);
            });

            // First non-null/non-empty value wins for each metadata property.
            static string? FirstNonEmpty(IEnumerable<string?> values)
                => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));

            var mergedFields = contributors.SelectMany(c => c.Fields).ToList();

            result.Add(new SettingDefinitionGroup
            {
                Key = contributors[0].Key,
                Order = contributors.Min(c => c.Order),
                // Built-in only when every contributor is a framework group - a single
                // consumer contribution keeps the merged group visible under the toggle.
                IsBuiltIn = contributors.All(c => c.IsBuiltIn),
                ModuleName = FirstNonEmpty(contributors.Select(c => c.ModuleName)) ?? contributors[0].ModuleName,
                DisplayName = FirstNonEmpty(contributors.Select(c => c.DisplayName)) ?? contributors[0].DisplayName,
                I18nKey = FirstNonEmpty(contributors.Select(c => c.I18nKey)),
                Icon = FirstNonEmpty(contributors.Select(c => c.Icon)),
                Description = FirstNonEmpty(contributors.Select(c => c.Description)),
                PermissionGroup = FirstNonEmpty(contributors.Select(c => c.PermissionGroup)),
                PermissionSlug = FirstNonEmpty(contributors.Select(c => c.PermissionSlug)),
                // 合并组携带全部贡献者的 Options 类型 - validator 预检要对每个类型分别
                // 绑定候选并验证，只留第一个会让其余贡献者的字段静默跳过预检。
                OptionsTypes = contributors
                    .SelectMany(c => c.OptionsTypes ?? [])
                    .Distinct()
                    .ToList() is { Count: > 0 } types ? types : null,
                Fields = mergedFields,
            });
        }

        return result;
    }

    /// <summary>
    /// 校验合并后的分组列表中不存在重复的字段键。
    /// 重复字段 key 会导致配置中心行为不确定，应在启动期快速失败。
    /// 注：重复的分组 key 在调用前已由 <see cref="MergeByGroupKey"/> 合并，此处不再检查。
    /// </summary>
    /// <exception cref="InvalidOperationException">存在重复的字段键时抛出。</exception>
    public static void ValidateNoConflicts(IReadOnlyList<SettingDefinitionGroup> groups)
    {
        var fieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var f in g.Fields)
                if (!fieldKeys.Add(f.Key))
                    throw new InvalidOperationException(
                        $"Duplicate runtime-setting field key '{f.Key}'. Two [RuntimeSetting] properties resolve to the same config key.");
    }
}
