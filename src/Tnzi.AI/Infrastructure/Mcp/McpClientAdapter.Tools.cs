namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpClientAdapter"/> 的工具相关 partial - 工具列举。
/// </summary>
internal sealed partial class McpClientAdapter
{
    /// <inheritdoc />
    /// <remarks>
    /// SDK 的 <c>ListToolsAsync</c> 直接给出 <see cref="McpClientTool"/>（它本身就是 <see cref="AIFunction"/>，
    /// 调用时用协议层的原名回到服务器），这里原样交出去。开了 <see cref="McpServerConfig.PrefixToolNameWithServer"/>
    /// 时经 SDK 的 <see cref="McpClientTool.WithName"/> 改成 <see cref="McpToolNames.Prefixed"/> 的名字：
    /// 只改模型看到的名字，原名仍可经 <see cref="McpToolNames.OriginalName"/> 取回。
    /// </remarks>
    public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var list = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            list.Add(_prefixToolNameWithServer && !string.IsNullOrWhiteSpace(_serverName)
                ? tool.WithName(McpToolNames.Prefixed(_serverName, tool.Name))
                : tool);
        }

        return list;
    }
}
