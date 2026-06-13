namespace Tnzi.AI.Dtos;

/// <summary>
/// AgentPersona 输出 DTO
/// </summary>
public class AgentPersonaDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>可见性作用域（System / Tenant）</summary>
    public ResourceScope Scope { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 创建 AgentPersona 请求
/// </summary>
public class CreateAgentPersonaDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>
    /// 显式指定作用域（可选）。未指定时服务层按当前租户上下文推断：
    /// 有租户上下文 → Tenant；无租户上下文 → System。
    /// </summary>
    public ResourceScope? Scope { get; set; }
}

/// <summary>
/// 更新 AgentPersona 请求
/// </summary>
public class UpdateAgentPersonaDto
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Content { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// AgentPersona 查询参数
/// </summary>
public class AgentPersonaQueryDto : PagedQueryDto
{
    public string? Keyword { get; set; }
    /// <summary>按作用域过滤（可选）</summary>
    public ResourceScope? Scope { get; set; }
}
