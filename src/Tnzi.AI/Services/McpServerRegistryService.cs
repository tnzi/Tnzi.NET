namespace Tnzi.AI.Services;

/// <summary>
/// MCP Server 注册表服务实现 - 数据库驱动的 CRUD + 凭证加密 + 运行时联动
/// </summary>
/// <remarks>
/// AuthToken 加密策略：
///   使用 ASP.NET Core 标准的 <c>IDataProtectionProvider</c>，purpose 字符串为
///   <see cref="McpServerRegistration.AuthTokenProtectorPurpose"/>（McpServerCatalog 读取侧
///   共用同一常量）。明文凭证永远不会出现在 DTO 中（仅暴露 <c>HasAuthToken</c> 布尔标志），
///   密文也不会暴露。
///
/// 运行时绑定：
///   启用的注册条目由 <c>IMcpServerCatalog</c> 物化为运行时 MCP 服务器配置（与 AI:Mcp:Servers
///   部署配置合并，同名 DB 优先）。CRUD 成功后调用 <see cref="InvalidateRuntimeAsync"/> 失效
///   catalog 快照、连接缓存与工具缓存，使变更在本节点立即生效（多实例部署依赖 catalog 的
///   30 秒 TTL 收敛）。
///
/// 信任边界：
///   注册表仅接受 HTTP 系 transport（sse / streamable-http / http）。stdio 注册被拒绝 -
///   运行时录入本机启动命令等价于远程命令执行；stdio 服务器只能通过部署配置
///   （AI:Mcp options）声明。
///
/// TestConnectionAsync 探针策略：
///   真实连接探测 - 先做静态校验（IsEnabled / transport / URL / 凭证可解密），然后经
///   IMcpClientFactory 建立 MCP 会话并列出工具（短超时），返回成功/失败与延迟。
/// </remarks>
public class McpServerRegistryService : ApplicationService, IMcpServerRegistryService
{
    private static readonly TimeSpan TestConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly IRepository<McpServerRegistration, Guid> _repository;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IMcpClientFactory _clientFactory;
    private readonly IMcpServerCatalog _serverCatalog;
    private readonly IMcpToolProvider _toolProvider;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;

    public McpServerRegistryService(
        IRepository<McpServerRegistration, Guid> repository,
        IDataProtectionProvider dataProtectionProvider,
        IMcpClientFactory clientFactory,
        IMcpServerCatalog serverCatalog,
        IMcpToolProvider toolProvider,
        IOptionsMonitor<AIOptions> aiOptions,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _dataProtectionProvider = Check.NotNull(dataProtectionProvider);
        _clientFactory = Check.NotNull(clientFactory);
        _serverCatalog = Check.NotNull(serverCatalog);
        _toolProvider = Check.NotNull(toolProvider);
        _aiOptions = Check.NotNull(aiOptions);
    }

    /// <summary>
    /// SSRF egress 校验：除非 <c>AI:Mcp:AllowPrivateEndpoints</c> 开启，否则拒绝指向私有/内网的 ServerUrl。
    /// 返回 null 表示通过，否则返回英文阻止原因。
    /// </summary>
    private async Task<string?> CheckEgressAsync(string serverUrl, CancellationToken ct)
    {
        if (_aiOptions.CurrentValue.Mcp.AllowPrivateEndpoints)
            return null;
        return await EgressGuard.CheckAsync(serverUrl, ct);
    }

