
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
    /// <param name="toolNames">单个工具名称列表（per-tool 授权/请求覆盖；在工具组之外额外解析，按名称去重合并；权限仍门控）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的工具列表，无工具时返回 null</returns>
    Task<IList<AITool>?> ResolveToolsAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? userPermissions = null, IEnumerable<string>? toolNames = null, CancellationToken ct = default);
}

/// <summary>
/// 工具解析器 — 解析 C# 工具、OpenAPI 工具、MCP 工具并合并
/// </summary>
public class ToolResolver : IToolResolver, IDisposable
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly OpenApiToolGenerator _openApiToolGenerator;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly IToolPermissionEvaluator? _permissionEvaluator;
    private readonly IShellCommandAnalyzer? _shellCommandAnalyzer;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<ApprovalToolWrapper>? _approvalLogger;
    private readonly ILogger _toolAdapterLogger;
    private readonly ILogger<ToolResolver> _logger;

    /// <summary>
    /// OpenAPI 工具缓存（线程安全的延迟初始化，避免重复拉取规范）
    /// </summary>
    private readonly SemaphoreSlim _openApiCacheLock = new(1, 1);
    private readonly IDisposable? _optionsChangeListener;
    private IReadOnlyList<AITool>? _openApiToolsCache;
    private long _openApiCacheVersion;
    private long _currentConfigVersion;

    public ToolResolver(
        IToolRegistry toolRegistry,
        IMcpToolProvider mcpToolProvider,
        OpenApiToolGenerator openApiToolGenerator,
        IOptionsMonitor<AIOptions> options,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        ILogger<ToolResolver> logger,
        IToolApprovalHandler? approvalHandler = null,
        IToolPermissionEvaluator? permissionEvaluator = null,
        IShellCommandAnalyzer? shellCommandAnalyzer = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        IEventBus? eventBus = null)
    {
        _toolRegistry = Check.NotNull(toolRegistry);
        _mcpToolProvider = Check.NotNull(mcpToolProvider);
        _openApiToolGenerator = Check.NotNull(openApiToolGenerator);
        _options = Check.NotNull(options);
        _serviceProvider = Check.NotNull(serviceProvider);
        var lf = Check.NotNull(loggerFactory);
        _approvalHandler = approvalHandler;
        _permissionEvaluator = permissionEvaluator;
        _shellCommandAnalyzer = shellCommandAnalyzer;
        _executionContextAccessor = executionContextAccessor;
        _eventBus = eventBus;
        _approvalLogger = lf.CreateLogger<ApprovalToolWrapper>();
        _toolAdapterLogger = lf.CreateLogger(typeof(ToolAdapter).FullName!);
        _logger = Check.NotNull(logger);

        _optionsChangeListener = options.OnChange(_ =>
        {
            Interlocked.Increment(ref _currentConfigVersion);
        });
    }

    /// <inheritdoc />
    public async Task<IList<AITool>?> ResolveToolsAsync(IEnumerable<string>? toolGroups, IEnumerable<string>? userPermissions = null, IEnumerable<string>? toolNames = null, CancellationToken ct = default)
    {
        // C# 工具：当 toolGroups 非空（组解析）或 toolNames 非空（per-tool 授权/请求覆盖）时从 Registry 获取。
        // 两路按工具名去重合并为一个 ToolDefinition 集合，再统一转换/审批包装，使 per-tool 授权与组授权
        // 共享同一条权限过滤 + 审批管道。权限不足的工具在 Registry 层已被排除（授权≠绕过权限）。
        IList<AITool>? csharpTools = null;
        var toolNamesList = toolNames as IReadOnlyCollection<string> ?? toolNames?.ToList();
        var hasToolNames = toolNamesList is { Count: > 0 };
        if (toolGroups != null || hasToolNames)
        {
            var toolDefMap = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);

            if (toolGroups != null)
            {
                foreach (var td in _toolRegistry.GetToolsByGroupsWithPermissions(toolGroups, userPermissions))
                {
                    toolDefMap.TryAdd(td.Name, td);
                }
            }

            if (hasToolNames)
            {
                foreach (var td in _toolRegistry.GetToolsByNames(toolNamesList!, userPermissions))
                {
                    toolDefMap.TryAdd(td.Name, td);
                }
            }

            var toolDefinitions = (IReadOnlyList<ToolDefinition>)toolDefMap.Values.ToList();
            csharpTools = ToolAdapter.ConvertToAITools(toolDefinitions, _serviceProvider, _toolAdapterLogger);

            if (csharpTools.Count > 0 && ShouldWrapWithApproval())
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
            // Route OpenAPI tools through the same approval/permission gating as C# tools.
            // Uses synthetic tool-group name "openapi" so approval rules can target the group.
            // Only applies when governance is actually configured (ShouldWrapWithApproval()),
            // preserving the default no-governance behaviour.
            IEnumerable<AITool> openApiToolsToMerge = openApiTools;
            if (ShouldWrapWithApproval() && openApiTools.Count > 0)
            {
                var approvalOptions = _options.CurrentValue.ToolApproval;
                var openApiGroupMap = new Dictionary<string, string>(openApiTools.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var t in openApiTools)
                {
                    if (t.Name != null) openApiGroupMap[t.Name] = "openapi";
                }
                openApiToolsToMerge = ApprovalToolWrapper.Wrap(
                    (IList<AITool>)openApiTools,
                    _approvalHandler,
                    approvalOptions,
                    _permissionEvaluator,
                    _shellCommandAnalyzer,
                    _executionContextAccessor,
                    _approvalLogger,
                    openApiGroupMap,
                    _eventBus);
            }

            foreach (var t in openApiToolsToMerge)
            {
                if (t.Name != null && names.Add(t.Name)) merged.Add(t);
            }
        }

        // 3. MCP 工具（最低优先级）
        if (_options.CurrentValue.Mcp?.Enabled == true)
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
        if (_approvalHandler == null && _permissionEvaluator == null) return tools;

        var approvalOptions = _options.CurrentValue.ToolApproval;
        var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var td in toolDefinitions)
        {
            if (!string.IsNullOrEmpty(td.GroupName))
            {
                toolNameToGroup[td.Name] = td.GroupName;
            }
        }

        return ApprovalToolWrapper.Wrap(tools, _approvalHandler, approvalOptions, _permissionEvaluator, _shellCommandAnalyzer, _executionContextAccessor, _approvalLogger, toolNameToGroup, _eventBus);
    }

    private bool ShouldWrapWithApproval()
        => _options.CurrentValue.ToolApproval.Enabled || _permissionEvaluator != null;

    /// <summary>
    /// 获取 OpenAPI 生成的工具（线程安全的延迟初始化缓存，配置变更时自动失效）
    /// </summary>
    private async Task<IReadOnlyList<AITool>?> GetOpenApiToolsAsync(CancellationToken ct)
    {
        if (_options.CurrentValue.OpenApiTools is not { Enabled: true })
        {
            return null;
        }

        var configVersion = Volatile.Read(ref _currentConfigVersion);

        // 快速路径：缓存有效且版本匹配
        if (_openApiToolsCache != null && Volatile.Read(ref _openApiCacheVersion) == configVersion)
        {
            return _openApiToolsCache;
        }

        await _openApiCacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 二次检查：另一个线程可能已在等锁期间完成初始化
            if (_openApiToolsCache != null && Volatile.Read(ref _openApiCacheVersion) == configVersion)
            {
                return _openApiToolsCache;
            }

            _openApiToolsCache = await _openApiToolGenerator.GenerateToolsAsync(ct).ConfigureAwait(false);
            if (_openApiToolsCache.Count > 0)
            {
                _logger.LogDebug("Resolved {Count} OpenAPI tools", _openApiToolsCache.Count);
            }
            Volatile.Write(ref _openApiCacheVersion, configVersion);
            return _openApiToolsCache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate OpenAPI tools, skipping");
            return null;
        }
        finally
        {
            _openApiCacheLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _optionsChangeListener?.Dispose();
        _openApiCacheLock.Dispose();
    }
}
