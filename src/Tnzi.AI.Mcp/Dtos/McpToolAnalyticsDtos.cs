namespace Tnzi.AI.Mcp.Dtos;

/// <summary>
/// MCP 工具统计信息
/// </summary>
public class McpToolStatsDto
{
    /// <summary>Tool name</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Total call count</summary>
    public int TotalCalls { get; set; }

    /// <summary>Average duration in milliseconds</summary>
    public double AvgDurationMs { get; set; }

    /// <summary>P95 duration in milliseconds</summary>
    public double P95DurationMs { get; set; }

    /// <summary>Error rate (0.0 ~ 1.0)</summary>
    public double ErrorRate { get; set; }

    /// <summary>Unique caller count</summary>
    public int UniqueCallers { get; set; }

    /// <summary>First usage time</summary>
    public DateTime? FirstUsed { get; set; }

    /// <summary>Last usage time</summary>
    public DateTime? LastUsed { get; set; }
}

/// <summary>
/// MCP 工具热度排名
/// </summary>
public class McpToolPopularityDto
{
    /// <summary>Tool name</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Total call count</summary>
    public int CallCount { get; set; }

    /// <summary>Success rate (0.0 ~ 1.0)</summary>
    public double SuccessRate { get; set; }

    /// <summary>Average duration in milliseconds</summary>
    public double AvgDurationMs { get; set; }
}

/// <summary>
/// MCP 工具错误信息
/// </summary>
public class McpToolErrorDto
{
    /// <summary>Error message</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Occurrence count</summary>
    public int Count { get; set; }

    /// <summary>Last occurrence time</summary>
    public DateTime LastOccurred { get; set; }
}
