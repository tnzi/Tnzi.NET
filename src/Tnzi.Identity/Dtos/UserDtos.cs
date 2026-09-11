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

    /// <summary>
    /// 这个账号还欠着的事（未接受邀请 / 必须改密 / …）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsLockedOut"/> 分开呈现：未接受邀请的账号在库里两者同时为真，
    /// 但含义完全不同（「新人还没来」vs「这个人被停用了」），
    /// 管理台把它们混成一种状态就没法用了。
    /// </remarks>
    public PendingUserActions PendingActions { get; set; }

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
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
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
    public string UserName { get; set; } = null!;

    /// <summary>
    /// 初始密码。<b>可以为空</b>，此时创建出的账号没有密码。
    /// </summary>
    /// <remarks>
    /// ★ 从必填改为可空，是为了让「管理员开号 + 本人来设密码」这条路走得通
    /// （<see cref="Services.IInvitationService"/>）。让邀请自己拼一个 <c>User</c> 塞进库
    /// 才是危险的做法：那样会绕开这条路径上的重名校验、密码策略与注册事件，
    /// 而 <c>CreateAsync</c> 是它们唯一的共同出口。
    /// <para>
    /// 留空创建出的账号<b>登录不进去</b>：没有 <c>PasswordHash</c> 则密码登录必失败，
    /// 而邀请路径还会另外置上 <see cref="PendingUserActions.InvitationPending"/>，
    /// 由守卫挡住其余全部签发路径。
    /// </para>
    /// </remarks>
    public string? Password { get; set; }

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

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
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
/// 用户本人可自助修改的资料字段。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意不含 <c>RoleIds</c> 与 <c>OrganizationId</c></b>，这是本类型存在的全部理由。
/// 自助端点（<c>PUT /api/users/profile</c>）此前直接收 <see cref="UpdateUserDto"/>，
/// 而那个 DTO 是给<b>管理端</b>用的、带这两个字段，于是任何已登录用户 PUT 一个
/// <c>{"roleIds":["&lt;管理员角色id&gt;"]}</c> 就能给自己授予任意角色、把自己挪进任意组织。
/// </para>
/// <para>
/// 修在 DTO 而不是在控制器里把字段置空：<c>UserService.UpdateAsync</c> 无从知道调用者
/// 是管理员还是本人，任何「调用前记得清掉」的约定都会在下一个调用点被忘掉。
/// 字段在绑定层不存在，越权就不是「被拦住」而是<b>无法表达</b>。
/// </para>
/// <para>
/// 新增自助字段加在这里；新增仅管理员可改的字段加在 <see cref="UpdateUserDto"/>。
/// 两者的字段集由 <c>UserProfileDtoShapeTests</c> 守着。
/// </para>
/// </remarks>
public class UpdateProfileDto
{
    // ★★★ 这里**刻意没有** Email / PhoneNumber。
    //
    // 它们曾经在，于是 PUT /users/profile 可以直接改掉联系方式 —— 而旁边就摆着
    // 一整套换绑流程（change-email/send-code → change-email/confirm，验证码发到**新地址**）。
    // 一个端点能绕过另一个端点的验证仪式，那套仪式就等于不存在。
    // 更糟的是直接赋值不清确认位：换完地址仍带着「已验证」的章，而那一位是框架对外的断言。
    //
    // 要改联系方式就走换绑端点；管理员要直接改，走 admin/users/{id} + confirm-contact。

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    [FileField]
    public Guid? AvatarId { get; set; }
    public int? Gender { get; set; }
    public DateTime? Birthday { get; set; }
    public string? Bio { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
}

/// <summary>
/// 管理端「确认联系方式」入参。两项都为 <c>null</c> 时拒绝（没有要办的事）。
/// </summary>
public class ConfirmContactDto
{
    /// <summary>把邮箱标记为已确认 / 未确认；<c>null</c> 表示不动这一位。</summary>
    public bool? ConfirmEmail { get; set; }

    /// <summary>把手机号标记为已确认 / 未确认；<c>null</c> 表示不动这一位。</summary>
    public bool? ConfirmPhoneNumber { get; set; }
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

    /// <summary>
    /// 按待办筛选：命中<b>任意一位</b>即返回（管理台的「谁还没接受邀请」就是这一条）。
    /// </summary>
    public PendingUserActions? PendingAction { get; set; }

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

    /// <summary>
    /// 要求本人下次登录时必须修改这个密码。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// ★ <b>默认开启</b>：管理员设的密码经过了一条带外通道（口头、聊天工具、便签）才到本人手里，
    /// 那条通道上谁都可能看见。让它默认是临时的，才对得起管理端上「登录后需重新修改」那句提示 ——
    /// 在此之前那句话是假的，框架根本没有这个机制。
    /// 明确不需要时（比如系统账号、自动化用途）传 false。
    /// </remarks>
    public bool RequireChangeOnNextLogin { get; set; } = true;
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
    public IEnumerable<Guid> RoleIds { get; set; } = null!;
}

/// <summary>
/// 移除角色DTO
/// </summary>
public class RemoveRolesDto
{
    public IEnumerable<Guid> RoleIds { get; set; } = null!;
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

/// <summary>
/// 账户停用请求DTO
/// </summary>
public class DeactivateAccountDto
{
    /// <summary>
    /// 停用原因
    /// </summary>
    [StringLength(500)]
    public string? Reason { get; set; }
}

/// <summary>
/// 用户个人数据导出DTO（GDPR 合规）
/// </summary>
public class PersonalDataExportDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 组织名称
    /// </summary>
    public string? OrganizationName { get; set; }

    /// <summary>
    /// 角色列表
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime? LastModificationTime { get; set; }

    /// <summary>
    /// 是否启用双因素认证
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>
    /// 邮箱是否已确认
    /// </summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>
    /// 手机号是否已确认
    /// </summary>
    public bool PhoneNumberConfirmed { get; set; }

    /// <summary>
    /// 关联的外部登录提供者
    /// </summary>
    public List<string> LinkedProviders { get; set; } = new();

    /// <summary>
    /// 导出时间
    /// </summary>
    public DateTime ExportedAt { get; set; }
}

