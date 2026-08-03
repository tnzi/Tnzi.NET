namespace Tnzi.System.Dtos;

/// <summary>
/// 一份全局外观（主题）快照。
/// <para>
/// Theme 是前端拥有 schema 的不透明 JSON 文档（布局模式、Tab 栏显隐、配色等），
/// 后端只负责存储、鉴权与尺寸校验；null 表示该 scope 尚未配置，客户端回退本地默认值。
/// </para>
/// <para>
/// 「不透明」正是这套机制能同时服务多个前端产品的原因：管理端与对话端各有各的外壳字段
/// （前者有侧栏宽度与 Tab 栏，后者有会话列宽与气泡圆角），后端不需要认识任何一种，
/// 只需按 scope 分别保管。见 <see cref="Services.IAppearanceService"/>。
/// </para>
/// </summary>
public class ThemeSnapshotDto
{
    /// <summary>Opaque theme snapshot document; null when this scope has no saved theme.</summary>
    public JsonElement? Theme { get; set; }

    /// <summary>Last save time (UTC); null when this scope has no saved theme.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 保存一份全局外观快照的请求。
/// </summary>
public class SaveThemeSnapshotDto
{
    /// <summary>Theme snapshot document; must be a JSON object.</summary>
    public JsonElement Theme { get; set; }
}
