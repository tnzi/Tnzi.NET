namespace Tnzi.AI.Dtos;

/// <summary>
/// 使用量统计摘要 DTO
/// </summary>
public class UsageSummaryDto
{
    /// <summary>Total requests</summary>
    public long TotalRequests { get; set; }
    /// <summary>Successful requests</summary>
    public long SuccessfulRequests { get; set; }
    /// <summary>Failed requests</summary>
    public long FailedRequests { get; set; }
    /// <summary>Total input tokens</summary>
    public long TotalInputTokens { get; set; }
    /// <summary>Total output tokens</summary>
    public long TotalOutputTokens { get; set; }
    /// <summary>Total tokens (input + output)</summary>
    public long TotalTokens { get; set; }
    /// <summary>Average duration (ms)</summary>
    public double AverageDurationMs { get; set; }
    /// <summary>Success rate (0-1)</summary>
    public double SuccessRate { get; set; }
}

/// <summary>
/// 使用量摘要查询 DTO
/// </summary>
public class UsageSummaryQueryDto
{
    /// <summary>Start time</summary>
    public DateTime StartTime { get; set; }
    /// <summary>End time</summary>
    public DateTime EndTime { get; set; }
    /// <summary>Provider filter</summary>
    public string? Provider { get; set; }
    /// <summary>Model filter</summary>
    public string? Model { get; set; }
    /// <summary>Agent ID filter</summary>
    public Guid? AgentId { get; set; }
}

/// <summary>
/// 使用日志 DTO
/// </summary>
public class UsageLogDto
{
    /// <summary>Log ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Provider name</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Model name</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Operation type</summary>
    public string OperationType { get; set; } = string.Empty;
    /// <summary>Input tokens</summary>
    public int InputTokens { get; set; }
    /// <summary>Output tokens</summary>
    public int OutputTokens { get; set; }
    /// <summary>Total tokens</summary>
    public int TotalTokens { get; set; }
    /// <summary>Duration (ms)</summary>
    public long DurationMs { get; set; }
    /// <summary>Success</summary>
    public bool IsSuccess { get; set; }
    /// <summary>Error message</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 使用日志查询 DTO
/// </summary>
public class UsageLogQueryDto : PagedQueryDto
{
    /// <summary>Start time</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>End time</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Provider filter</summary>
    public string? Provider { get; set; }
    /// <summary>Model filter</summary>
    public string? Model { get; set; }
    /// <summary>Operation type filter</summary>
    public string? OperationType { get; set; }
    /// <summary>Success filter</summary>
    public bool? IsSuccess { get; set; }
    /// <summary>Agent ID filter</summary>
    public Guid? AgentId { get; set; }
}

/// <summary>
/// 按提供商分组的使用量统计
/// </summary>
public class ProviderUsageDto
{
    /// <summary>Provider name</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Total requests</summary>
    public long TotalRequests { get; set; }
    /// <summary>Total input tokens</summary>
    public long TotalInputTokens { get; set; }
    /// <summary>Total output tokens</summary>
    public long TotalOutputTokens { get; set; }
    /// <summary>Total tokens</summary>
    public long TotalTokens { get; set; }
    /// <summary>Average duration (ms)</summary>
    public double AverageDurationMs { get; set; }
}

/// <summary>
/// 按模型分组的使用量统计
/// </summary>
public class ModelUsageDto
{
    /// <summary>Provider name</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Model name</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Total requests</summary>
    public long TotalRequests { get; set; }
    /// <summary>Total input tokens</summary>
    public long TotalInputTokens { get; set; }
    /// <summary>Total output tokens</summary>
    public long TotalOutputTokens { get; set; }
    /// <summary>Total tokens</summary>
    public long TotalTokens { get; set; }
    /// <summary>Average duration (ms)</summary>
    public double AverageDurationMs { get; set; }
}
