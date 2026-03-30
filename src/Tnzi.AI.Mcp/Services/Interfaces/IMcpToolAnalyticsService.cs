using Tnzi.AI.Mcp.Dtos;

namespace Tnzi.AI.Mcp.Services.Interfaces;

/// <summary>
/// MCP 工具使用分析服务
/// </summary>
public interface IMcpToolAnalyticsService
{
    /// <summary>
    /// 记录一次工具调用
    /// </summary>
    Task RecordUsageAsync(string toolName, long durationMs, bool isSuccess, string? errorMessage = null, string? callerApiKeyId = null);

    /// <summary>
    /// 获取指定工具的统计信息
    /// </summary>
    Task<Result<McpToolStatsDto>> GetToolStatsAsync(string toolName, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// 获取最常用工具排名
    /// </summary>
    Task<Result<List<McpToolPopularityDto>>> GetMostUsedToolsAsync(int top = 10, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// 获取指定工具的错误列表
    /// </summary>
    Task<Result<List<McpToolErrorDto>>> GetToolErrorsAsync(string toolName, int top = 20);

    /// <summary>
    /// 清理过期记录
    /// </summary>
    Task<Result<int>> CleanupOldRecordsAsync(int retentionDays = 90);
}
