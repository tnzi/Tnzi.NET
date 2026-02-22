namespace Tnzi.Identity.Events;

/// <summary>
/// 组织创建事件
/// </summary>
public class OrganizationCreatedEvent : EventBase
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid? CreatorId { get; set; }
}

/// <summary>
/// 组织更新事件
/// </summary>
public class OrganizationUpdatedEvent : EventBase
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public List<string> UpdatedFields { get; set; } = new();
    public Guid? LastModifierId { get; set; }
}

/// <summary>
/// 组织删除事件
/// </summary>
public class OrganizationDeletedEvent : EventBase
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid? DeletedBy { get; set; }
}

/// <summary>
/// 用户分配到组织事件
/// </summary>
public class UserAssignedToOrganizationEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid? AssignedBy { get; set; }
}

/// <summary>
/// 用户从组织移除事件
/// </summary>
public class UserRemovedFromOrganizationEvent : EventBase
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid? RemovedBy { get; set; }
}