    private IDataProtector GetProtector() =>
        _dataProtectionProvider.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose);

    private static McpServerRegistrationDto ToDto(McpServerRegistration entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ServerUrl = entity.ServerUrl,
        Transport = entity.Transport,
        AuthType = entity.AuthType,
        Priority = entity.Priority,
        IsEnabled = entity.IsEnabled,
        Description = entity.Description,
        Tags = entity.Tags,
        HasAuthToken = !string.IsNullOrEmpty(entity.AuthTokenEncrypted),
        CreationTime = entity.CreationTime,
        LastModificationTime = entity.LastModificationTime
    };

    /// <summary>
    /// 校验 transport + ServerUrl（注册表仅允许 HTTP 系 transport 且 URL 必须为绝对 http(s) URI）。
    /// 返回 null 表示通过，否则返回英文错误消息。
    /// </summary>
    private static string? ValidateTransportAndUrl(string transport, string serverUrl)
    {
        if (string.Equals(transport.Trim(), "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return "Transport 'stdio' is not allowed in the runtime registry: " +
                   "stdio servers must be configured via deployment configuration (AI:Mcp options), not the runtime registry.";
        }

        if (!McpServerRegistrationMapper.IsAllowedTransport(transport))
        {
            return $"Transport '{transport}' is not supported. Allowed values: {string.Join(", ", McpServerRegistrationMapper.AllowedTransports)}.";
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Server URL must be an absolute http(s) URI.";
        }

        return null;
    }

    /// <summary>
    /// CRUD 成功后失效运行时缓存（catalog 快照 + MCP 连接 + 工具缓存），best-effort 不影响 CRUD 结果。
    /// </summary>
    private async Task InvalidateRuntimeAsync(string serverName, string? previousName = null)
    {
        try
        {
            _serverCatalog.Invalidate();
            await _clientFactory.InvalidateClientAsync(serverName, CancellationToken.None);
            _toolProvider.InvalidateCache(serverName);
            if (previousName != null && !string.Equals(previousName, serverName, StringComparison.OrdinalIgnoreCase))
            {
                await _clientFactory.InvalidateClientAsync(previousName, CancellationToken.None);
                _toolProvider.InvalidateCache(previousName);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to invalidate MCP runtime caches for server '{ServerName}'", serverName);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<McpServerRegistrationDto>>> GetPagedListAsync(McpServerRegistrationQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        try
        {
            var keyword = query.Keyword?.Trim().ToLower();
            var transport = query.Transport?.Trim();

            var queryable = _repository.AsQueryable()
                .WhereIf(p => p.Transport == transport!, !string.IsNullOrEmpty(transport))
                .WhereIf(p => p.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
                .WhereIf(
                    p => p.Name.ToLower().Contains(keyword!) || (p.Description != null && p.Description.ToLower().Contains(keyword!)),
                    !string.IsNullOrEmpty(keyword))
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.CreationTime);

            var totalCount = await queryable.CountAsync(ct);
            var items = await queryable
                .Skip((query.PageIndex - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            var dtoItems = items.Select(ToDto).ToList();
            var paged = new PagedList<McpServerRegistrationDto>(dtoItems, query.PageIndex, query.PageSize, totalCount);
            return Ok<IPagedList<McpServerRegistrationDto>>(paged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error querying MCP server registrations");
            return Fail<IPagedList<McpServerRegistrationDto>>("Failed to query MCP server registrations", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerRegistrationDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var entity = await _repository.AsQueryable().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<McpServerRegistrationDto>("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting MCP server registration {Id}", id);
            return Fail<McpServerRegistrationDto>("Failed to get MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerRegistrationDto>> CreateAsync(CreateMcpServerRegistrationDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);

        try
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return Fail<McpServerRegistrationDto>("Registration name is required", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }
            if (string.IsNullOrWhiteSpace(dto.ServerUrl))
            {
                return Fail<McpServerRegistrationDto>("Server URL is required", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }
            if (string.IsNullOrWhiteSpace(dto.Transport))
            {
                return Fail<McpServerRegistrationDto>("Transport is required", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }

            var serverUrl = dto.ServerUrl.Trim();
            var validationError = ValidateTransportAndUrl(dto.Transport, serverUrl);
            if (validationError != null)
            {
                return Fail<McpServerRegistrationDto>(validationError, 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }

            var egressError = await CheckEgressAsync(serverUrl, ct);
            if (egressError != null)
            {
                return Fail<McpServerRegistrationDto>($"Server URL rejected: {egressError}", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }

            var exists = await _repository.AsQueryable().AnyAsync(p => p.Name == name, ct);
            if (exists)
            {
                return Fail<McpServerRegistrationDto>($"MCP server registration with name '{name}' already exists", 409, ErrorCodes.McpServerRegistrationAlreadyExists);
            }

            var entity = new McpServerRegistration
            {
                Name = name,
                ServerUrl = dto.ServerUrl.Trim(),
                Transport = dto.Transport.Trim(),
                AuthType = dto.AuthType,
                Priority = dto.Priority,
                IsEnabled = dto.IsEnabled,
                Description = dto.Description,
                Tags = dto.Tags,
                AuthTokenEncrypted = string.IsNullOrEmpty(dto.AuthToken) ? null : GetProtector().Protect(dto.AuthToken)
            };

            await _repository.InsertAsync(entity);
            await InvalidateRuntimeAsync(entity.Name);
            LogInformation("Created MCP server registration {Name} (transport {Transport})", entity.Name, entity.Transport);
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating MCP server registration");
            return Fail<McpServerRegistrationDto>("Failed to create MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerRegistrationDto>> UpdateAsync(Guid id, UpdateMcpServerRegistrationDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);

        try
        {
            var entity = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<McpServerRegistrationDto>("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }

            // 校验生效后的 transport/URL（含 legacy 行：未修复非法 transport 前不允许保存）
            var effectiveTransport = string.IsNullOrWhiteSpace(dto.Transport) ? entity.Transport : dto.Transport.Trim();
            var effectiveUrl = string.IsNullOrWhiteSpace(dto.ServerUrl) ? entity.ServerUrl : dto.ServerUrl.Trim();
            var validationError = ValidateTransportAndUrl(effectiveTransport, effectiveUrl);
            if (validationError != null)
            {
                return Fail<McpServerRegistrationDto>(validationError, 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }

            var egressError = await CheckEgressAsync(effectiveUrl, ct);
            if (egressError != null)
            {
                return Fail<McpServerRegistrationDto>($"Server URL rejected: {egressError}", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }

            var previousName = entity.Name;
            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name.Trim() != entity.Name)
            {
                var newName = dto.Name.Trim();
                var conflict = await _repository.AsQueryable().AnyAsync(p => p.Id != id && p.Name == newName, ct);
                if (conflict)
                {
                    return Fail<McpServerRegistrationDto>($"MCP server registration with name '{newName}' already exists", 409, ErrorCodes.McpServerRegistrationAlreadyExists);
                }
                entity.Name = newName;
            }

            entity.ServerUrl = effectiveUrl;
            entity.Transport = effectiveTransport;
            if (dto.AuthType != null)
                entity.AuthType = dto.AuthType;
            if (dto.Priority.HasValue)
                entity.Priority = dto.Priority.Value;
            if (dto.IsEnabled.HasValue)
                entity.IsEnabled = dto.IsEnabled.Value;
            if (dto.Description != null)
                entity.Description = dto.Description;
            if (dto.Tags != null)
                entity.Tags = dto.Tags;

            // AuthToken 语义：null = 保留现有；空字符串 = 清除；非空 = 加密替换
            if (dto.AuthToken != null)
            {
                entity.AuthTokenEncrypted = string.IsNullOrEmpty(dto.AuthToken) ? null : GetProtector().Protect(dto.AuthToken);
            }

            await _repository.UpdateAsync(entity);
            await InvalidateRuntimeAsync(entity.Name, previousName);
            LogInformation("Updated MCP server registration {Id} ({Name})", entity.Id, entity.Name);
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating MCP server registration {Id}", id);
            return Fail<McpServerRegistrationDto>("Failed to update MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var entity = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }

            await _repository.DeleteAsync(entity);
            await InvalidateRuntimeAsync(entity.Name);
            LogInformation("Deleted MCP server registration {Id} ({Name})", id, entity.Name);
            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting MCP server registration {Id}", id);
            return Fail("Failed to delete MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerTestResultDto>> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var entity = await _repository.AsQueryable().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<McpServerTestResultDto>("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }

            // 静态预检：禁用/transport/URL/凭证可解密 - 提前返回更精确的失败原因，避免无谓的网络探测
            if (!entity.IsEnabled)
            {
                return TestFailure(sw, "MCP server registration is disabled");
            }

            var validationError = ValidateTransportAndUrl(entity.Transport, entity.ServerUrl);
            if (validationError != null)
            {
                return TestFailure(sw, validationError);
            }

            var egressError = await CheckEgressAsync(entity.ServerUrl, ct);
            if (egressError != null)
            {
                return TestFailure(sw, $"Server URL rejected: {egressError}");
            }

            if (string.IsNullOrEmpty(entity.AuthTokenEncrypted) &&
                !string.IsNullOrEmpty(entity.AuthType) &&
                !string.Equals(entity.AuthType, "none", StringComparison.OrdinalIgnoreCase))
            {
                return TestFailure(sw, $"Auth type '{entity.AuthType}' requires an auth token but none is configured");
            }

            McpServerConfig config;
            try
            {
                config = McpServerRegistrationMapper.ToServerConfig(entity, GetProtector().Unprotect);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to decrypt auth token for MCP server registration {Id}", id);
                return TestFailure(sw, "Stored auth token could not be decrypted (data protection key rotated?)");
            }

            // 真实连接探测：经 IMcpClientFactory 建立 MCP 会话并列出工具（短超时）。
            // 成功的连接会留在工厂缓存中（预热），失败的连接不会被缓存。
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TestConnectionTimeout);
                var adapter = await _clientFactory.GetOrCreateClientAsync(config, cts.Token);
                var tools = await adapter.ListToolsAsync(cts.Token);

                sw.Stop();
                return Ok(new McpServerTestResultDto
                {
                    Success = true,
                    Message = $"Connected to MCP server '{entity.Name}': {tools.Count} tool(s) available",
                    LatencyMs = sw.ElapsedMilliseconds
                });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return TestFailure(sw, $"Connection to MCP server timed out after {TestConnectionTimeout.TotalSeconds:0} seconds");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "MCP connection probe failed for server registration {Id} ({Name})", id, entity.Name);
                return TestFailure(sw, $"Connection failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Error testing MCP server registration {Id}", id);
            return Fail<McpServerTestResultDto>("MCP server test failed", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    private Result<McpServerTestResultDto> TestFailure(Stopwatch sw, string message)
    {
        sw.Stop();
        return Ok(new McpServerTestResultDto
        {
            Success = false,
            Message = message,
            LatencyMs = sw.ElapsedMilliseconds
        });
    }
}
