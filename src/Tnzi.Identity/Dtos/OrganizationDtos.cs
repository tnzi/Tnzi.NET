


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

    /// <summary>
    /// 获取或设置 子组织列表
    /// </summary>
    public List<OrganizationDto> Children { get; set; } = new();
}

/// <summary>
/// 创建组织DTO
/// </summary>
public class CreateOrganizationDto
{
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
}

/// <summary>
/// 更新组织DTO
/// </summary>
public class UpdateOrganizationDto
{
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
/// 组织统计DTO
/// </summary>
public class OrganizationStatisticsDto
{
    public int DirectChildren { get; set; }
    public int TotalChildren { get; set; }
    public int DirectUsers { get; set; }
    public int TotalUsers { get; set; }
}

