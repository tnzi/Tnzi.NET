
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 从有效 MCP 服务器（IMcpServerCatalog：部署配置 + DB 注册表合并）拉取工具，
/// 按 AllowedTools 过滤并按每服务器 ApprovalMode 做审批包装后返回。
/// 单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
/// <remarks>
/// <para>
/// 工具缓存按 <see cref="McpCacheKey"/>（租户键 + Server Name）分桶：两个租户可以注册同名 server
/// 而工具清单各不相同，只按名字缓存会把先跑的租户的清单发给后来的租户。
/// </para>
/// <para>
/// 用户专属服务器（<see cref="IUserMcpServerSource"/>）的缓存键再带上用户（<see cref="McpCacheKey.ForUser"/>）：
/// 两个用户连着同名服务器、持不同令牌时，连接、工具清单、失败冷却互不相通。这一类服务器只出现在
/// 「为该用户取工具」的那一次调用里，绝不进入别的用户的清单。
/// </para>
/// <para>
/// 缓存的工具绑定在列出它们的那条连接上（SDK 的 <see cref="McpClientTool"/> 持有自己的 McpClient）。
/// 命中缓存时仍向 <see cref="IMcpClientFactory"/> 取当前连接（30 秒一次健康检查）：连接已被换掉
/// （子进程崩溃、HTTP 会话丢失后重连），就从新连接重新列出，而不是把绑在死连接上的工具一直发到缓存过期。
/// </para>
/// </remarks>
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
    private readonly IUserMcpServerSource? _userServerSource;
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
        IEventBus? eventBus = null,
        IUserMcpServerSource? userServerSource = null)
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
        _userServerSource = userServerSource;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
        // 当前运行的用户：与 ApprovalToolWrapper 发审批请求、权限规则按人匹配是同一个来源（AgentRunRequest.UserId）
        => GetToolsAsync(_executionContextAccessor?.CurrentRequest?.UserId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AITool>> GetToolsAsync(Guid? userId, CancellationToken ct = default)
    {
        // 有效服务器 = 部署配置 + DB 注册表合并（catalog 内部处理 AI:Mcp:Enabled 总开关），再加上这个用户自己的服务器
        var catalogServers = await _serverCatalog.GetEffectiveServersAsync(ct).ConfigureAwait(false);
        var servers = new List<McpServerConfig>(catalogServers);
        if (userId.HasValue)
        {
            servers.AddRange(await GetUserServersAsync(userId.Value, ct).ConfigureAwait(false));
        }

        return await BuildToolsAsync(servers, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AITool>> GetUserToolsAsync(Guid userId, CancellationToken ct = default)
        => await BuildToolsAsync(await GetUserServersAsync(userId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>对一组服务器取工具：过滤、认证恢复 / 审批 / 审计包装、按名去重（先出现者胜）。单台不可用只跳过它。</summary>
    private async Task<IReadOnlyList<AITool>> BuildToolsAsync(IReadOnlyList<McpServerConfig> servers, CancellationToken ct)
    {
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
                if ((server.OAuth != null && _oauthHandler != null) || server is UserMcpServerConfig)
                {
                    var authRecoveryLogger = _loggerFactory.CreateLogger<McpAuthRecoveryToolWrapper>();
                    toAdd = McpAuthRecoveryToolWrapper.Wrap(toAdd, server, _oauthHandler, _clientFactory, this, authRecoveryLogger);
                }

                // Build per-server approval options from McpServerConfig. Approval only wraps tools that are AIFunction; non-AIFunction AITool are passed through without approval.
                var approvalOptions = BuildApprovalOptions(server);
                // 审批开着而没有审批处理器时同样包装：ApprovalToolWrapper 把 Ask 决策失败关闭地拒掉，
                // 不包装就等于让这台服务器上需要审批的工具直接执行
                if (approvalOptions.Enabled || _permissionEvaluator != null)
                {
                    var approvalLogger = _loggerFactory.CreateLogger<ApprovalToolWrapper>();
                    var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var t in toAdd)
                    {
                        if (t.Name != null)
                        {
                            toolNameToGroup[t.Name] = "mcp:" + server.Name;
                        }
                        var originalName = McpToolNames.OriginalName(t);
                        if (!string.IsNullOrWhiteSpace(originalName))
                        {
                            toolNameToGroup[originalName] = "mcp:" + server.Name;
                        }
                    }
                    toAdd = ApprovalToolWrapper.Wrap(toAdd, _approvalHandler, approvalOptions, _permissionEvaluator, _shellCommandAnalyzer, _executionContextAccessor, approvalLogger, toolNameToGroup, _eventBus);
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

                // 记录该服务器提供的工具名称（按租户分桶，同名 server 的两个租户各存一份）
                _serverToolNames[McpCacheKey.For(server)] = serverTools;
            }
            catch (McpServerUnavailableException ex)
            {
                // 冷却期内：失败原因已在建连那一次记过警告，每次运行不再重复
                _logger.LogDebug(ex, "MCP server '{ServerName}' is cooling down after a failed connection. Skipping.", server.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from MCP server '{ServerName}'. Skipping.", server.Name);
            }
        }

        return allTools;
    }

    /// <summary>
    /// 取这个用户的专属服务器并盖上用户。清单来自消费方，框架不信任它携带的任何隔离信息：
    /// 用户 id 由这里盖，不合规的条目（见 <see cref="UserMcpServerConfig"/>）跳过并记警告，
    /// 整个来源抛异常按空清单处理（失败关闭：少给工具，不给错工具）。
    /// </summary>
    private async Task<IReadOnlyList<UserMcpServerConfig>> GetUserServersAsync(Guid userId, CancellationToken ct)
    {
        if (_userServerSource is null || _options.CurrentValue.Mcp?.Enabled != true)
        {
            return [];
        }

        IReadOnlyList<UserMcpServerConfig> declared;
        try
        {
            declared = await _userServerSource.GetServersAsync(userId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "The user MCP server source failed; this run gets no user-scoped MCP servers");
            return [];
        }

        var result = new List<UserMcpServerConfig>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in declared ?? [])
        {
            try
            {
                var stamped = server.ForUser(userId);
                if (names.Add(stamped.Name))
                {
                    result.Add(stamped);
                }
                else
                {
                    _logger.LogWarning("User-scoped MCP server '{ServerName}' is declared twice; keeping the first", stamped.Name);
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("A user-scoped MCP server was skipped: {Reason}", ex.Message);
            }
        }

        return result;
    }

    /// <summary>
    /// 失效<b>这一份配置</b>的工具缓存（分区键精确匹配）。用户专属服务器用它：按名字失效会把别的用户同名服务器的缓存也清掉。
    /// </summary>
    public void InvalidateServerCache(McpServerConfig server)
    {
        Check.NotNull(server);
        var key = McpCacheKey.For(server);
        _toolCache.TryRemove(key, out _);
        _serverToolNames.TryRemove(key, out _);
    }

    /// <summary>
    /// 失效某个用户的用户专属服务器的工具缓存；<paramref name="serverName"/> 为 null = 该用户的全部。
    /// </summary>
    public void InvalidateUserCache(Guid userId, string? serverName = null)
    {
        foreach (var key in _toolCache.Keys.Where(k => McpCacheKey.BelongsToUser(k, userId, serverName)).ToList())
        {
            _toolCache.TryRemove(key, out _);
        }

        foreach (var key in _serverToolNames.Keys.Where(k => McpCacheKey.BelongsToUser(k, userId, serverName)).ToList())
        {
            _serverToolNames.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 失效指定服务器或全部服务器的工具缓存，下次 GetToolsAsync 调用将重新从 MCP 服务器拉取。
    /// </summary>
    /// <param name="serverName">
    /// 服务器名称；null 表示失效所有缓存。按名字失效会清掉<b>所有租户</b>下叫这个名字的条目 ——
    /// 调用方（注册表 CRUD / 401 恢复）只带得出名字，而本类是 Singleton 没有租户上下文；
    /// 多失效一个只是重新拉一次工具清单，漏失效一个是继续用过期清单且毫无症状。
    /// </param>
    public void InvalidateCache(string? serverName)
    {
        if (serverName != null)
        {
            foreach (var key in _toolCache.Keys.Where(k => McpCacheKey.MatchesServerName(k, serverName)).ToList())
            {
                _toolCache.TryRemove(key, out _);
            }
            foreach (var key in _serverToolNames.Keys.Where(k => McpCacheKey.MatchesServerName(k, serverName)).ToList())
            {
                _serverToolNames.TryRemove(key, out _);
            }
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
    /// 获取指定服务器缓存的工具名称列表（诊断用）。
    /// </summary>
    /// <param name="serverName">
    /// 服务器名称。只解析<b>无租户维度</b>的条目（部署配置 <c>AI:Mcp:Servers</c> 来源）。
    /// DB 注册表来源的条目（含宿主级注册行）按租户分桶，裸名字寻址不到它们：这里返回空列表，
    /// 而不是随便挑一个租户的清单答给调用方。
    /// </param>
    /// <returns>工具名称列表（只读）；服务器不存在、尚未缓存或带租户维度时返回空列表</returns>
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
        var cacheKey = McpCacheKey.For(server);

        if (cacheSeconds > 0 && _toolCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow
            && await IsCurrentConnectionAsync(server, cached, ct).ConfigureAwait(false))
        {
            return cached.Tools;
        }

        var fetchLock = _toolLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var adapter = await _clientFactory.GetOrCreateClientAsync(server, ct).ConfigureAwait(false);

            if (cacheSeconds > 0 && _toolCache.TryGetValue(cacheKey, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow
                && ReferenceEquals(cached.Adapter, adapter))
            {
                return cached.Tools;
            }

            var tools = await adapter.ListToolsAsync(ct).ConfigureAwait(false);

            if (cacheSeconds > 0)
            {
                _toolCache[cacheKey] = new ToolCacheEntry(
                    tools,
                    adapter,
                    DateTimeOffset.UtcNow.AddSeconds(cacheSeconds));
            }

            return tools;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    /// <summary>
    /// 缓存的工具是否还绑在工厂当前的那条连接上。工厂换了连接（重连）时这些工具调用的是已释放的客户端。
    /// 建连失败照常抛出，由调用方按「这台服务器暂不可用」跳过。
    /// </summary>
    private async Task<bool> IsCurrentConnectionAsync(McpServerConfig server, ToolCacheEntry cached, CancellationToken ct)
    {
        var current = await _clientFactory.GetOrCreateClientAsync(server, ct).ConfigureAwait(false);
        if (ReferenceEquals(current, cached.Adapter))
        {
            return true;
        }

        _logger.LogInformation("MCP server '{ServerName}' was reconnected; listing its tools again from the new connection", server.Name);
        return false;
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
            // 名单可以写服务器的原名，也可以写加了前缀的名字（模型看到的那个），两种都认
            set.Add(name);
            if (prefixName)
            {
                set.Add(McpToolNames.Prefixed(serverName, name));
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

        var original = McpToolNames.OriginalName(tool);
        return !string.IsNullOrWhiteSpace(original) && allowedSet.Contains(original);
    }

    /// <summary>缓存项：工具清单 + 列出它们的那条连接（工具绑在它上面）+ 过期时间</summary>
    private sealed record ToolCacheEntry(IReadOnlyList<AITool> Tools, IMcpClientAdapter Adapter, DateTimeOffset ExpiresAt);
}
