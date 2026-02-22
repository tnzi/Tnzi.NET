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
public class RoleUpdatedEvent : EventBase
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
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
