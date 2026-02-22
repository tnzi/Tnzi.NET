namespace Tnzi.ScopedContext;

/// <summary>
/// 作用域上下文项键名常量
/// </summary>
public static class ScopedContextItems
{
    public const string CurrentUserId = "CurrentUserId";
    public const string CurrentUserName = "CurrentUserName";
    public const string CurrentUserRoles = "CurrentUserRoles";
    public const string AuditOperation = "AuditOperation";
    public const string ClientIpAddress = "ClientIpAddress";
    public const string UserAgent = "UserAgent";
    public const string TraceId = "TraceId";
}

/// <summary>
/// 审计操作信息
/// </summary>
public class AuditOperationInfo
{
    public string OperationType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

