namespace Tnzi.Identity.Events;

/// <summary>
/// 用户注册事件
/// </summary>
public class UserRegisteredEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime RegistrationTime { get; set; }
}

/// <summary>
/// 用户登录事件
/// </summary>
public class UserLoggedInEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? LoginProvider { get; set; }
}

/// <summary>
/// 用户登出事件
/// </summary>
public class UserLoggedOutEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime LogoutTime { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// 用户密码修改事件
/// </summary>
public class UserPasswordChangedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime ChangedTime { get; set; }
    public Guid? ChangedBy { get; set; }
}

/// <summary>
/// 密码重置请求事件（用于发送重置邮件）
/// </summary>
public class PasswordResetRequestedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ResetToken { get; set; } = string.Empty;
    public DateTime RequestTime { get; set; }
    public string? FrontendUrl { get; set; }
    public string? SiteName { get; set; }
}

/// <summary>
/// 用户密码重置事件
/// </summary>
public class UserPasswordResetEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime ResetTime { get; set; }
    public Guid? ResetBy { get; set; }
    public bool IsSelfReset { get; set; }
}

/// <summary>
/// 用户锁定事件
/// </summary>
public class UserLockedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime LockedTime { get; set; }
    public Guid? LockedBy { get; set; }
    public string? LockReason { get; set; }
}

/// <summary>
/// 用户解锁事件
/// </summary>
public class UserUnlockedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime UnlockedTime { get; set; }
    public Guid? UnlockedBy { get; set; }
}

/// <summary>
/// 用户启用事件
/// </summary>
public class UserEnabledEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime EnabledTime { get; set; }
    public Guid? EnabledBy { get; set; }
}

/// <summary>
/// 用户禁用事件
/// </summary>
public class UserDisabledEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime DisabledTime { get; set; }
    public Guid? DisabledBy { get; set; }
    public string? DisableReason { get; set; }
}

/// <summary>
/// 用户信息更新事件
/// </summary>
public class UserUpdatedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public List<string> UpdatedFields { get; set; } = new();
    public DateTime LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
}

/// <summary>
/// 双因素认证验证码发送事件
/// </summary>
public class TwoFactorCodeSentEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Email" 或 "Sms"
    public string Address { get; set; } = string.Empty; // 邮箱地址或手机号
    [JsonIgnore] // 防止验证码明文泄露到日志/消息队列
    public string Code { get; set; } = string.Empty; // 验证码
    public DateTime ExpiresAt { get; set; }
    public int ExpirationMinutes { get; set; }
}

/// <summary>
/// 用户登录失败事件
/// </summary>
public class UserLoginFailedEvent : EventBase
{
    /// <summary>
    /// 用户ID（可能为 null，用户不存在时）
    /// </summary>
    public Guid? UserId { get; set; }
    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }
    /// <summary>
    /// 失败原因（"User not found", "Invalid password", "Invalid 2FA code" 等）
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;
    /// <summary>
    /// IP地址
    /// </summary>
    public string? IpAddress { get; set; }
    /// <summary>
    /// UserAgent
    /// </summary>
    public string? UserAgent { get; set; }
    /// <summary>
    /// 失败时间
    /// </summary>
    public DateTime FailureTime { get; set; }
}

/// <summary>
/// 用户邮箱确认事件
/// </summary>
public class UserEmailConfirmedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime ConfirmationTime { get; set; }
}

/// <summary>
/// 邮箱确认邮件重发事件
/// 用于重发邮箱确认邮件（包含确认链接）
/// </summary>
public class EmailConfirmationResentEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime ResentTime { get; set; }
}

/// <summary>
/// 快速注册验证码发送事件
/// 注意：由应用层订阅此事件，调用 Notification 模块发送邮件/短信
/// </summary>
public class QuickRegisterCodeSentEvent : EventBase
{
    /// <summary>
    /// 验证码
    /// </summary>
    [JsonIgnore]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 发送类型（Email 或 Sms）
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 目标地址（邮箱或手机号）
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 过期分钟数
    /// </summary>
    public int ExpirationMinutes { get; set; }
}

