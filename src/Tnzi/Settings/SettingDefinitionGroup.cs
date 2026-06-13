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

    public required IReadOnlyList<SettingFieldDefinition> Fields { get; init; }
}
