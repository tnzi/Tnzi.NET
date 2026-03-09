namespace Tnzi.AI.Domain;

/// <summary>
/// Agent 线程消息实体 - 存储对话历史
/// </summary>
public class AgentThreadMessage : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属线程 ID
    /// </summary>
    public Guid ThreadId { get; set; }

    /// <summary>
    /// 消息角色: system, user, assistant, tool
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 工具调用信息（JSON，可选）
    /// </summary>
    public string? ToolCalls { get; set; }

    /// <summary>
    /// Token 使用量（JSON，可选）
    /// </summary>
    public string? Usage { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 导航属性
    /// </summary>
    public virtual AgentThread? Thread { get; set; }
}
