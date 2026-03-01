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
/// Localization resource DTO
/// </summary>
public class ResourceDto
{
    /// <summary>
    /// Culture name
    /// </summary>
    public string Culture { get; set; } = null!;

    /// <summary>
    /// Resource key-value pairs
    /// </summary>
    public Dictionary<string, string> Resources { get; set; } = new();
}

/// <summary>
/// Missing translation entry DTO
/// </summary>
public class MissingTranslationDto
{
    /// <summary>
    /// Culture name (e.g., "en", "zh-CN")
    /// </summary>
    public string Culture { get; set; } = null!;

    /// <summary>
    /// The missing translation key
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Number of times this key was requested
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// First time this missing key was accessed
    /// </summary>
    public DateTime FirstAccessTime { get; set; }

    /// <summary>
    /// Last time this missing key was accessed
    /// </summary>
    public DateTime LastAccessTime { get; set; }
}

/// <summary>
/// Per-culture missing translation statistics
/// </summary>
public class CultureMissingCountDto
{
    /// <summary>
    /// Culture name
    /// </summary>
    public string Culture { get; set; } = null!;

    /// <summary>
    /// Number of missing keys for this culture
    /// </summary>
    public int MissingKeyCount { get; set; }

    /// <summary>
    /// Total access count across all missing keys
    /// </summary>
    public int TotalAccessCount { get; set; }
}

/// <summary>
/// Summary of all missing translations
/// </summary>
public class MissingTranslationSummaryDto
{
    /// <summary>
    /// Total number of distinct missing keys
    /// </summary>
    public int TotalMissingKeys { get; set; }

    /// <summary>
    /// Total access count across all missing keys
    /// </summary>
    public int TotalAccessCount { get; set; }

    /// <summary>
    /// Number of cultures with missing translations
    /// </summary>
    public int AffectedCultureCount { get; set; }

    /// <summary>
    /// Per-culture breakdown
    /// </summary>
    public List<CultureMissingCountDto> CultureBreakdown { get; set; } = new();
}
