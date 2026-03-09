
namespace Tnzi.AI.Infrastructure.Tools;

/// <summary>
/// 工具解析器接口 — 解析 C# 工具 + OpenAPI 工具 + MCP 工具并合并
/// </summary>
public interface IToolResolver
{
    /// <summary>
    /// 解析并合并工具列表（C# 工具 + OpenAPI 工具 + MCP 工具，按名称去重）
    /// </summary>
    /// <param name="toolGroups">工具组列表（为空时仅合并 MCP 工具）</param>
    /// <param name="userPermissions">用户权限列表（为空时不过滤权限，返回所有工具）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的工具列表，无工具时返回 null</returns>
    Task<IList<AITool>?> ResolveToolsAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? userPermissions = null, CancellationToken ct = default);
}

/// <summary>
/// 工具解析器 — 解析 C# 工具、OpenAPI 工具、MCP 工具并合并
/// </summary>
public class ToolResolver : IToolResolver
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly OpenApiToolGenerator _openApiToolGenerator;
    private readonly IOptions<AIOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly ILogger<ApprovalToolWrapper>? _approvalLogger;
    private readonly ILogger _toolAdapterLogger;
    private readonly ILogger<ToolResolver> _logger;

    /// <summary>
    /// OpenAPI 工具缓存（首次解析后缓存，避免重复拉取规范）
    /// </summary>
    private IReadOnlyList<AITool>? _openApiToolsCache;

    public ToolResolver(
        IToolRegistry toolRegistry,
        IMcpToolProvider mcpToolProvider,
        OpenApiToolGenerator openApiToolGenerator,
        IOptions<AIOptions> options,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        ILogger<ToolResolver> logger,
        IToolApprovalHandler? approvalHandler = null)
    {
        _toolRegistry = Check.NotNull(toolRegistry);
        _mcpToolProvider = Check.NotNull(mcpToolProvider);
        _openApiToolGenerator = Check.NotNull(openApiToolGenerator);
        _options = Check.NotNull(options);
        _serviceProvider = Check.NotNull(serviceProvider);
        var lf = Check.NotNull(loggerFactory);
        _approvalHandler = approvalHandler;
        _approvalLogger = lf.CreateLogger<ApprovalToolWrapper>();
        _toolAdapterLogger = lf.CreateLogger(typeof(ToolAdapter).FullName!);
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
            csharpTools = ToolAdapter.ConvertToAITools(toolDefinitions, _serviceProvider, _toolAdapterLogger);

            if (csharpTools.Count > 0 && _options.Value.ToolApproval.Enabled)
            {
                csharpTools = WrapWithApproval(csharpTools, toolDefinitions);
            }
        }

        // 统一合并管道：按名称去重，优先级 C# > OpenAPI > MCP
        var merged = new List<AITool>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. C# 工具（最高优先级）
        if (csharpTools != null)
        {
            foreach (var t in csharpTools)
            {
                if (t.Name != null && names.Add(t.Name)) merged.Add(t);
            }
        }

        // 2. OpenAPI 工具（次高优先级）
        var openApiTools = await GetOpenApiToolsAsync(ct);
        if (openApiTools != null)
        {
            foreach (var t in openApiTools)
            {
                if (t.Name != null && names.Add(t.Name)) merged.Add(t);
            }
        }

        // 3. MCP 工具（最低优先级）
        if (_options.Value.Mcp?.Enabled == true)
        {
            var mcpTools = await _mcpToolProvider.GetToolsAsync(ct).ConfigureAwait(false);
            foreach (var t in mcpTools)
            {
                if (t.Name != null && names.Add(t.Name)) merged.Add(t);
            }
        }

        return merged.Count > 0 ? merged : null;
    }

    /// <summary>
    /// 对 C# 工具应用全局 Approval 包装（MCP 工具在 McpToolProvider 内已处理）
    /// </summary>
    private IList<AITool> WrapWithApproval(IList<AITool> tools, IReadOnlyList<ToolDefinition> toolDefinitions)
    {
        if (_approvalHandler == null) return tools;

        var approvalOptions = _options.Value.ToolApproval;
        var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var td in toolDefinitions)
        {
            if (!string.IsNullOrEmpty(td.GroupName))
            {
                toolNameToGroup[td.Name] = td.GroupName;
            }
        }

        return ApprovalToolWrapper.Wrap(tools, _approvalHandler, approvalOptions, _approvalLogger, toolNameToGroup);
    }

    /// <summary>
    /// 获取 OpenAPI 生成的工具（按 Scoped 生命周期缓存，避免重复拉取规范）
    /// </summary>
    private async Task<IReadOnlyList<AITool>?> GetOpenApiToolsAsync(CancellationToken ct)
    {
        if (_options.Value.OpenApiTools is not { Enabled: true })
        {
            return null;
        }

        if (_openApiToolsCache != null)
        {
            return _openApiToolsCache;
        }

        try
        {
            _openApiToolsCache = await _openApiToolGenerator.GenerateToolsAsync(ct).ConfigureAwait(false);
            if (_openApiToolsCache.Count > 0)
            {
                _logger.LogDebug("Resolved {Count} OpenAPI tools", _openApiToolsCache.Count);
            }
            return _openApiToolsCache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate OpenAPI tools, skipping");
            return null;
        }
    }
}
