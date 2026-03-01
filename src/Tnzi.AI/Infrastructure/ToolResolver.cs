
namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 工具解析器接口 — 解析 C# 工具 + MCP 工具并合并
/// </summary>
public interface IToolResolver
{
    /// <summary>
    /// 解析并合并工具列表（C# 工具 + MCP 工具，按名称去重）
    /// </summary>
    /// <param name="toolGroups">工具组列表（为空时仅合并 MCP 工具）</param>
    /// <param name="userPermissions">用户权限列表（为空时不过滤权限，返回所有工具）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的工具列表，无工具时返回 null</returns>
    Task<IList<AITool>?> ResolveToolsAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? userPermissions = null, CancellationToken ct = default);
}

/// <summary>
/// 工具解析器 — 解析 C# 工具、应用 Approval 包装、合并 MCP 工具
/// </summary>
public class ToolResolver : IToolResolver
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly IOptions<AIOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolResolver> _logger;

    public ToolResolver(
        IToolRegistry toolRegistry,
        IMcpToolProvider mcpToolProvider,
        IOptions<AIOptions> options,
        IServiceProvider serviceProvider,
        ILogger<ToolResolver> logger)
    {
        _toolRegistry = Check.NotNull(toolRegistry);
        _mcpToolProvider = Check.NotNull(mcpToolProvider);
        _options = Check.NotNull(options);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IList<AITool>?> ResolveToolsAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? userPermissions = null, CancellationToken ct = default)
    {
        // C# 工具：仅当 toolGroups 非空时从 Registry 获取
        IList<AITool>? csharpTools = null;
        if (toolGroups != null)
        {
            var toolDefinitions = _toolRegistry.GetToolsByGroupsWithPermissions(toolGroups, userPermissions);
            csharpTools = ToolAdapter.ConvertToAITools(toolDefinitions, _serviceProvider);

            if (csharpTools.Count > 0 && _options.Value.ToolApproval.Enabled)
            {
                csharpTools = WrapWithApproval(csharpTools, toolDefinitions);
            }
        }

        // MCP 工具：当启用时拉取并合并
        if (_options.Value.Mcp?.Enabled == true)
        {
            return await MergeWithMcpToolsAsync(csharpTools, ct);
        }

        return csharpTools is { Count: > 0 } ? csharpTools : null;
    }

    /// <summary>
    /// 对 C# 工具应用全局 Approval 包装（MCP 工具在 McpToolProvider 内已处理）
    /// </summary>
    private IList<AITool> WrapWithApproval(IList<AITool> tools, IReadOnlyList<ToolDefinition> toolDefinitions)
    {
        var approvalHandler = _serviceProvider.GetService<IToolApprovalHandler>();
        if (approvalHandler == null) return tools;

        var approvalOptions = _options.Value.ToolApproval;
        var approvalLogger = _serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<ApprovalToolWrapper>();

        var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var td in toolDefinitions)
        {
            if (!string.IsNullOrEmpty(td.GroupName))
            {
                toolNameToGroup[td.Name] = td.GroupName;
            }
        }

        return ApprovalToolWrapper.Wrap(tools, approvalHandler, approvalOptions, approvalLogger, toolNameToGroup);
    }

    /// <summary>
    /// 拉取 MCP 工具并与 C# 工具按名称去重合并
    /// </summary>
    private async Task<IList<AITool>?> MergeWithMcpToolsAsync(IList<AITool>? csharpTools, CancellationToken ct)
    {
        var mcpTools = await _mcpToolProvider.GetToolsAsync(ct).ConfigureAwait(false);
        var merged = new List<AITool>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (csharpTools != null)
        {
            foreach (var t in csharpTools)
            {
                if (t.Name != null && names.Add(t.Name)) merged.Add(t);
            }
        }

        foreach (var t in mcpTools)
        {
            if (t.Name != null && names.Add(t.Name)) merged.Add(t);
        }

        return merged.Count > 0 ? merged : null;
    }
}
