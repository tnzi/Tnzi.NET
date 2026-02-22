namespace Tnzi.Identity.Dtos;

/// <summary>
/// 用户列表项DTO（不包含 UserDetail 字段，用于列表查询）
/// </summary>
public class UserListItemDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public bool IsLockedOut { get; set; }
    public bool IsEmailConfirmed { get; set; }
    public bool IsPhoneNumberConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// 用户信息DTO（包含 UserDetail 字段，用于单个用户详情查询）
/// </summary>
public class UserDto : UserListItemDto
{
    public string? Nickname { get; set; }
    public string? Avatar { get; set; }
    public Guid? AvatarId { get; set; }
    public int Gender { get; set; }
    public DateTime? Birthday { get; set; }
    public string? Bio { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
}

/// <summary>
/// 创建用户DTO
/// </summary>
public class CreateUserDto
{
    [Required]
    public string UserName { get; set; } = string.Empty;
    
    [Required]
    public string Password { get; set; } = string.Empty;
    
    [EmailAddress]
    public string? Email { get; set; }
    
    public string? PhoneNumber { get; set; }
    
    public string? Nickname { get; set; }
    
    public Guid? OrganizationId { get; set; }
    
    public List<Guid>? RoleIds { get; set; }
}

/// <summary>
/// 更新用户DTO
/// </summary>
public class UpdateUserDto
{
    public string? Email { get; set; }
    
    public string? PhoneNumber { get; set; }
    
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    [FileField]
    public Guid? AvatarId { get; set; }
    public int? Gender { get; set; }
    public DateTime? Birthday { get; set; }
    public string? Bio { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public Guid? OrganizationId { get; set; }
    
    public List<Guid>? RoleIds { get; set; }
}

/// <summary>
/// 用户列表查询DTO
/// </summary>
public class UserListQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;
    
    public string? Keyword { get; set; }
    
    public Guid? OrganizationId { get; set; }
    
    public Guid? RoleId { get; set; }
    
    public bool? IsLockedOut { get; set; }
    
    public bool? IsEmailConfirmed { get; set; }
    
    public string? SortBy { get; set; }
    
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// 用户统计DTO
/// </summary>
public class UserStatisticsDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int LockedUsers { get; set; }
    public int UsersByOrganization { get; set; }
    public int UsersByRole { get; set; }
    public int RecentRegistrations { get; set; } // 最近7天注册数
}

/// <summary>
/// 锁定用户DTO
/// </summary>
public class LockUserDto
{
    public DateTimeOffset? LockoutEnd { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 修改密码DTO
/// </summary>
public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = null!;
    [Required]
    public string NewPassword { get; set; } = null!;
}

/// <summary>
/// 管理员重置密码DTO
/// </summary>
public class ResetPasswordByAdminDto
{
    [Required]
    public string NewPassword { get; set; } = null!;
}

/// <summary>
/// 批量更新用户DTO
/// </summary>
public class UpdateUserBatchDto
{
    public Guid Id { get; set; }
    public UpdateUserDto Dto { get; set; } = null!;
}

/// <summary>
/// 分配组织DTO
/// </summary>
public class AssignOrganizationDto
{
    public Guid OrganizationId { get; set; }
}

/// <summary>
/// 分配角色DTO
/// </summary>
public class AssignRolesDto
{
    public IEnumerable<Guid> RoleIds { get; set; } = new List<Guid>();
}

/// <summary>
/// 移除角色DTO
/// </summary>
public class RemoveRolesDto
{
    public IEnumerable<Guid> RoleIds { get; set; } = new List<Guid>();
}

/// <summary>
/// 修改邮箱DTO
/// </summary>
public class ChangeEmailDto
{
    /// <summary>
    /// 新邮箱地址
    /// </summary>
    [Required]
    [EmailAddress]
    public string NewEmail { get; set; } = null!;

    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; } = null!;
}

/// <summary>
/// 修改手机号DTO
/// </summary>
public class ChangePhoneNumberDto
{
    /// <summary>
    /// 新手机号
    /// </summary>
    [Required]
    public string NewPhoneNumber { get; set; } = null!;

    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; } = null!;
}

/// <summary>
/// 发送变更验证码DTO
/// </summary>
public class SendChangeVerificationCodeDto
{
    /// <summary>
    /// 新地址（邮箱或手机号）
    /// </summary>
    [Required]
    public string NewAddress { get; set; } = null!;
}

/// <summary>
/// 登录历史查询DTO
/// </summary>
public class LoginHistoryQueryDto
{
    /// <summary>
    /// 开始日期
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 结束日期
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool? IsSuccess { get; set; }
}

