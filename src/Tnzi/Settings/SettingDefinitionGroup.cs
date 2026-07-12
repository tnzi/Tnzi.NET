namespace Tnzi.Settings;

/// <summary>
/// 配置中心分组定义。模块通过 ISettingDefinitionProvider 注册；
/// 只有被加载的模块会注册 provider，因此「模块启用判断」天然成立。
/// </summary>
[ExperimentalApi(Reason = "Settings center contract is new and may evolve")]
public sealed class SettingDefinitionGroup
{
    /// <summary>全局唯一组键（kebab-case，如 "ai-budget"）。也用作 Setting 表行的 Group。</summary>
    public required string Key { get; init; }

    /// <summary>
    /// 左侧菜单分组标签，同名组在前端聚合显示。约定使用模块英文显示名
    /// （如 "System"、"AI"），不要用 C# 模块类名（如 "AIModule"）。
    /// </summary>
    public required string ModuleName { get; init; }

    public required string DisplayName { get; init; }
    public string? I18nKey { get; init; }
    public string? Description { get; init; }

    /// <summary>Iconify 图标名（如 "mdi:web"）。</summary>
    public string? Icon { get; init; }

    /// <summary>同一菜单分组内的排列顺序（数值越小越靠前，默认 0）。</summary>
    public int Order { get; init; }

    /// <summary>
    /// 该组派生的配置权限码归属的权限组 name（如 "chat"、"ai"、"system"）。
    /// 缺省 = <see cref="ModuleName"/> 规范化。详见 <c>SettingsPermissionNaming</c>。
    /// </summary>
    public string? PermissionGroup { get; init; }

    /// <summary>
    /// 权限码的实体面段（`{permGroup}.settings.{slug}.view/update` 中的 slug）。
    /// 缺省从 <see cref="Key"/> 去归属组前缀后 camelCase 派生。
    /// </summary>
    public string? PermissionSlug { get; init; }

    /// <summary>
    /// 该组字段所属的 Options 类型（属性驱动定义时由提取器填充；GROUP MERGE 的组
    /// 携带全部贡献者类型）。配置中心保存前用它们反查已注册的 IValidateOptions&lt;T&gt;，
    /// 对「合并候选值绑出的实例」跑与运行时绑定完全相同的验证 — 防止字段级校验放行、
    /// reload 后绑定/验证抛异常。手写 provider 可为 null（跳过 validator 预检）。
    /// </summary>
    public IReadOnlyList<Type>? OptionsTypes { get; init; }

    public required IReadOnlyList<SettingFieldDefinition> Fields { get; init; }
}
