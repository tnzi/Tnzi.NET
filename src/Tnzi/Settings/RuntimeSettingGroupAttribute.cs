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

    /// <summary>
    /// 该配置组派生的权限码归属的权限组 name（如 "chat"、"ai"、"system"）。
    /// 缺省 = <see cref="Module"/> 规范化（小写、仅字母数字）。当模块没有与自身
    /// 同名的权限组时（如 AspNetCore 的 "Web" 配置归属到 "system" 组），显式指定。
    /// 详见 <c>SettingsPermissionNaming</c>。
    /// </summary>
    public string? PermissionGroup { get; init; }

    /// <summary>
    /// 权限码的实体面段（`{permGroup}.settings.{slug}.view/update` 中的 slug）。
    /// 缺省从 <see cref="Key"/> 去掉归属组前缀后 camelCase 派生。仅当自动派生不合意时覆盖。
    /// </summary>
    public string? PermissionSlug { get; init; }
}
