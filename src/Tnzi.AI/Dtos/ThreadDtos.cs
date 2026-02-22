namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 线程 DTO
/// </summary>
public class AgentThreadDto
{
    /// <summary>Thread ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>Thread title</summary>
    public string? Title { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建线程 DTO
/// </summary>
public class CreateAgentThreadDto
{
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>Thread title</summary>
    [MaxLength(200)]
    public string? Title { get; set; }
}
