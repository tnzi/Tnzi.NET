namespace Tnzi.AI.Entities;

/// <summary>
/// 技能实体 — 存储租户级和用户级自定义技能。
/// 不使用 MultiTenantAuditedEntity 以避免全局多租户查询过滤器干扰 —
/// 技能需要同时支持 Tenant scope（按 TenantId 过滤）和 User scope（按 OwnerUserId 过滤），
/// DatabaseSkillStore 已有显式联合过滤逻辑。
/// </summary>
public class SkillEntity : FullAuditedEntity<Guid>
{
    /// <summary>租户 ID（Tenant scope 时非空）</summary>
    public Guid? TenantId { get; set; }
    /// <summary>技能 slug（人类可读标识符，小写字母/数字/连字符）</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>作用域（Tenant 或 User）</summary>
    public SkillScope Scope { get; set; }

    /// <summary>所有者用户 ID（User 作用域时非空）</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>技能名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>技能描述</summary>
    public string? Description { get; set; }

    /// <summary>提示词模板内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>使用场景说明</summary>
    public string? WhenToUse { get; set; }

    /// <summary>参数定义 JSON（SkillParameter 列表）</summary>
    public string ParametersJson { get; set; } = "[]";

    /// <summary>约束配置 JSON（AllowedToolGroups, RequiredModel, RequiredProvider）</summary>
    public string? ConstraintsJson { get; set; }

    /// <summary>依赖要求 JSON（SkillRequirements）</summary>
    public string? RequirementsJson { get; set; }

    /// <summary>标签 JSON（string 列表）</summary>
    public string? TagsJson { get; set; }

    /// <summary>优先级（数值越大优先级越高）</summary>
    public int Priority { get; set; }

    /// <summary>版本</summary>
    public string? Version { get; set; }

    /// <summary>作者</summary>
    public string? Author { get; set; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;
}
