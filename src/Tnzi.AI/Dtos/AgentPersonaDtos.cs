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
    public bool IsSystem { get; set; }
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
    public bool IsSystem { get; set; }
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
    public bool? IsSystem { get; set; }
}

/// <summary>
/// AgentPersona 查询参数
/// </summary>
public class AgentPersonaQueryDto : PagedQueryDto
{
    public string? Keyword { get; set; }
    public bool? IsSystem { get; set; }
}
