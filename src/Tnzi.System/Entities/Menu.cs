
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
public class Menu : MultiTenantAuditedEntity<Guid>
{
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Associates this row with a front-end route by its name (e.g. "identity.users").
    /// <para>
    /// When set, the row <b>overrides</b> that route's derived menu entry
    /// (title / icon / order / visibility) under the front-end <c>'merge'</c> menu
    /// source — the route table stays the source of truth for which pages exist,
    /// this table just lets operators retitle / reorder / hide / re-icon them
    /// without a redeploy.
    /// </para>
    /// <para>
    /// When null, the row is a stand-alone <b>custom</b> navigation node (e.g. an
    /// external link via <see cref="Path"/>) the route table doesn't know about.
    /// </para>
    /// </summary>
    public string? MenuKey { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Path { get; set; }
    public string? Component { get; set; }
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public string? Permission { get; set; }
    public MenuType Type { get; set; }
}
