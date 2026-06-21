namespace Tnzi.Settings;

/// <summary>类级：含可热设置字段的 Options 打此特性，提供 admin 左导航分组元数据。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuntimeSettingGroupAttribute : Attribute
{
    public string? Key { get; init; }
    public string? Module { get; init; }
    public string? DisplayName { get; init; }
    public string? Icon { get; init; }
    public int Order { get; init; }
    public string? I18nKey { get; init; }
}
