namespace Tnzi.Localization.Dtos;

/// <summary>
/// 文化信息 DTO
/// </summary>
public class CultureDto
{
    /// <summary>
    /// 文化名称（如 "en", "zh-CN"）
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 显示名称（如 "English", "中文(中国)"）
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// 是否为默认文化
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// 支持的文化列表 DTO
/// </summary>
public class CultureListDto
{
    /// <summary>
    /// 文化列表
    /// </summary>
    public List<CultureDto> Cultures { get; set; } = new();
}

/// <summary>
/// 本地化资源 DTO
/// </summary>
public class ResourceDto
{
    /// <summary>
    /// 文化名称
    /// </summary>
    public string Culture { get; set; } = null!;

    /// <summary>
    /// 资源键值对
    /// </summary>
    public Dictionary<string, string> Resources { get; set; } = new();
}
