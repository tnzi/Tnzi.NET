namespace Tnzi.System.Dtos;

/// <summary>
/// 创建菜单
/// </summary>
public class CreateMenuDto
{
    public Guid? ParentId { get; set; }

    /// <summary>Route name to override (e.g. "identity.users"); null for a custom node.</summary>
    [MaxLength(100)]
    public string? MenuKey { get; set; }

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

    /// <summary>Route name to override (e.g. "identity.users"); null for a custom node.</summary>
    [MaxLength(100)]
    public string? MenuKey { get; set; }

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
    public string? MenuKey { get; set; }
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
    public string? MenuKey { get; set; }
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

/// <summary>
/// 菜单批量种子结果。Seed 按 MenuKey upsert：不存在则插入，已存在则跳过
/// （保护运营在 admin 里对该行做过的覆盖修改）。
/// </summary>
public class MenuSeedResultDto
{
    /// <summary>新插入的行数</summary>
    public int Inserted { get; set; }

    /// <summary>已存在被跳过的行数（MenuKey 已有行）</summary>
    public int Skipped { get; set; }
}
