namespace Tnzi.Template.Models;

/// <summary>
/// 通用布局信息（独立于业务实体）
/// </summary>
public class LayoutInfo
{
    /// <summary>
    /// 布局名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 布局分类（如 Email/SMS/Push 或模块自定义类别）
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 布局内容模板
    /// </summary>
    public string LayoutContent { get; set; } = string.Empty;

    /// <summary>
    /// 是否默认布局
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 元数据（可扩展）
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 文件路径（如果来源于文件系统）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 文件最后修改时间（如果来源于文件系统）
    /// </summary>
    public DateTime? LastModified { get; set; }
}

