namespace Tnzi.Identity.Dtos;

/// <summary>
/// 租户信息 DTO
/// </summary>
public class TenantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public string? Remark { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 租户查询 DTO
/// </summary>
public class TenantQueryDto : PagedQueryDto
{
    public string? Keyword { get; set; }
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 创建租户 DTO
/// </summary>
public class CreateTenantDto
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = null!;

    [StringLength(64)]
    public string? Code { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime? ExpiredAt { get; set; }

    [StringLength(500)]
    public string? Remark { get; set; }
}

/// <summary>
/// 更新租户 DTO
/// </summary>
public class UpdateTenantDto
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = null!;

    [StringLength(64)]
    public string? Code { get; set; }

    public bool IsEnabled { get; set; }
    public DateTime? ExpiredAt { get; set; }

    [StringLength(500)]
    public string? Remark { get; set; }
}
