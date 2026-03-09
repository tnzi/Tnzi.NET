


namespace Tnzi.Identity.Dtos;

/// <summary>
/// 组织DTO
/// </summary>
public class OrganizationDto
{
    /// <summary>
    /// 获取或设置 组织ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置 组织名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 组织代码
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 获取或设置 描述
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 获取或设置 父组织ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 获取或设置 层级路径
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// 获取或设置 层级深度
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 获取或设置 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 组织树节点DTO，继承 OrganizationDto 并增加 Children 递归属性
/// </summary>
public class OrganizationTreeNodeDto : OrganizationDto
{
    /// <summary>
    /// 获取或设置 子组织列表
    /// </summary>
    public List<OrganizationTreeNodeDto> Children { get; set; } = new();
}

/// <summary>
/// 组织树平面投影DTO
/// 仅用于 ProjectTo 查询，避免递归 Children 在投影阶段形成循环引用
/// </summary>
public class OrganizationTreeItemDto : OrganizationDto
{
}

/// <summary>
/// 创建组织DTO
/// </summary>
public class CreateOrganizationDto
{
    /// <summary>
    /// 获取或设置 组织名称
    /// </summary>
    [Required]
    public string Name { get; set; } = null!;

    /// <summary>
    /// 获取或设置 组织代码
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 获取或设置 描述
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 获取或设置 父组织ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 更新组织DTO
/// </summary>
public class UpdateOrganizationDto
{
    /// <summary>
    /// 获取或设置 组织名称
    /// </summary>
    [Required]
    public string Name { get; set; } = null!;

    /// <summary>
    /// 获取或设置 组织代码
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 获取或设置 描述
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }
}

/// <summary>
/// 移动组织DTO
/// </summary>
public class MoveOrganizationDto
{
    public Guid? NewParentId { get; set; }
}

/// <summary>
/// 批量更新组织DTO
/// </summary>
public class UpdateOrganizationBatchDto
{
    public Guid Id { get; set; }
    public UpdateOrganizationDto Dto { get; set; } = null!;
}

/// <summary>
/// 更新排序值DTO
/// </summary>
public class UpdateSortOrderDto
{
    /// <summary>
    /// 新的排序值
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 批量排序更新项DTO
/// </summary>
public class BatchSortOrderItemDto
{
    /// <summary>
    /// 组织ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 新的排序值
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 组织统计DTO
/// </summary>
public class OrganizationStatisticsDto
{
    public int DirectChildren { get; set; }
    public int TotalChildren { get; set; }
    public int DirectUsers { get; set; }
    public int TotalUsers { get; set; }
}

