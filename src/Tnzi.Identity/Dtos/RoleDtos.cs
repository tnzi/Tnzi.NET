namespace Tnzi.Identity.Dtos;

/// <summary>
/// 角色信息DTO
/// </summary>
public class RoleDto
{
    /// <summary>
    /// 角色ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 角色名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 规范化角色名称
    /// </summary>
    public string? NormalizedName { get; set; }

    /// <summary>
    /// 角色描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否为系统内置角色
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 是否为默认角色
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 角色详情DTO（包含用户计数）
/// </summary>
public class RoleDetailDto : RoleDto
{
    /// <summary>
    /// 角色下的用户数量
    /// </summary>
    public int UserCount { get; set; }
}

/// <summary>
/// 创建角色DTO
/// </summary>
public class CreateRoleDto
{
    /// <summary>
    /// 角色名称
    /// </summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// 角色描述
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 是否为默认角色（新注册用户自动分配）
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// 更新角色DTO
/// </summary>
public class UpdateRoleDto
{
    /// <summary>
    /// 角色名称
    /// </summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// 角色描述
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 是否为默认角色
    /// </summary>
    public bool? IsDefault { get; set; }
}

/// <summary>
/// 角色列表查询DTO
/// </summary>
public class RoleListQueryDto : PagedQueryDto
{
    /// <summary>
    /// 关键词搜索（名称、描述）
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 是否为系统角色
    /// </summary>
    public bool? IsSystem { get; set; }

    /// <summary>
    /// 是否为默认角色
    /// </summary>
    public bool? IsDefault { get; set; }
}
