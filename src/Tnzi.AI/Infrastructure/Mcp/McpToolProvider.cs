
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 从有效 MCP 服务器（IMcpServerCatalog：部署配置 + DB 注册表合并）拉取工具，
/// 按 AllowedTools 过滤并按每服务器 ApprovalMode 做审批包装后返回。
/// 单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
public class McpToolProvider : IMcpToolProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IMcpServerCatalog _serverCatalog;
    private readonly IMcpClientFactory _clientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly IToolPermissionEvaluator? _permissionEvaluator;
    private readonly IShellCommandAnalyzer? _shellCommandAnalyzer;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly IEventBus? _eventBus;
    private readonly McpOAuthClientHandler? _oauthHandler;
    private readonly ILogger<McpToolProvider> _logger;
    private readonly ConcurrentDictionary<string, ToolCacheEntry> _toolCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _toolLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<string>> _serverToolNames = new(StringComparer.OrdinalIgnoreCase);

    public McpToolProvider(
        IOptionsMonitor<AIOptions> options,
        IMcpServerCatalog serverCatalog,
        IMcpClientFactory clientFactory,
        ILoggerFactory loggerFactory,
        ILogger<McpToolProvider> logger,
        IToolApprovalHandler? approvalHandler = null,
        IToolPermissionEvaluator? permissionEvaluator = null,
        IShellCommandAnalyzer? shellCommandAnalyzer = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        McpOAuthClientHandler? oauthHandler = null,
        IEventBus? eventBus = null)
    {
        _options = Check.NotNull(options);
        _serverCatalog = Check.NotNull(serverCatalog);
        _clientFactory = Check.NotNull(clientFactory);
        _loggerFactory = Check.NotNull(loggerFactory);
        _logger = Check.NotNull(logger);
        _approvalHandler = approvalHandler;
        _permissionEvaluator = permissionEvaluator;
        _shellCommandAnalyzer = shellCommandAnalyzer;
        _executionContextAccessor = executionContextAccessor;
        _eventBus = eventBus;
        _oauthHandler = oauthHandler;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        // 有效服务器 = 部署配置 + DB 注册表合并（catalog 内部处理 AI:Mcp:Enabled 总开关）
        var servers = await _serverCatalog.GetEffectiveServersAsync(ct).ConfigureAwait(false);
        if (servers.Count == 0)
        {
            return Array.Empty<AITool>();
        }

        var toolCacheSeconds = _options.CurrentValue.Mcp?.ToolCacheSeconds ?? 300;
        var allTools = new List<AITool>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                continue;
            }

            try
            {
                var tools = await GetToolsForServerAsync(server, toolCacheSeconds, ct).ConfigureAwait(false);

                // Filter by AllowedTools (empty = allow all)
                var allowedSet = BuildNameSet(server.AllowedTools, server.Name, server.PrefixToolNameWithServer);
                var filtered = allowedSet.Count == 0
                    ? tools
                    : tools.Where(t => IsAllowedTool(t, allowedSet)).ToList();

                // Wrap with auth recovery for servers with OAuth configured (retry once on 401/403)
                IList<AITool> toAdd = filtered is IList<AITool> list ? list : filtered.ToList();
                if (server.OAuth != null && _oauthHandler != null)
                {
                    var authRecoveryLogger = _loggerFactory.CreateLogger<McpAuthRecoveryToolWrapper>();
                    toAdd = McpAuthRecoveryToolWrapper.Wrap(toAdd, server.Name, _oauthHandler, _clientFactory, this, authRecoveryLogger);
                }

                // Build per-server approval options from McpServerConfig. Approval only wraps tools that are AIFunction; non-AIFunction AITool are passed through without approval.
                var approvalOptions = BuildApprovalOptions(server);
                if (approvalOptions.Enabled || _permissionEvaluator != null)
                {
                    if (_approvalHandler != null || _permissionEvaluator != null)
                    {
                        var approvalLogger = _loggerFactory.CreateLogger<ApprovalToolWrapper>();
                        var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var t in toAdd)
                        {
                            if (t.Name != null)
                            {
                                toolNameToGroup[t.Name] = "mcp:" + server.Name;
                            }
                            var originalName = GetOriginalToolName(t);
                            if (!string.IsNullOrWhiteSpace(originalName))
                            {
                                toolNameToGroup[originalName] = "mcp:" + server.Name;
                            }
                        }
                        toAdd = ApprovalToolWrapper.Wrap(toAdd, _approvalHandler, approvalOptions, _permissionEvaluator, _shellCommandAnalyzer, _executionContextAccessor, approvalLogger, toolNameToGroup, _eventBus);
                    }
                }

                // 审计：包装 AIFunction 以便在调用时记录 ServerName、ToolName、参数数量
                if (toAdd.Count > 0)
                {
                    var auditLogger = _loggerFactory.CreateLogger<McpAuditToolWrapper>();
                    toAdd = McpAuditToolWrapper.Wrap(toAdd, server.Name, auditLogger);
                }

                var serverTools = new List<string>();
                foreach (var tool in toAdd)
                {
                    var name = tool.Name ?? string.Empty;
                    if (seenNames.Add(name))
                    {
                        allTools.Add(tool);
                        serverTools.Add(name);
                    }
                    else
                    {
                        _logger.LogDebug("MCP tool '{ToolName}' from server '{ServerName}' skipped (duplicate name)", name, server.Name);
                    }
                }

                // 记录该服务器提供的工具名称
                _serverToolNames[server.Name] = serverTools;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from MCP server '{ServerName}'. Skipping.", server.Name);
            }
        }

        return allTools;
    }

    /// <summary>
    /// 失效指定服务器或全部服务器的工具缓存，下次 GetToolsAsync 调用将重新从 MCP 服务器拉取。
    /// </summary>
    /// <param name="serverName">服务器名称；null 表示失效所有缓存</param>
    public void InvalidateCache(string? serverName)
    {
        if (serverName != null)
        {
            _toolCache.TryRemove(serverName, out _);
            _serverToolNames.TryRemove(serverName, out _);
            _logger.LogDebug("Invalidated MCP tool cache for server '{ServerName}'", serverName);
        }
        else
        {
            _toolCache.Clear();
            _serverToolNames.Clear();
            _logger.LogDebug("Invalidated all MCP tool caches");
        }
    }

    /// <summary>
    /// 获取指定服务器缓存的工具名称列表。
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <returns>工具名称列表（只读）；服务器不存在或尚未缓存时返回空列表</returns>
    public IReadOnlyList<string> GetServerToolNames(string serverName)
    {
        Check.NotNullOrWhiteSpace(serverName);

        if (_serverToolNames.TryGetValue(serverName, out var names))
        {
            return names;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// 根据 McpServerConfig 构造等效的 ToolApprovalOptions，用于 ApprovalToolWrapper。
    /// </summary>
    private static ToolApprovalOptions BuildApprovalOptions(McpServerConfig server)
    {
        var alwaysRequire = ExpandNameSet(server.AlwaysRequireApprovalTools, server.Name, server.PrefixToolNameWithServer);
        var neverRequire = ExpandNameSet(server.NeverRequireApprovalTools, server.Name, server.PrefixToolNameWithServer);
        var options = new ToolApprovalOptions
        {
            Enabled = server.ApprovalMode != McpApprovalMode.NeverRequire,
            Mode = server.ApprovalMode switch
            {
                McpApprovalMode.NeverRequire => ToolApprovalMode.NeverRequire,
                McpApprovalMode.AlwaysRequire => ToolApprovalMode.AlwaysRequire,
                McpApprovalMode.Specific => ToolApprovalMode.Specific,
                _ => ToolApprovalMode.NeverRequire
            },
            AlwaysRequireApproval = alwaysRequire,
            NeverRequireApproval = neverRequire,
            AlwaysRequireApprovalGroups = new List<string>(),
            TimeoutSeconds = 300
        };
        return options;
    }

    private async Task<IReadOnlyList<AITool>> GetToolsForServerAsync(
        McpServerConfig server,
        int cacheSeconds,
        CancellationToken ct)
    {
        if (cacheSeconds > 0 && _toolCache.TryGetValue(server.Name, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Tools;
        }

        var fetchLock = _toolLocks.GetOrAdd(server.Name, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (cacheSeconds > 0 && _toolCache.TryGetValue(server.Name, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Tools;
            }

            var adapter = await _clientFactory.GetOrCreateClientAsync(server, ct).ConfigureAwait(false);
            var tools = await adapter.ListToolsAsync(ct).ConfigureAwait(false);

            if (cacheSeconds > 0)
            {
                _toolCache[server.Name] = new ToolCacheEntry(
                    tools,
                    DateTimeOffset.UtcNow.AddSeconds(cacheSeconds));
            }

            return tools;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    private static HashSet<string> BuildNameSet(
        List<string>? names,
        string serverName,
        bool prefixName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names == null || names.Count == 0)
        {
            return set;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            set.Add(name);
            if (prefixName && !name.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            {
                set.Add($"mcp:{serverName}/{name}");
            }
        }
        return set;
    }

    private static List<string> ExpandNameSet(
        List<string>? names,
        string serverName,
        bool prefixName)
    {
        return BuildNameSet(names, serverName, prefixName).ToList();
    }

    private static bool IsAllowedTool(AITool tool, HashSet<string> allowedSet)
    {
        if (allowedSet.Count == 0)
        {
            return true;
        }

        var name = tool.Name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name) && allowedSet.Contains(name))
        {
            return true;
        }

        var original = GetOriginalToolName(tool);
        return !string.IsNullOrWhiteSpace(original) && allowedSet.Contains(original);
    }

    private static string? GetOriginalToolName(AITool tool)
    {
        if (tool?.AdditionalProperties == null)
        {
            return null;
        }

        if (tool.AdditionalProperties.TryGetValue("mcp.originalName", out var value))
        {
            return value as string;
        }

        return null;
    }

    private sealed class ToolCacheEntry
    {
        public ToolCacheEntry(IReadOnlyList<AITool> tools, DateTimeOffset expiresAt)
        {
            Tools = tools;
            ExpiresAt = expiresAt;
        }

        public IReadOnlyList<AITool> Tools { get; }
        public DateTimeOffset ExpiresAt { get; }
    }
}
