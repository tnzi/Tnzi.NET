namespace Tnzi.System.Dtos;

/// <summary>
/// 创建菜单
/// </summary>
public class CreateMenuDto
{
    public Guid? ParentId { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Icon { get; set; }

    [MaxLength(200)]
    public string? Path { get; set; }

    [MaxLength(200)]
    public string? Component { get; set; }

    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }

    [MaxLength(100)]
    public string? Permission { get; set; }

    public MenuType Type { get; set; }
}

/// <summary>
/// 更新菜单
/// </summary>
public class UpdateMenuDto
{
    public Guid? ParentId { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Icon { get; set; }

    [MaxLength(200)]
    public string? Path { get; set; }

    [MaxLength(200)]
    public string? Component { get; set; }

    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }

    [MaxLength(100)]
    public string? Permission { get; set; }

    public MenuType Type { get; set; }
}

/// <summary>
/// 菜单输出
/// </summary>
public class MenuDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Path { get; set; }
    public string? Component { get; set; }
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public string? Permission { get; set; }
    public MenuType Type { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 菜单树节点
/// </summary>
public class MenuTreeNode
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Path { get; set; }
    public string? Component { get; set; }
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public string? Permission { get; set; }
    public MenuType Type { get; set; }
    public List<MenuTreeNode> Children { get; set; } = new();
}

/// <summary>
/// 菜单排序
/// </summary>
public class MenuOrderDto
{
    public Guid Id { get; set; }
    public int SortOrder { get; set; }
}
