namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 人格实体 — 可复用的 Soul 内容
/// </summary>
public class AgentPersona : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>人格名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL 友好标识符</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>人格内容（Markdown 格式的 Soul 描述）</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否为系统内置人格</summary>
    public bool IsSystem { get; set; }

    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }
}
