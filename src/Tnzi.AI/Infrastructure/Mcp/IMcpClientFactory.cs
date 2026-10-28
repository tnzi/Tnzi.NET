namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端工厂 - 按配置创建/复用与 MCP 服务器的连接。
/// client 按分区键（租户键 + Server Name，部署配置条目不带租户键）缓存复用，两个租户的同名 server 各用各的连接；
/// 注册表改动经 <see cref="InvalidateClientAsync(string, CancellationToken)"/> 失效对应连接。
/// </summary>
public interface IMcpClientFactory
{
    /// <summary>
    /// 获取或创建与指定 MCP 服务器配置对应的 McpClient。
    /// 如果缓存中的连接已断开，会自动移除并以指数退避重试重连（最多 3 次）。
    /// </summary>
    /// <param name="config">服务器配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已连接并完成初始化的 McpClient（调用方不负责 Dispose，由工厂在应用关闭时统一释放）</returns>
    Task<IMcpClientAdapter> GetOrCreateClientAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>
    /// 从缓存中移除指定名称的 MCP 客户端并释放其资源（所有租户分区里叫这个名字的连接都失效，失败冷却一并清掉）。
    /// 幂等操作：对不存在的名称不抛异常。
    /// </summary>
    /// <param name="serverName">MCP 服务器名称</param>
    /// <param name="ct">取消令牌</param>
    Task InvalidateClientAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// 失效<b>这一份配置</b>对应的连接。用户专属服务器（<see cref="UserMcpServerConfig"/>）只失效那个用户的那一条，
    /// 不像按名字失效那样波及别的用户；<paramref name="refreshCredential"/> 为 true 时下一次建连会带
    /// <c>McpCredentialRequest.Refresh = true</c> 向消费方要刷新过的凭据。其它配置等同按名字失效。
    /// </summary>
    Task InvalidateClientAsync(McpServerConfig config, bool refreshCredential, CancellationToken ct = default)
        => InvalidateClientAsync(config.Name, ct);

    /// <summary>
    /// 失效某个用户的用户专属连接（连带失败冷却与待刷新标记）。<paramref name="serverName"/> 为 null = 该用户的全部。幂等。
    /// </summary>
    Task InvalidateUserClientsAsync(Guid userId, string? serverName, CancellationToken ct = default) => Task.CompletedTask;
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

    /// <summary>
    /// 列出该 MCP 服务器提供的资源。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>资源信息列表</returns>
    Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<McpResourceInfo>>(Array.Empty<McpResourceInfo>());

    /// <summary>
    /// 读取该 MCP 服务器上的指定资源内容。
    /// </summary>
    /// <param name="uri">资源 URI</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>资源内容；资源不存在时返回 null</returns>
    Task<McpResourceContent?> ReadResourceAsync(string uri, CancellationToken ct = default) =>
        Task.FromResult<McpResourceContent?>(null);

    /// <summary>
    /// 列出该 MCP 服务器提供的 Prompt 模板。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>Prompt 信息列表</returns>
    Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<McpPromptInfo>>(Array.Empty<McpPromptInfo>());

    /// <summary>
    /// 获取该 MCP 服务器上的指定 Prompt 模板渲染结果。
    /// </summary>
    /// <param name="promptName">Prompt 名称</param>
    /// <param name="arguments">Prompt 参数（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Prompt 结果；Prompt 不存在时返回 null</returns>
    Task<McpPromptResult?> GetPromptAsync(string promptName, Dictionary<string, string>? arguments = null, CancellationToken ct = default) =>
        Task.FromResult<McpPromptResult?>(null);

    /// <summary>
    /// 检查与 MCP 服务器的连接是否仍然有效。
    /// 默认实现通过调用 ListToolsAsync 并限时 5 秒验证连接状态。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>连接仍然有效返回 true，否则返回 false</returns>
    async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await ListToolsAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
