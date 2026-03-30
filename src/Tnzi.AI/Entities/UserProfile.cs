namespace Tnzi.AI.Entities;

/// <summary>
/// 用户 AI 档案实体 — 用于个性化上下文注入
/// </summary>
public class UserProfile : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>关联的用户 ID</summary>
    public Guid UserId { get; set; }

    /// <summary>显示名称</summary>
    public string? DisplayName { get; set; }

    /// <summary>角色/职位描述</summary>
    public string? Role { get; set; }

    /// <summary>首选语言（如 "zh-CN", "en-US"）</summary>
    public string? PreferredLanguage { get; set; }

    /// <summary>自由格式的个人描述（Markdown）</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }
}
