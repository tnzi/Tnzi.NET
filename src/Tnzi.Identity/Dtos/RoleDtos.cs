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
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }
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
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }
}
