namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端工厂 - 按配置创建/复用与 MCP 服务器的连接。
/// 第一版以 Server Name 为 key 缓存 client；配置变更需重启应用生效。
/// </summary>
public interface IMcpClientFactory
{
    /// <summary>
    /// 获取或创建与指定 MCP 服务器配置对应的 McpClient。
    /// </summary>
    /// <param name="config">服务器配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已连接并完成初始化的 McpClient（调用方不负责 Dispose，由工厂在应用关闭时统一释放）</returns>
    Task<IMcpClientAdapter> GetOrCreateClientAsync(McpServerConfig config, CancellationToken ct = default);
}

/// <summary>
/// MCP 客户端适配器 - 屏蔽 SDK 具体类型，避免调用方依赖 ModelContextProtocol 程序集，便于单元测试与未来替换实现。
/// </summary>
public interface IMcpClientAdapter : IAsyncDisposable
{
    /// <summary>
    /// 列出该 MCP 服务器提供的工具（已为 AITool 兼容类型，可直接传入 AgentExecutor）。
    /// </summary>
    Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default);
}