/// <summary>
/// 快速注册完成事件
/// </summary>
public class QuickRegisterCompletedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime RegistrationTime { get; set; }
    public bool RequirePasswordSetup { get; set; }
}

/// <summary>
/// 用户邮箱变更事件
/// </summary>
public class UserEmailChangedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? OldEmail { get; set; }
    public string NewEmail { get; set; } = string.Empty;
    public DateTime ChangedTime { get; set; }
}

/// <summary>
/// 用户手机号变更事件
/// </summary>
public class UserPhoneChangedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? OldPhoneNumber { get; set; }
    public string NewPhoneNumber { get; set; } = string.Empty;
    public DateTime ChangedTime { get; set; }
}

/// <summary>
/// 异常登录检测事件
/// 注意：由应用层订阅此事件，发送安全通知
/// </summary>
public class AbnormalLoginDetectedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime DetectedTime { get; set; }

    /// <summary>
    /// 异常类型列表
    /// </summary>
    public List<string> AbnormalTypes { get; set; } = new();

    /// <summary>
    /// 风险等级（0-100）
    /// </summary>
    public int RiskLevel { get; set; }

    /// <summary>
    /// 详细描述
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// 建议操作
    /// </summary>
    public string RecommendedAction { get; set; } = string.Empty;
}

/// <summary>
/// 用户账户自助停用事件
/// </summary>
public class UserAccountDeactivatedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime DeactivatedTime { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 用户账户自助删除事件（GDPR 合规）
/// </summary>
public class UserAccountDeletedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime DeletedTime { get; set; }
}

/// <summary>
/// 用户个人数据导出事件（GDPR 审计）
/// </summary>
public class PersonalDataExportedEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime ExportedTime { get; set; }
}

/// <summary>
/// 用户-角色映射变更事件。
/// </summary>
/// <remarks>
/// Published whenever a user's role membership changes (assign / remove /
/// create-with-roles / delete). The Authorization module subscribes to this
/// to invalidate its <c>FunctionAuthCache</c> per-user entry - without this
/// signal the cache (30 min TTL) would let a revoked user retain elevated
/// permissions for up to a TTL window.
///
/// <para>
/// <b>Semantics</b>: <c>AddedRoleIds</c> + <c>RemovedRoleIds</c> describe the
/// delta, not the final state. Consumers must NOT treat either field as
/// "user's complete role set after the change". <c>ChangeType</c> documents
/// intent for audit consumers.
/// </para>
/// </remarks>
public class UserRolesChangedEvent : EventBase
{
    /// <summary>Affected user. Always set.</summary>
    public Guid UserId { get; set; }

    /// <summary>User name at time of change (denormalised for audit logs).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Role IDs newly granted to the user. Empty for pure-removal.</summary>
    public List<Guid> AddedRoleIds { get; set; } = new();

    /// <summary>Role IDs removed from the user. Empty for pure-addition.</summary>
    public List<Guid> RemovedRoleIds { get; set; } = new();

    /// <summary>Why this change happened (audit context).</summary>
    public UserRolesChangeType ChangeType { get; set; }

    /// <summary>UTC timestamp the change committed.</summary>
    public DateTime ChangedTime { get; set; }

    /// <summary>Admin who triggered the change (null for self-service flows).</summary>
    public Guid? ChangedBy { get; set; }
}

/// <summary>
/// Why a <see cref="UserRolesChangedEvent"/> was raised. Used by audit
/// consumers; cache-invalidation consumers can ignore this field.
/// </summary>
public enum UserRolesChangeType
{
    /// <summary>Admin explicitly assigned one or more roles.</summary>
    Assigned,
    /// <summary>Admin explicitly removed one or more roles.</summary>
    Removed,
    /// <summary>User was created with an initial role set.</summary>
    CreatedWithRoles,
    /// <summary>User was deleted - all role memberships dropped.</summary>
    UserDeleted,
}

