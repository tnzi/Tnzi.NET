namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 调用时的禁用检查（<see cref="McpServerConfig.DeniedTools"/>）：工具名或服务器原名在禁用名单里时，
/// 调用在发往服务器之前被拒绝，返回以 <see cref="RejectionPrefix"/> 开头的错误文本。
/// </summary>
/// <remarks>
/// <para>
/// 禁用的第一道是 <see cref="McpToolProvider"/> 的清单过滤：被禁用的工具根本不交给模型，模型点它的名字时
/// 执行器答「工具不存在」。这一道是第二道，挂在每个 MCP 工具包装链的最外层，不依赖清单过滤一定被走过：
/// 一个被禁用的工具对象无论经哪条路径到了调用方手里，都到不了服务器，也不会先向人发起审批。
/// </para>
/// <para>
/// 拒绝用返回值而不是异常，与审批拒绝同形：模型读到原因，MCP 回写通道按前缀把它标成错误结果。
/// </para>
/// </remarks>
public sealed class McpDeniedToolGuard : DelegatingAIFunction
{
    /// <summary>拒绝结果的前缀，与审批拒绝相同（MCP 回写通道据此标成错误结果）。</summary>
    public const string RejectionPrefix = "Tool call rejected:";

    private readonly string _serverName;
    private readonly IReadOnlySet<string> _deniedNames;
    private readonly ILogger<McpDeniedToolGuard>? _logger;

    /// <param name="innerFunction">被包装的工具</param>
    /// <param name="serverName">工具所属服务器名</param>
    /// <param name="deniedNames">禁用名单（已展开成原名与前缀名、忽略大小写的集合）</param>
    /// <param name="logger">可选日志</param>
    public McpDeniedToolGuard(AIFunction innerFunction, string serverName, IReadOnlySet<string> deniedNames, ILogger<McpDeniedToolGuard>? logger = null)
        : base(Check.NotNull(innerFunction))
    {
        _serverName = Check.NotNullOrEmpty(serverName);
        _deniedNames = Check.NotNull(deniedNames);
        _logger = logger;
    }

    /// <summary>
    /// 对工具列表中的 AIFunction 包装禁用检查；名单为空时原样返回，非 AIFunction 的 AITool 原样保留。
    /// </summary>
    public static IList<AITool> Wrap(IList<AITool> tools, string serverName, IReadOnlySet<string> deniedNames, ILogger<McpDeniedToolGuard>? logger = null)
    {
        Check.NotNull(tools);
        Check.NotNull(deniedNames);
        if (tools.Count == 0 || deniedNames.Count == 0)
        {
            return tools;
        }

        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            result.Add(tool is AIFunction function ? new McpDeniedToolGuard(function, serverName, deniedNames, logger) : tool);
        }

        return result;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (McpToolNames.IsNamedIn(this, _deniedNames))
        {
            _logger?.LogWarning("MCP tool call refused: tool '{ToolName}' is denied on server '{ServerName}'", Name, _serverName);
            return $"{RejectionPrefix} the tool '{Name}' is denied on MCP server '{_serverName}'.";
        }

        return await base.InvokeCoreAsync(arguments ?? new AIFunctionArguments(), cancellationToken).ConfigureAwait(false);
    }
}
