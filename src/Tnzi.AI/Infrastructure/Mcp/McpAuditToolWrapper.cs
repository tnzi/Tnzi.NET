namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 包装 MCP 来源的 AIFunction，在执行时记录审计日志（ServerName、ToolName、参数数量），便于安全与审计链路。
/// </summary>
public sealed class McpAuditToolWrapper : DelegatingAIFunction
{
    private readonly string _serverName;
    private readonly ILogger<McpAuditToolWrapper>? _logger;

    public McpAuditToolWrapper(AIFunction innerFunction, string serverName, ILogger<McpAuditToolWrapper>? logger = null)
        : base(Check.NotNull(innerFunction))
    {
        _serverName = Check.NotNullOrEmpty(serverName);
        _logger = logger;
    }

    /// <summary>
    /// 对工具列表中的 AIFunction 包装审计日志；非 AIFunction 的 AITool 原样返回。
    /// </summary>
    public static IList<AITool> Wrap(IList<AITool> tools, string serverName, ILogger<McpAuditToolWrapper>? logger = null)
    {
        if (tools == null || tools.Count == 0)
        {
            return tools ?? (IList<AITool>)new List<AITool>();
        }

        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            if (tool is AIFunction aiFunction)
            {
                result.Add(new McpAuditToolWrapper(aiFunction, serverName, logger));
            }
            else
            {
                result.Add(tool);
            }
        }

        return result;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var argCount = arguments?.Count ?? 0;
        _logger?.LogInformation(
            "MCP tool invoked: ServerName={ServerName}, ToolName={ToolName}, ArgumentCount={ArgumentCount}",
            _serverName,
            InnerFunction.Name,
            argCount);

        return await base.InvokeCoreAsync(arguments ?? new AIFunctionArguments(), cancellationToken).ConfigureAwait(false);
    }
}
