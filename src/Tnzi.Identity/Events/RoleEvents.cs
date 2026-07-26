namespace Tnzi.Identity.Events;

/// <summary>
/// 角色创建事件
/// </summary>
public class RoleCreatedEvent : EventBase
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 角色更新事件
/// </summary>
/// <remarks>
/// <see cref="PreviousName"/> is non-null only when the role was renamed
/// in this update - lets audit / authorization consumers detect renames
/// without a separate <c>RoleRenamedEvent</c>. Name comparison is
/// case-insensitive (consistent with the rest of the framework's role
/// matching), so changing case only does NOT set PreviousName.
/// </remarks>
public class RoleUpdatedEvent : EventBase
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    /// <summary>
    /// The role's name before this update. <c>null</c> when the update
    /// didn't change the name (description-only / IsDefault flip / etc.).
    /// </summary>
    public string? PreviousName { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedTime { get; set; }
}

/// <summary>
/// 角色删除事件
/// </summary>
public class RoleDeletedEvent : EventBase
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public DateTime DeletedTime { get; set; }
}
