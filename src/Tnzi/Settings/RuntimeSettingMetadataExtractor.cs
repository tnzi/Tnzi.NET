namespace Tnzi.Settings;

/// <summary>从打了 [RuntimeSetting] 的 Options 类型派生配置中心分组定义（纯函数）。</summary>
public static class RuntimeSettingMetadataExtractor
{
    public static SettingDefinitionGroup? Extract(Type optionsType)
    {
        Check.NotNull(optionsType);
        var props = optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RuntimeSettingAttribute>() != null)
            .ToList();
        if (props.Count == 0) return null;

        var section = ConfigSectionResolver.Resolve(optionsType);
        var groupAttr = optionsType.GetCustomAttribute<RuntimeSettingGroupAttribute>();
        object? instance = null;
        try { instance = Activator.CreateInstance(optionsType); } catch { /* 无默认构造则无默认值 */ }

        var fields = new List<SettingFieldDefinition>(props.Count);
        foreach (var p in props)
        {
            if (!IsScalar(p.PropertyType))
                throw new InvalidOperationException(
                    $"[RuntimeSetting] on '{optionsType.Name}.{p.Name}' (type '{p.PropertyType.Name}') is not supported: " +
                    "only scalar types (string, numeric, bool, enum, Guid, DateTime, DateTimeOffset, TimeSpan) can be runtime settings.");

            var a = p.GetCustomAttribute<RuntimeSettingAttribute>()!;
            var type = a.Type == SettingFieldType.Auto ? InferType(p.PropertyType) : a.Type;
            ValidatePattern(optionsType, p, a.Pattern);
            string? defaultValue = null;
            if (instance != null)
            {
                try { defaultValue = p.GetValue(instance)?.ToString(); } catch { /* ignore */ }
            }
            fields.Add(new SettingFieldDefinition
            {
                Key = $"{section}:{p.Name}",
                Label = a.Label ?? p.Name,
                I18nKey = a.I18n,
                Description = a.Description,
                Type = type,
                IsReadOnly = a.ReadOnly,
                IsRequired = a.Required,
                Min = double.IsNaN(a.Min) ? null : a.Min,
                Max = double.IsNaN(a.Max) ? null : a.Max,
                Pattern = string.IsNullOrWhiteSpace(a.Pattern) ? null : a.Pattern,
                Options = ResolveOptions(a, p.PropertyType, type),
                Subsection = string.IsNullOrWhiteSpace(a.Subsection) ? null : a.Subsection,
                DefaultValueAccessor = defaultValue == null ? null : () => defaultValue,
            });
        }

        return new SettingDefinitionGroup
        {
            Key = groupAttr?.Key ?? section.ToLowerInvariant(),
            ModuleName = groupAttr?.Module ?? section,
            DisplayName = groupAttr?.DisplayName ?? section,
            I18nKey = groupAttr?.I18nKey,
            Icon = groupAttr?.Icon,
            Order = groupAttr?.Order ?? 0,
            PermissionGroup = groupAttr?.PermissionGroup,
            PermissionSlug = groupAttr?.PermissionSlug,
            OptionsTypes = [optionsType],
            Fields = fields,
        };
    }

    /// <summary>无效正则属于定义错误，启动期 fail-fast（与非标量属性同一护栏纪律）。</summary>
    private static void ValidatePattern(Type optionsType, PropertyInfo property, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"[RuntimeSetting] on '{optionsType.Name}.{property.Name}' has an invalid Pattern regex: {ex.Message}", ex);
        }
    }

    /// <summary>判断属性类型是否为可经字符串往返的标量（可作为单条 Setting 行）。非标量的 [RuntimeSetting] 属于误用。</summary>
    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(string) || t.IsPrimitive || t.IsEnum
            || t == typeof(decimal) || t == typeof(Guid)
            || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);
    }

    private static SettingFieldType InferType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(bool)) return SettingFieldType.Boolean;
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return SettingFieldType.Int;
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return SettingFieldType.Decimal;
        if (t == typeof(TimeSpan)) return SettingFieldType.Duration;
        if (t.IsEnum) return SettingFieldType.Select;
        return SettingFieldType.String;
    }

    private static IReadOnlyList<string>? ResolveOptions(RuntimeSettingAttribute a, Type propType, SettingFieldType type)
    {
        if (!string.IsNullOrWhiteSpace(a.Options))
            return a.Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var underlying = Nullable.GetUnderlyingType(propType) ?? propType;
        if (type == SettingFieldType.Select && underlying.IsEnum)
            return Enum.GetNames(underlying);
        return null;
    }
}
