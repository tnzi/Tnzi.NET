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
    /// <summary>Message count</summary>
    public int MessageCount { get; set; }
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

/// <summary>
/// 线程列表查询 DTO
/// </summary>
public class ThreadListQueryDto : PagedQueryDto
{
    /// <summary>Agent ID filter</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Keyword search (title)</summary>
    [MaxLength(100)]
    public string? Keyword { get; set; }
    /// <summary>Start time filter</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>End time filter</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 线程详情 DTO（含最近消息）
/// </summary>
public class AgentThreadDetailDto
{
    /// <summary>Thread ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>Agent name</summary>
    public string? AgentName { get; set; }
    /// <summary>Thread title</summary>
    public string? Title { get; set; }
    /// <summary>Thread metadata (JSON)</summary>
    public string? Metadata { get; set; }
    /// <summary>Total message count</summary>
    public int MessageCount { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Recent messages</summary>
    public List<ThreadMessageDto> Messages { get; set; } = [];
}

/// <summary>
/// 线程消息 DTO
/// </summary>
public class ThreadMessageDto
{
    /// <summary>Message ID</summary>
    public Guid Id { get; set; }
    /// <summary>Role (system, user, assistant, tool)</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Message content</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>Tool calls (JSON)</summary>
    public string? ToolCalls { get; set; }
    /// <summary>Token usage (JSON)</summary>
    public string? Usage { get; set; }
    /// <summary>Message order</summary>
    public int Order { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 线程导出 DTO（完整线程数据，含所有消息）
/// </summary>
public class ThreadExportDto
{
    /// <summary>Thread ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>Agent name</summary>
    public string? AgentName { get; set; }
    /// <summary>Thread title</summary>
    public string? Title { get; set; }
    /// <summary>Thread metadata (JSON)</summary>
    public string? Metadata { get; set; }
    /// <summary>Total message count</summary>
    public int MessageCount { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Export time</summary>
    public DateTime ExportedAt { get; set; }
    /// <summary>All messages (ordered)</summary>
    public List<ThreadMessageDto> Messages { get; set; } = [];
}
