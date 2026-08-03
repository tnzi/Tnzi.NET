namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 组装投递给外部 agent 的受管 MCP 配置。
/// </summary>
/// <remarks>
/// 把 Agent 绑定上配置的 MCP server，与框架自己的回写通道（<c>Tnzi.AI.Mcp</c> 暴露的
/// HTTP/SSE 端点）合并成一份 Claude 风格的 <c>mcpServers</c> 对象。
/// <para>
/// 框架<b>只</b>注入这条回写通道，<b>不内置任何业务工具</b>：建工单、发评论、改状态
/// 这些是消费应用的领域，由应用自己往 <c>Tnzi.AI.Mcp</c> 注册。框架不含业务。
/// </para>
/// </remarks>
public interface ICliMcpConfigComposer
{
    /// <summary>
    /// 合并绑定配置与回写通道。两者都没有时返回 <c>null</c>（= 让 CLI 继承宿主本机配置）。
    /// </summary>
    string? Compose(string? bindingMcpConfigJson, string? writeBackToken, CliWriteBackOptions options);
}

/// <inheritdoc cref="ICliMcpConfigComposer" />
public class CliMcpConfigComposer : ICliMcpConfigComposer
{
    private readonly ILogger<CliMcpConfigComposer> _logger;

    /// <summary>初始化 MCP 配置组装器。</summary>
    public CliMcpConfigComposer(ILogger<CliMcpConfigComposer> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public string? Compose(string? bindingMcpConfigJson, string? writeBackToken, CliWriteBackOptions options)
    {
        Check.NotNull(options);

        var servers = ParseServers(bindingMcpConfigJson);
        var hasWriteBack = options.Enabled
                           && !string.IsNullOrWhiteSpace(writeBackToken)
                           && !string.IsNullOrWhiteSpace(options.McpEndpoint);

        if (servers is null && !hasWriteBack)
        {
            return null;
        }

        servers ??= [];

        if (hasWriteBack)
        {
            servers[options.ServerName] = new JsonObject
            {
                ["type"] = "sse",
                ["url"] = options.McpEndpoint,
                ["headers"] = new JsonObject
                {
                    // MCP server 同时接受 X-Api-Key 与 Authorization: Bearer；
                    // 用 Bearer 是因为它更不容易被中间代理当成可缓存的自定义头。
                    ["Authorization"] = $"Bearer {writeBackToken}"
                }
            };
        }
        else if (options.Enabled && string.IsNullOrWhiteSpace(options.McpEndpoint))
        {
            // 开了回写却没给端点：静默跳过会让人以为通道通了，实际 agent 手里什么都没有。
            _logger.LogWarning(
                "AI:Cli:WriteBack is enabled but McpEndpoint is empty; the agent will receive no write-back channel");
        }

        return new JsonObject { ["mcpServers"] = servers }.ToJsonString();
    }

    private JsonObject? ParseServers(string? bindingMcpConfigJson)
    {
        if (string.IsNullOrWhiteSpace(bindingMcpConfigJson))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(bindingMcpConfigJson);
            if (node?["mcpServers"] is JsonObject servers)
            {
                // 深拷贝：JsonNode 的子节点带父指针，直接复用会在重新挂载时抛异常。
                return JsonNode.Parse(servers.ToJsonString()) as JsonObject;
            }

            // 允许绑定里直接写成 { "name": {...} } 的裸形态。
            return node as JsonObject is { } bare
                ? JsonNode.Parse(bare.ToJsonString()) as JsonObject
                : null;
        }
        catch (JsonException ex)
        {
            // fail-closed 到「没有受管配置」而不是「配置写错了但照样跑」：
            // 后者会让 agent 悄悄继承宿主本机的全部 MCP server，那是个越权面。
            _logger.LogError(ex, "The agent binding's MCP config is not valid JSON; no managed MCP servers will be passed");
            return [];
        }
    }
}
