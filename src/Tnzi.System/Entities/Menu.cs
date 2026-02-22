
namespace Tnzi.System.Entities;

/// <summary>
/// 菜单类型
/// </summary>
public enum MenuType
{
    /// <summary>
    /// 目录
    /// </summary>
    Directory = 0,

    /// <summary>
    /// 菜单
    /// </summary>
    Menu = 1,

    /// <summary>
    /// 按钮
    /// </summary>
    Button = 2
}

/// <summary>
/// 菜单实体
/// </summary>
public class Menu : FullAuditedEntity<Guid>
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Path { get; set; }
    public string? Component { get; set; }
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public string? Permission { get; set; }
    public MenuType Type { get; set; }
}
