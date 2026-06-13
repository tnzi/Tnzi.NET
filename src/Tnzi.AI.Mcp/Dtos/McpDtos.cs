namespace Tnzi.AI.Dtos;

/// <summary>
/// MCP Server 状态 DTO
/// </summary>
public class McpServerStatusDto
{
    /// <summary>Whether MCP server is enabled</summary>
    public bool Enabled { get; set; }

    /// <summary>HTTP/SSE endpoint path</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Whether authentication is required</summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>Whether rate-limit keys are partitioned per tenant (not execution isolation)</summary>
    public bool RateLimitPerTenant { get; set; }

    /// <summary>Rate limit per minute</summary>
    public int RateLimitPerMinute { get; set; }

    /// <summary>Number of exposed agents</summary>
    public int ExposedAgentCount { get; set; }

    /// <summary>Number of custom tools</summary>
    public int CustomToolCount { get; set; }

    /// <summary>Total registered MCP tools</summary>
    public int TotalToolCount { get; set; }
}

/// <summary>
/// MCP 工具信息 DTO
/// </summary>
public class McpToolInfoDto
{
    /// <summary>Tool name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tool description</summary>
    public string? Description { get; set; }
}
