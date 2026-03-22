namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// 为 MCP HTTP handlers 提供应用级 IServiceProvider 访问入口。
/// </summary>
public sealed class McpServerServiceProviderAccessor
{
    public IServiceProvider? ServiceProvider { get; set; }
}
