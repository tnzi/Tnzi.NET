namespace Tnzi.System.Dtos;

/// <summary>配置中心字段 DTO（schema + 当前生效值）。</summary>
public class SettingsCenterFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? I18nKey { get; set; }
    public string? Description { get; set; }

    /// <summary>SettingFieldType 枚举名字符串（String/Text/Int/Decimal/Boolean/Select/Password）。</summary>
    public string Type { get; set; } = nameof(SettingFieldType.String);

    public bool IsEncrypted { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsRequired { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<string>? Options { get; set; }

    /// <summary>当前生效值。加密字段恒为 null（用 IsSet 表达是否已配置）。</summary>
    public string? Value { get; set; }

    /// <summary>默认值（appsettings 原始值 → 编译期默认）。加密字段恒为 null。</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Setting 表存在覆盖行。</summary>
    public bool IsOverridden { get; set; }

    /// <summary>加密字段已配置（覆盖行存在）。非加密字段与 IsOverridden 相同。</summary>
    public bool IsSet { get; set; }
}

/// <summary>配置中心分组 DTO。</summary>
public class SettingsCenterGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? I18nKey { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int Order { get; set; }
    public List<SettingsCenterFieldDto> Fields { get; set; } = new();
}
