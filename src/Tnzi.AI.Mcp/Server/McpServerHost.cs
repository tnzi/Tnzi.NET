



namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server Host 实现 - 将 Agent 和自定义工具暴露为 MCP Server
/// </summary>
public partial class McpServerHost : IMcpServerHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly ILogger<McpServerHost> _logger;
    private readonly McpServerSecurityMiddleware _security;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    // 已注册的 Agent 工具
    private readonly ConcurrentDictionary<Guid, AgentToolRegistration> _agentTools = new();

    // 已从 AI:McpServer:ExposedAgentIds 导入过的 Agent。每个配置项只导入一次：管理端移除之后不再被它加回来。
    private readonly ConcurrentDictionary<Guid, byte> _importedConfiguredAgentIds = new();

    // 已注册的自定义工具
    private readonly ConcurrentDictionary<string, CustomToolRegistration> _customTools = new(StringComparer.OrdinalIgnoreCase);

    // 构建结果缓存：避免每次 tools/list 都对每个 Agent 重新建 scope + GetByIdAsync（N+1）。
    // 失效驱动（ExposeAgent/RemoveAgent/ExposeTool bump 版本号）为主，TTL 兜底防止配置外漂移。
    // ★ 按调用方的有效租户分区（键：租户 ID，宿主 = Guid.Empty）：Agent 是多租户实体，同一份注册表在不同租户下
    // 解析出的工具集不同（租户 A 的 agent 在租户 B 与宿主下根本不存在），工具名 → AgentId 的映射同理。
    // 此前只有一份不分租户的缓存，在谁的上下文里建、就把谁看得见的 agent 名称与描述发给此后 30 秒内的所有调用方。
    private static readonly TimeSpan ToolCacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private long _registrationVersion;          // 每次 expose/remove 自增
    private readonly ConcurrentDictionary<Guid, ToolSet> _toolSets = new();

    /// <summary>
    /// Bumps the registration version and drops every built tool set, so the next
    /// <see cref="ListToolsAsync"/>/<see cref="CallToolAsync"/> rebuilds. Called whenever the set of
    /// exposed agents or custom tools changes.
    /// </summary>
    private void InvalidateToolCache()
    {
        Interlocked.Increment(ref _registrationVersion);
        _toolSets.Clear();
    }

    /// <summary>
    /// Agent 工具的输入：一条发给 Agent 的消息。运行时按 <c>message</c> → <c>input</c> → 唯一参数的顺序取（见 <see cref="ExtractMessage"/>）。
    /// </summary>
    private static readonly JsonElement AgentToolInputSchema = ParseSchema(
        "{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\",\"description\":\"The message to send to the agent.\"}},\"required\":[\"message\"]}");

    /// <summary>
    /// 自定义工具没有声明 schema 时对外宣告的输入：一个字符串参数 <c>input</c>。
    /// handler 收到的是 <c>tools/call</c> 的整份参数对象，即 <c>{"input": "..."}</c>。
    /// </summary>
    internal static readonly JsonElement DefaultCustomToolInputSchema = ParseSchema(
        "{\"type\":\"object\",\"properties\":{\"input\":{\"type\":\"string\"}},\"required\":[\"input\"]}");

    /// <summary><c>tools/call</c> 没带参数时交给 handler 的参数对象。</summary>
    private static readonly JsonElement EmptyArguments = ParseSchema("{}");

    /// <summary>工具以错误结果返回时写进审计的最大长度（<c>McpToolUsageRecord.ErrorMessage</c> 列宽 2000，留出余量）。</summary>
    private const int MaxAuditedErrorLength = 1000;

    /// <summary>被取消的调用写进审计的那句话。</summary>
    private const string CancelledCallMessage = "The call was cancelled: the MCP request was cancelled or the client disconnected.";

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public McpServerHost(
        IServiceProvider serviceProvider,
        IOptionsMonitor<McpServerOptions> options,
        ILogger<McpServerHost> logger,
        McpServerSecurityMiddleware security,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _security = Check.NotNull(security);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// 从当前 HTTP 请求上下文中读取调用方哈希摘要（由 <see cref="McpServerHttpSecurityMiddleware"/> 存入）。
    /// </summary>
    private string? GetCallerHash() =>
        _httpContextAccessor?.HttpContext?.Items[McpServerSecurityMiddleware.CallerHashItemKey] as string;

    /// <summary>
    /// 当前请求的调用面（由 <see cref="McpServerHttpSecurityMiddleware"/> 存入）。
    /// 没有 HTTP 上下文（进程内调用）时不受限 —— 那里没有外部调用方。
    /// </summary>
    /// <remarks>
    /// 调用面的 <see cref="McpCallerScope.TenantId"/> 就是本次请求的<b>有效租户</b>：运行范围凭据 = 凭据签发时的租户，
    /// 静态 API Key / 进程内 = 宿主（null）。列工具、按名解析、执行三处都在它之下进行，且都显式切换。
    /// 不继承请求头（<c>X-Tenant-Id</c>）或 JWT 带来的环境租户：那是客户端可控的输入，不是 MCP 调用方的身份。
    /// </remarks>
    private McpCallerScope GetCallerScope() =>
        _httpContextAccessor?.HttpContext?.Items[McpServerSecurityMiddleware.CallerScopeItemKey] as McpCallerScope
        ?? McpCallerScope.Unrestricted;

    /// <summary>
    /// 为工具调用构造限流键，<b>按调用方分区</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 此前的键是 <c>agent:{agentId}</c> / <c>tool:{name}</c> —— 不带任何调用方维度，
    /// 而它们与 HTTP 请求桶共用同一个 <c>McpServerSecurityMiddleware</c> 单例、同一个字典、
    /// 同一个 <c>RateLimitPerMinute</c> 阈值。于是客户端 A 打满 agent X 的桶之后，
    /// 客户端 B（哪怕来自另一个租户、拿着另一把 API Key）对同一个 agent 的调用一律 429：
    /// 一个普通客户端就能拒绝掉所有人对某个 agent 的访问。
    /// </para>
    /// <para>
    /// 分区口径与 <c>BuildClientKey</c> 保持一致（只有调用方摘要，不含任何客户端自报的段），
    /// 这样两个桶按同一个身份切分。没有 HTTP 上下文时落到单一 <c>local</c> 分区 —— 那里本来就
    /// 只有一个调用方。
    /// </para>
    /// </remarks>
    private string BuildToolRateLimitKey(string bucket)
    {
        var caller = GetCallerHash();
        return caller is null ? $"local:{bucket}" : $"{caller}:{bucket}";
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetCustomToolNames() => [.. _customTools.Keys];

    /// <inheritdoc />
    /// <remarks>
    /// 对外宣告的输入是 <see cref="DefaultCustomToolInputSchema"/>（一个字符串参数 <c>input</c>），
    /// handler 收到 <c>{"input": "..."}</c>。要别的参数形状，用带 schema 的重载声明。
    /// </remarks>
    public void ExposeTool(string name, string description, Func<JsonElement, Task<string>> handler)
        => ExposeTool(name, description, DefaultCustomToolInputSchema, handler);

    /// <inheritdoc />
    /// <remarks>
    /// ★ 宣告与运行时必须是同一个形状：<c>tools/list</c> 发出去的就是这里声明的 schema，<c>tools/call</c> 收到的参数对象原样交给 handler。
    /// 此前 schema 由一个从不执行的委托反推（单个字符串参数 <c>input</c>），handler 却收到整份参数对象：
    /// 按宣告调用的客户端发 <c>{"input": ...}</c>，按别的字段取值的 handler 抛异常，客户端只看到一句内部错误。
    /// 名字与 schema 在注册时就校验（名字按 SDK 的规则，schema 的顶层必须是 object），不合规当场抛出，而不是到构建工具列表时被静默跳过。
    /// </remarks>
    public void ExposeTool(string name, string description, JsonElement inputSchema, Func<JsonElement, Task<string>> handler)
    {
        Check.NotNull(handler);

        // 字符串 handler 是完整形态的一个特例：只看参数、不要取消令牌，返回值作为一个文本块（与以前同一个结果形状）
        ExposeTool(new McpToolDefinition
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema,
            Handler = async (call, _) => CreateTextResult(await handler(call.Arguments), isError: false)
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// 名字与 schema 在注册时就校验（名字按 SDK 的规则，schema 的顶层必须是 object），不合规当场抛出，而不是到构建工具列表时被静默跳过。
    /// </remarks>
    public void ExposeTool(McpToolDefinition tool)
    {
        Check.NotNull(tool);
        Check.NotNullOrWhiteSpace(tool.Name);
        Check.NotNull(tool.Handler);

        if (!McpToolNames.IsValidCustomToolName(tool.Name))
        {
            throw new ArgumentException(
                $"MCP tool name '{tool.Name}' is invalid: use 1-128 characters from A-Z, a-z, 0-9, '_', '-' and '.'.", nameof(tool));
        }

        // Tool.InputSchema 的 setter 按协议校验（顶层 type 必须是 object），不合规抛 ArgumentException
        var schema = new Tool { Name = tool.Name, InputSchema = tool.InputSchema.Clone() }.InputSchema;

        _customTools[tool.Name] = new CustomToolRegistration(tool.Name, tool.Description, schema, tool.Handler);
        InvalidateToolCache();
        _logger.LogInformation("Registered custom MCP tool '{ToolName}'", tool.Name);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfiguredAgentsExposed();
        var scope = GetCallerScope();
        var toolSet = await GetToolSetAsync(scope.TenantId, cancellationToken);
        // 运行范围调用方只看得见凭据点名的工具：看不见 == 不存在
        var tools = toolSet.Tools.Where(x => scope.AllowsTool(x.Name)).ToList();
        if (scope.AllowsUserMcpTools)
        {
            // 平台自己的工具名是保留的（不论这枚凭据能不能调它）：用户连接的第三方服务器不能顶掉或冒充它们
            var taken = new HashSet<string>(toolSet.Tools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            tools.AddRange((await ListUserToolsAsync(scope, cancellationToken)).Where(t => taken.Add(t.Name)).Select(t => t.Tool));
        }

        return tools;
    }

    /// <inheritdoc />
    public async Task<CallToolResult> CallToolAsync(
        string name,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(name);
        EnsureConfiguredAgentsExposed();

        var scope = GetCallerScope();
        var staticAllowed = scope.AllowsTool(name);
        if (!staticAllowed && !scope.AllowsUserMcpTools)
        {
            // 越界调用是 MCP 错误结果而不是 401：凭据本身有效，只是这把钥匙开不了这扇门。
            // 与「工具不存在」给同一句话的形状 —— 运行范围调用方看不见的工具就是不存在的工具。
            _logger.LogWarning("MCP tool call to '{ToolName}' refused: outside the caller's run-scoped allow-list", name);
            await _security.AuditLogAsync(name, null, 0, false, "Tool is not permitted for this credential",
                callerApiKeyId: GetCallerHash(), ct: cancellationToken);
            return CreateErrorResult($"Tool '{name}' is not exposed by MCP Server.");
        }

        // 凭据没点名这个工具时只剩转发用户服务器工具这一条路：平台自己的工具（自定义 / Agent）不会因此变得可调
        if (staticAllowed)
        {
            if (_customTools.TryGetValue(name, out var customTool))
            {
                return await InvokeCustomToolAsync(customTool, arguments, scope, cancellationToken);
            }

            var agentRegistration = await ResolveAgentToolAsync(name, scope.TenantId, cancellationToken);
            if (agentRegistration != null)
            {
                var message = ExtractMessage(arguments);
                return await InvokeAgentAsync(agentRegistration.AgentId, name, message, scope.TenantId, cancellationToken);
            }
        }

        if (scope.AllowsUserMcpTools && await RelayToUserServerAsync(name, arguments, scope, cancellationToken) is { } relayed)
        {
            return relayed;
        }

        return CreateErrorResult($"Tool '{name}' is not exposed by MCP Server.");
    }

    /// <summary>
    /// 把配置里预暴露的 Agent 导入注册表。每个配置项只导入一次。
    /// </summary>
    /// <remarks>
    /// ★ 此前每次 <c>tools/list</c> / <c>tools/call</c> 都把「配置里有、注册表里没有」的 Agent 加回去：
    /// 管理端移除一个配置项里的 Agent，接口答 200、已暴露列表也不再有它，外部客户端的下一次 <c>tools/list</c>
    /// （或管理页刷新状态）就把它原样加了回来，紧急下线手段无效且外观正常。现在移除维持到进程重启，
    /// 永久下线要同时改配置；运行期热更新新增的配置项照常导入。
    /// </remarks>
    private void EnsureConfiguredAgentsExposed()
    {
        foreach (var agentId in _options.CurrentValue.ExposedAgentIds)
        {
            if (_importedConfiguredAgentIds.TryAdd(agentId, 0) && !_agentTools.ContainsKey(agentId))
            {
                ExposeAgent(agentId);
            }
        }
    }

    /// <inheritdoc />
    public bool IsConfiguredAgent(Guid agentId) => _options.CurrentValue.ExposedAgentIds.Contains(agentId);

    /// <summary>
    /// 取某个有效租户下的工具集（带缓存）。失效驱动（注册版本号）为主，30s TTL 兜底。
    /// 避免每次 tools/list / 冷缓存 tools/call 都对每个 Agent 重新建 scope + 查库（N+1）。
    /// </summary>
    private async Task<ToolSet> GetToolSetAsync(Guid? tenantId, CancellationToken ct)
    {
        var key = tenantId ?? Guid.Empty;

        // 快路径：版本未变且 TTL 未过期，直接返回缓存
        if (_toolSets.TryGetValue(key, out var cached) && cached.IsFresh(Interlocked.Read(ref _registrationVersion)))
        {
            return cached;
        }

        await _buildLock.WaitAsync(ct);
        try
        {
            // 双检：可能已有其他请求在等锁期间重建。版本在构建之前取：构建期间注册表又变了，
            // 这份结果带着旧版本号入缓存，下一次读取就会重建
            var version = Interlocked.Read(ref _registrationVersion);
            if (_toolSets.TryGetValue(key, out cached) && cached.IsFresh(version))
            {
                return cached;
            }

            var built = await BuildToolSetAsync(tenantId, version, ct);
            _toolSets[key] = built;
            return built;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <summary>
    /// 在指定有效租户下实际构建全部 MCP 工具（无缓存），连同该租户的工具名 → AgentId 映射。
    /// </summary>
    private async Task<ToolSet> BuildToolSetAsync(Guid? tenantId, long version, CancellationToken ct)
    {
        var specs = new List<AgentToolSpec>();
        foreach (var (agentId, registration) in _agentTools)
        {
            try
            {
                if (await LoadAgentToolSpecAsync(agentId, registration, tenantId) is { } spec)
                {
                    specs.Add(spec);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build MCP tool for Agent '{AgentId}'. Skipping.", agentId);
            }
        }

        var customTools = _customTools.Values.ToList();
        var tools = new List<Tool>();
        var agentToolNames = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var (spec, toolName) in AssignAgentToolNames(specs, customTools.Select(t => t.Name)))
        {
            if (!agentToolNames.TryAdd(toolName, spec.AgentId))
            {
                // 区分后缀之后仍撞名（另一个 Agent 恰好就叫这个名字）：不列出，免得一个名字指向两个 Agent
                _logger.LogWarning(
                    "MCP tool name '{ToolName}' for Agent '{AgentId}' is still taken after disambiguation. Skipping.",
                    toolName, spec.AgentId);
                continue;
            }

            tools.Add(new Tool { Name = toolName, Description = spec.Description, InputSchema = AgentToolInputSchema });
        }

        foreach (var custom in customTools)
        {
            tools.Add(new Tool { Name = custom.Name, Description = custom.Description, InputSchema = custom.InputSchema });
        }

        return new ToolSet(tools, agentToolNames, version, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>
    /// 给每个 Agent 工具定下最终的名字：同名（不分大小写）的 Agent 之间、与自定义工具之间，撞名的每一个 Agent 都加上区分后缀。
    /// </summary>
    /// <remarks>
    /// 此前同名的两个 Agent 在列表里出现两个同名工具，按名解析后写覆盖（字典遍历顺序不确定），
    /// 与自定义工具同名的 Agent 则永远调不到（按名调用先查自定义工具）。后缀取自 Agent 自己的 Id，所以名字在重建之间是稳定的。
    /// </remarks>
    private IEnumerable<(AgentToolSpec Spec, string ToolName)> AssignAgentToolNames(
        IReadOnlyList<AgentToolSpec> specs, IEnumerable<string> customToolNames)
    {
        var customNames = new HashSet<string>(customToolNames, StringComparer.OrdinalIgnoreCase);
        var counts = specs
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs.OrderBy(s => s.AgentId))
        {
            if (counts[spec.Name] == 1 && !customNames.Contains(spec.Name))
            {
                yield return (spec, spec.Name);
                continue;
            }

            var disambiguated = McpToolNames.Disambiguate(spec.Name, spec.AgentId);
            _logger.LogWarning(
                "MCP tool name '{ToolName}' is used by more than one tool; Agent '{AgentId}' is exposed as '{Disambiguated}'",
                spec.Name, spec.AgentId, disambiguated);
            yield return (spec, disambiguated);
        }
    }

    /// <summary>
    /// 执行自定义工具，经统一守卫：<c>tools/call</c> 的参数对象原样交给 handler（与宣告的 schema 同一个形状），
    /// 连同调用方与一个切到调用方有效租户的 DI 作用域（静态 key 切到宿主，与 Agent 工具同一口径）。
    /// </summary>
    private Task<CallToolResult> InvokeCustomToolAsync(
        CustomToolRegistration registration,
        IDictionary<string, JsonElement>? arguments,
        McpCallerScope caller,
        CancellationToken cancellationToken)
    {
        var payload = arguments == null
            ? EmptyArguments
            : JsonSerializer.SerializeToElement(arguments);

        return ExecuteWithGuardsAsync(registration.Name, agentId: null, async ct =>
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            using var tenantScope = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(caller.TenantId);

            var call = new McpToolCallContext
            {
                ToolName = registration.Name,
                Arguments = payload,
                Services = scope.ServiceProvider,
                Caller = caller
            };

            return await registration.Handler(call, ct)
                   ?? throw new InvalidOperationException($"MCP tool '{registration.Name}' returned no result.");
        }, cancellationToken);
    }

    /// <summary>
    /// 单一执行守卫：速率限制 → 执行 → 异常映射 → 审计/分析记录。
    /// Agent 与自定义工具的所有真实调用均经此处，消除多路径下重复的限流/审计逻辑。
    /// <para>
    /// 成功/失败看结果的 <c>IsError</c>（抛异常也落成 <c>IsError=true</c>），而非对结果文本嗅探
    /// <c>"Error:"</c> 前缀——避免合法工具结果恰好以 <c>"Error:"</c> 开头时被误判。
    /// </para>
    /// <para>
    /// 交给 <paramref name="execute"/> 的令牌在 MCP 请求被取消或客户端断开时触发。取消不映射成错误结果而是原样抛出：
    /// 已经没有人在等这个结果（协议规定被取消的请求不再应答），审计照样记下一条失败。
    /// </para>
    /// </summary>
    /// <returns>工具结果；异常时为 <c>"Error: ..."</c> 文本（业务异常透出消息，内部错误用通用提示），<c>IsError=true</c>。</returns>
    private async Task<CallToolResult> ExecuteWithGuardsAsync(
        string toolName,
        Guid? agentId,
        Func<CancellationToken, Task<CallToolResult>> execute,
        CancellationToken ct)
    {
        var rateLimitKey = BuildToolRateLimitKey(agentId.HasValue ? $"agent:{agentId.Value}" : $"tool:{toolName}");
        using var callCancellation = LinkToClientConnection(ct);
        var callToken = callCancellation?.Token ?? ct;
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var isSuccess = false;

        try
        {
            if (!_security.CheckRateLimit(rateLimitKey))
            {
                throw new RateLimitException("Rate limit exceeded");
            }

            var result = await execute(callToken);
            isSuccess = result.IsError != true;
            errorMessage = isSuccess ? null : DescribeErrorResult(result);
            return result;
        }
        catch (OperationCanceledException) when (callToken.IsCancellationRequested)
        {
            errorMessage = CancelledCallMessage;
            _logger.LogInformation("MCP tool call to '{ToolName}' was cancelled after {ElapsedMs} ms", toolName, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "MCP tool call failed for '{ToolName}'", toolName);
            // 仅暴露业务异常消息，内部错误使用通用提示
            var text = ex is BusinessException
                ? $"Error: {ex.Message}"
                : "Error: An internal error occurred while processing the request.";
            return CreateTextResult(text, isError: true);
        }
        finally
        {
            sw.Stop();
            // 不带调用令牌写：被取消的调用同样要留下记录，带着已取消的令牌去写只会让这条记录丢掉
            await _security.AuditLogAsync(
                toolName, agentId, sw.ElapsedMilliseconds, isSuccess, errorMessage,
                callerApiKeyId: GetCallerHash(), ct: CancellationToken.None);
        }
    }

    /// <summary>
    /// 把调用令牌与承载这次调用的 HTTP 请求连起来：客户端断开（外部运行被取消、进程被杀）时工具一并取消。
    /// </summary>
    /// <remarks>
    /// SDK 交来的令牌只在客户端发 <c>notifications/cancelled</c> 或会话结束时触发；承载 <c>tools/call</c> 的 POST 被中断时它不触发，
    /// 一个要跑十几分钟的工具会在没有任何人等结果的情况下跑完。没有 HTTP 上下文（进程内调用）时只用调用方给的令牌。
    /// </remarks>
    private CancellationTokenSource? LinkToClientConnection(CancellationToken ct)
    {
        var requestAborted = _httpContextAccessor?.HttpContext?.RequestAborted ?? CancellationToken.None;
        return requestAborted.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(ct, requestAborted) : null;
    }

    /// <summary>
    /// 工具以 <c>IsError=true</c> 返回（而不是抛出）时写进审计的那句话：第一个文本块，截到审计列放得下的长度。
    /// </summary>
    private static string DescribeErrorResult(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return string.IsNullOrWhiteSpace(text)
            ? "The tool returned an error result."
            : text.FitToLength(MaxAuditedErrorLength);
    }

    /// <summary>
    /// 在调用方的有效租户下按工具名解析 Agent：与 tools/list 同一份工具集，列不出来的就调不动（失败关闭）。
    /// 冷缓存时（之前没有 tools/list）现建一次。
    /// </summary>
    private async Task<AgentToolMatch?> ResolveAgentToolAsync(string toolName, Guid? tenantId, CancellationToken cancellationToken)
    {
        var toolSet = await GetToolSetAsync(tenantId, cancellationToken);
        return toolSet.AgentToolNames.TryGetValue(toolName, out var agentId)
            ? new AgentToolMatch(agentId, toolName)
            : null;
    }

    private static string ExtractMessage(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return string.Empty;
        }

        if (arguments.TryGetValue("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String)
        {
            return messageElement.GetString() ?? string.Empty;
        }

        if (arguments.TryGetValue("input", out var inputElement)
            && inputElement.ValueKind == JsonValueKind.String)
        {
            return inputElement.GetString() ?? string.Empty;
        }

        if (arguments.Count == 1)
        {
            var onlyValue = arguments.Values.First();
            return onlyValue.ValueKind == JsonValueKind.String
                ? onlyValue.GetString() ?? string.Empty
                : onlyValue.GetRawText();
        }

        return JsonSerializer.Serialize(arguments);
    }

    private static CallToolResult CreateTextResult(string text, bool isError)
    {
        var result = new CallToolResult
        {
            IsError = isError,
            Content =
            [
                new TextContentBlock { Text = text }
            ]
        };

        if (TryParseJson(text, out var structuredContent))
        {
            result.StructuredContent = structuredContent;
        }

        return result;
    }

    private static CallToolResult CreateErrorResult(string message) => CreateTextResult(message, isError: true);

    private static bool TryParseJson(string value, out JsonElement structuredContent)
    {
        try
        {
            structuredContent = JsonDocument.Parse(value).RootElement.Clone();
            return true;
        }
        catch
        {
            structuredContent = default;
            return false;
        }
    }

    private sealed record AgentToolRegistration(Guid AgentId, McpToolExposureOptions? Options);

    private sealed record CustomToolRegistration(
        string Name, string Description, JsonElement InputSchema, Func<McpToolCallContext, CancellationToken, Task<CallToolResult>> Handler);

    private sealed record AgentToolMatch(Guid AgentId, string ToolName);

    /// <summary>在某个有效租户下解析出的一个 Agent 工具：名字（未去重）与描述。</summary>
    private sealed record AgentToolSpec(Guid AgentId, string Name, string Description);

    /// <summary>某个有效租户下构建出的工具集，连同工具名 → AgentId 映射与它对应的注册版本。</summary>
    private sealed record ToolSet(List<Tool> Tools, IReadOnlyDictionary<string, Guid> AgentToolNames, long Version, long BuiltAtTicks)
    {
        public bool IsFresh(long currentVersion)
            => Version == currentVersion && DateTimeOffset.UtcNow.UtcTicks - BuiltAtTicks < ToolCacheTtl.Ticks;
    }
}
