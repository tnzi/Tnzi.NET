namespace Tnzi.System.Entities;

/// <summary>
/// 配置值类型
/// </summary>
public enum SettingValueType
{
    /// <summary>
    /// 字符串
    /// </summary>
    String = 0,

    /// <summary>
    /// 整数
    /// </summary>
    Integer = 1,

    /// <summary>
    /// 布尔值
    /// </summary>
    Boolean = 2,

    /// <summary>
    /// JSON
    /// </summary>
    Json = 3
}

/// <summary>
/// 系统配置实体（key-value 存储）
/// 支持扩展字段和动态配置
/// </summary>
public class Setting : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 配置键（唯一）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 配置值
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 配置描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 获取或设置 配置分组（用于分类管理）
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// 获取或设置 是否系统配置（系统配置不可删除）
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置 值类型
    /// </summary>
    public SettingValueType ValueType { get; set; }
}
