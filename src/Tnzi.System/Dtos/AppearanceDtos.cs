namespace Tnzi.System.Dtos;

/// <summary>
/// 全局管理端主题配置（外观）。
/// Theme 是前端拥有 schema 的不透明 JSON 快照（布局模式、Tab 栏显隐、配色等），
/// 后端只负责存储、鉴权与尺寸校验；null 表示尚未配置，客户端回退本地默认值。
/// </summary>
public class AdminThemeDto
{
    /// <summary>Opaque theme snapshot document; null when no global theme has been saved.</summary>
    public JsonElement? Theme { get; set; }

    /// <summary>Last save time (UTC); null when no global theme has been saved.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 保存全局管理端主题的请求。
/// </summary>
public class SaveAdminThemeDto
{
    /// <summary>Theme snapshot document; must be a JSON object.</summary>
    public JsonElement Theme { get; set; }
}
