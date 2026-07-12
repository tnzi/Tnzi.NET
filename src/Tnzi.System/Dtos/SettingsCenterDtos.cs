namespace Tnzi.System.Dtos;

/// <summary>配置中心字段 DTO（schema + 当前生效值）。</summary>
public class SettingsCenterFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? I18nKey { get; set; }
    public string? Description { get; set; }

    /// <summary>SettingFieldType 枚举名字符串（String/Text/Int/Decimal/Boolean/Select/Password/Duration）。</summary>
    public string Type { get; set; } = nameof(SettingFieldType.String);

    public bool IsEncrypted { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsRequired { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>String/Text 值的正则约束（.NET 语法，整值匹配）。前端保存前同源校验。</summary>
    public string? Pattern { get; set; }

    public List<string>? Options { get; set; }

    /// <summary>组内二级分节标签（纯展示层）。前端把同一 Subsection 的字段聚合成可折叠小节。</summary>
    public string? Subsection { get; set; }

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

    /// <summary>
    /// 当前用户是否可修改本组（持有 <c>{group}.settings.{slug}.update</c> 码或超管）。
    /// 为 false 时前端渲染为只读（用户有 view 权但无 update 权）。
    /// </summary>
    public bool CanEdit { get; set; } = true;

    public List<SettingsCenterFieldDto> Fields { get; set; } = new();
}
