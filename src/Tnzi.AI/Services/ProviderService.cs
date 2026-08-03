
namespace Tnzi.AI.Services;

/// <summary>
/// AI Provider 实体 CRUD 服务实现
/// </summary>
/// <remarks>
/// API Key 加密策略：
///   使用 ASP.NET Core 标准的 <c>IDataProtectionProvider</c>，purpose 字符串为
///   "Tnzi.AI.Providers.ApiKey"。框架程序集没有现成的加密 helper 类，这里直接使用
///   ASP.NET Core 标准原语，避免引入额外抽象。明文 API Key 永远不会出现在 ProviderDto 中
///   （仅暴露 HasApiKey 布尔标志），密文也不会暴露。
///
/// TestConnectionAsync 探针策略：
///   默认采用 "shallow probe" - 仅校验记录基础信息（存在性、IsEnabled、Endpoint 解析）
///   并测量内部解密/校验耗时。不发起真实 LLM 调用以避免计费和延迟波动。
///   未来若需要对接真实 IChatClientFactory 探针，可在此扩展。
///
/// 可见性策略（ResourceScope）：
///   Provider 有意不实现 IMultiTenant - 全局查询过滤器无共享行子句，System 行对真实租户不可见。
///   取而代之，每个 Provider 携带 Scope + TenantId，服务层联合过滤：
///     - 多租户模式（currentTenantId != null）：返回 Scope=System ∪ (Scope=Tenant &amp;&amp; TenantId==当前租户)
///     - 单租户模式（currentTenantId == null）：返回全部行（向后兼容）
///   此模式镜像 SkillEntity / DatabaseSkillStore 的实现。
///
/// 双源列表（Database + Configuration）：
///   admin 列表/详情/options 端点合并两类来源 - 数据库实体（可写）与 appsettings 的
///   <c>AI:Providers</c> 配置条目（只读，<c>Source=Configuration</c>）。配置条目的处理逻辑
///   见 partial 文件 ProviderService.ConfigurationSource.cs。
/// </remarks>
public partial class ProviderService : ApplicationService, IProviderService
{
    private const string ProtectorPurpose = Provider.ApiKeyProtectorPurpose;

    private readonly IRepository<Provider, Guid> _repository;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IOptionsMonitor<AIOptions>? _aiOptionsMonitor;

    public ProviderService(
        IRepository<Provider, Guid> repository,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider,
        ICurrentTenant? currentTenant = null,
        IOptionsMonitor<AIOptions>? aiOptionsMonitor = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _dataProtectionProvider = Check.NotNull(dataProtectionProvider);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _currentTenant = currentTenant;
        _aiOptionsMonitor = aiOptionsMonitor;
    }

    /// <summary>
    /// 对查询应用租户可见性过滤。
    /// 多租户模式下保留 System ∪ 当前租户行；单租户（currentTenant=null）下不添加过滤器。
    /// </summary>
    private IQueryable<Provider> ApplyVisibility(IQueryable<Provider> query)
    {
        var tenantId = _currentTenant?.Id;
        if (tenantId == null)
            return query; // 单租户：无需额外过滤

        return query.Where(p =>
            p.Scope == ResourceScope.System ||
            (p.Scope == ResourceScope.Tenant && p.TenantId == tenantId));
    }

    private IDataProtector GetProtector() => _dataProtectionProvider.CreateProtector(ProtectorPurpose);

    private static ProviderDto ToDto(Provider entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ProviderType = entity.ProviderType,
        Endpoint = entity.Endpoint,
        DefaultModel = entity.DefaultModel,
        Priority = entity.Priority,
        IsEnabled = entity.IsEnabled,
        Description = entity.Description,
        HasApiKey = !string.IsNullOrEmpty(entity.ApiKeyEncrypted),
        Scope = entity.Scope,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        LastModificationTime = entity.LastModificationTime
    };

    /// <inheritdoc />
    /// <remarks>
    /// 返回数据库实体与 appsettings <c>AI:Providers</c> 配置条目的合并列表：
    /// 配置条目（<c>Source=Configuration</c>，只读）置于列表头部，之后是数据库条目，
    /// 合并序列作为一个整体分页（TotalCount = 配置条目数 + 数据库条目数）。
    /// 当存在同名且已启用的数据库实体时，配置条目被隐藏（运行时该实体覆盖配置）。
    /// </remarks>
    public async Task<Result<IPagedList<ProviderDto>>> GetPagedListAsync(ProviderQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        try
        {
            var keyword = query.Keyword?.Trim().ToLower();
            var providerType = query.ProviderType?.Trim();

            var queryable = ApplyVisibility(_repository.AsQueryable())
                .WhereIf(p => p.ProviderType == providerType!, !string.IsNullOrEmpty(providerType))
                .WhereIf(p => p.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
                .WhereIf(
                    p => p.Name.ToLower().Contains(keyword!) || (p.Description != null && p.Description.ToLower().Contains(keyword!)),
                    !string.IsNullOrEmpty(keyword))
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.CreationTime);

            // Configuration-defined providers (AI:Providers) - hidden when an enabled DB
            // entity shares the name, because at runtime ChatClientFactory resolves the DB
            // row first and the config entry no longer takes effect.
            var enabledDbNames = await ApplyVisibility(_repository.AsQueryable())
                .Where(p => p.IsEnabled)
                .Select(p => p.Name)
                .ToListAsync(ct);
            var overriddenNames = new HashSet<string>(enabledDbNames, StringComparer.OrdinalIgnoreCase);
            var configDtos = FilterConfigurationDtos(
                BuildConfigurationProviderDtos().Where(p => !overriddenNames.Contains(p.Name)).ToList(),
                query);

            var dbTotal = await queryable.CountAsync(ct);
            var totalCount = dbTotal + configDtos.Count;

            // Merge-then-page: config entries occupy the head of the combined sequence,
            // DB rows follow. Only the DB remainder of the requested page hits the database.
            var skip = Math.Max(0, (query.PageIndex - 1) * query.PageSize);
            var pageItems = configDtos.Skip(skip).Take(query.PageSize).ToList();

            var remaining = query.PageSize - pageItems.Count;
            if (remaining > 0)
            {
                var dbSkip = Math.Max(0, skip - configDtos.Count);
                var dbItems = await queryable.Skip(dbSkip).Take(remaining).ToListAsync(ct);
                pageItems.AddRange(dbItems.Select(ToDto));
            }

            var paged = new PagedList<ProviderDto>(pageItems, query.PageIndex, query.PageSize, totalCount);
            return Ok<IPagedList<ProviderDto>>(paged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error querying providers");
            return Fail<IPagedList<ProviderDto>>("Failed to query providers", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProviderDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            if (TryGetConfigurationProvider(id, out var configDto))
            {
                return Ok(configDto!);
            }

            var entity = await ApplyVisibility(_repository.AsQueryable())
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<ProviderDto>("Provider not found", 404, ErrorCodes.ProviderNotFound);
            }
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting provider {Id}", id);
            return Fail<ProviderDto>("Failed to get provider", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProviderDto>> CreateAsync(CreateProviderDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);

        try
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return Fail<ProviderDto>("Provider name is required", 400, ErrorCodes.ProviderOperationFailed);
            }
            if (string.IsNullOrWhiteSpace(dto.ProviderType))
            {
                return Fail<ProviderDto>("Provider type is required", 400, ErrorCodes.ProviderOperationFailed);
            }

            // 计算作用域：优先使用请求显式值，其次按当前租户上下文推断
            var tenantId = _currentTenant?.Id;
            var scope = dto.Scope ?? (tenantId.HasValue ? ResourceScope.Tenant : ResourceScope.System);
            var effectiveTenantId = scope == ResourceScope.Tenant ? tenantId : null;

            var exists = await _repository.AsQueryable().AnyAsync(
                p => p.Name == name && p.Scope == scope && p.TenantId == effectiveTenantId, ct);
            if (exists)
            {
                return Fail<ProviderDto>($"Provider with name '{name}' already exists", 409, ErrorCodes.ProviderAlreadyExists);
            }

            var entity = new Provider
            {
                Name = name,
                ProviderType = dto.ProviderType.Trim(),
                Endpoint = dto.Endpoint,
                DefaultModel = dto.DefaultModel,
                Priority = dto.Priority,
                IsEnabled = dto.IsEnabled,
                Description = dto.Description,
                ApiKeyEncrypted = string.IsNullOrEmpty(dto.ApiKey) ? null : GetProtector().Protect(dto.ApiKey),
                Scope = scope,
                TenantId = effectiveTenantId
            };

            await _repository.InsertAsync(entity);
            LogInformation("Created provider {Name} (type {Type})", entity.Name, entity.ProviderType);
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating provider");
            return Fail<ProviderDto>("Failed to create provider", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProviderDto>> UpdateAsync(Guid id, UpdateProviderDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);

        try
        {
            if (IsConfigurationProviderId(id))
            {
                return Fail<ProviderDto>(ConfigurationProviderReadOnlyMessage, 400, ErrorCodes.ProviderOperationFailed);
            }

            var entity = await ApplyVisibility(_repository.AsQueryable(withTracking: true))
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<ProviderDto>("Provider not found", 404, ErrorCodes.ProviderNotFound);
            }

            if (_currentTenant?.Id != null && entity.Scope == ResourceScope.System)
            {
                return Fail<ProviderDto>("Access denied: system providers are managed by administrators.", 403, ErrorCodes.ProviderOperationFailed);
            }

            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name.Trim() != entity.Name)
            {
                var newName = dto.Name.Trim();
                var conflict = await _repository.AsQueryable().AnyAsync(
                    p => p.Id != id && p.Name == newName && p.Scope == entity.Scope && p.TenantId == entity.TenantId, ct);
                if (conflict)
                {
                    return Fail<ProviderDto>($"Provider with name '{newName}' already exists", 409, ErrorCodes.ProviderAlreadyExists);
                }
                entity.Name = newName;
            }

            if (!string.IsNullOrWhiteSpace(dto.ProviderType))
                entity.ProviderType = dto.ProviderType.Trim();
            if (dto.Endpoint != null)
                entity.Endpoint = dto.Endpoint;
            if (dto.DefaultModel != null)
                entity.DefaultModel = dto.DefaultModel;
            if (dto.Priority.HasValue)
                entity.Priority = dto.Priority.Value;
            if (dto.IsEnabled.HasValue)
                entity.IsEnabled = dto.IsEnabled.Value;
            if (dto.Description != null)
                entity.Description = dto.Description;

            // ApiKey 语义：null = 保留现有；空字符串 = 清除；非空 = 加密替换
            if (dto.ApiKey != null)
            {
                entity.ApiKeyEncrypted = string.IsNullOrEmpty(dto.ApiKey) ? null : GetProtector().Protect(dto.ApiKey);
            }

            await _repository.UpdateAsync(entity);
            LogInformation("Updated provider {Id} ({Name})", entity.Id, entity.Name);
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating provider {Id}", id);
            return Fail<ProviderDto>("Failed to update provider", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            if (IsConfigurationProviderId(id))
            {
                return Fail(ConfigurationProviderReadOnlyMessage, 400, ErrorCodes.ProviderOperationFailed);
            }

            var entity = await ApplyVisibility(_repository.AsQueryable(withTracking: true))
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail("Provider not found", 404, ErrorCodes.ProviderNotFound);
            }

            if (_currentTenant?.Id != null && entity.Scope == ResourceScope.System)
            {
                return Fail("Access denied: system providers are managed by administrators.", 403, ErrorCodes.ProviderOperationFailed);
            }

            await _repository.DeleteAsync(entity);
            LogInformation("Deleted provider {Id} ({Name})", id, entity.Name);
            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting provider {Id}", id);
            return Fail("Failed to delete provider", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProviderTestResultDto>> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (IsConfigurationProviderId(id))
            {
                return Fail<ProviderTestResultDto>(
                    "Connection test is not supported for configuration-defined providers (AI:Providers).",
                    400, ErrorCodes.ProviderOperationFailed);
            }

            var entity = await ApplyVisibility(_repository.AsQueryable())
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<ProviderTestResultDto>("Provider not found", 404, ErrorCodes.ProviderNotFound);
            }

            // Shallow probe - verify entity is enabled and credentials decrypt cleanly.
            // We do NOT issue a real LLM completion call (variable cost / latency / network).
            // Future enhancement: optionally invoke IChatClientFactory.GetChatClient and a
            // tiny ChatCompletionAsync ping when the factory is wired to the entity table.
            if (!entity.IsEnabled)
            {
                sw.Stop();
                return Ok(new ProviderTestResultDto
                {
                    Success = false,
                    Message = "Provider is disabled",
                    LatencyMs = sw.ElapsedMilliseconds
                });
            }

            if (!string.IsNullOrEmpty(entity.ApiKeyEncrypted))
            {
                try
                {
                    _ = GetProtector().Unprotect(entity.ApiKeyEncrypted);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Logger.LogWarning(ex, "Failed to decrypt API key for provider {Id}", id);
                    return Ok(new ProviderTestResultDto
                    {
                        Success = false,
                        Message = "Stored API key could not be decrypted (data protection key rotated?)",
                        LatencyMs = sw.ElapsedMilliseconds
                    });
                }
            }

            sw.Stop();
            return Ok(new ProviderTestResultDto
            {
                Success = true,
                Message = "Provider configuration is valid (shallow probe - no live LLM call)",
                LatencyMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Error testing provider {Id}", id);
            return Fail<ProviderTestResultDto>("Provider test failed", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 合并数据库实体（优先级排序在前）与 appsettings <c>AI:Providers</c> 中已启用的配置条目
    /// （追加在后，<c>Source=Configuration</c>）；同名且已启用的数据库实体会隐藏配置条目。
    /// </remarks>
    public async Task<Result<List<ProviderOptionDto>>> GetOptionsAsync(CancellationToken ct = default)
    {
        try
        {
            var items = await ApplyVisibility(_repository.AsQueryable())
                .Where(p => p.IsEnabled)
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Name)
                .Select(p => new ProviderOptionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    ProviderType = p.ProviderType,
                    DefaultModel = p.DefaultModel,
                    Source = ProviderSources.Database
                })
                .ToListAsync(ct);

            var dbNames = new HashSet<string>(items.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            items.AddRange(BuildConfigurationProviderDtos()
                .Where(p => p.IsEnabled && !dbNames.Contains(p.Name))
                .Select(p => new ProviderOptionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    ProviderType = p.ProviderType,
                    DefaultModel = p.DefaultModel,
                    Source = ProviderSources.Configuration
                }));

            return Ok(items);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing provider options");
            return Fail<List<ProviderOptionDto>>("Failed to list provider options", 500, ErrorCodes.ProviderOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProviderModelsDto>> ListModelsAsync(Guid id, CancellationToken ct = default)
    {
        // Configuration-defined providers support model listing too: the probe reads
        // BaseUrl/ApiKey straight from options (the key stays in memory - never serialized).
        if (TryGetConfigurationModelProbe(id, out var configProbe))
        {
            return Ok(await ListModelsCoreAsync(configProbe!, ct));
        }

        var entity = await ApplyVisibility(_repository.AsQueryable())
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity == null)
            return Fail<ProviderModelsDto>("Provider not found", 404, ErrorCodes.ProviderNotFound);

        string? apiKey = null;
        if (!string.IsNullOrEmpty(entity.ApiKeyEncrypted))
        {
            try { apiKey = GetProtector().Unprotect(entity.ApiKeyEncrypted); } catch { /* key rotated - proceed unauthenticated */ }
        }

        var probe = new ModelProbe(entity.Name, entity.ProviderType, entity.Endpoint, apiKey, entity.DefaultModel);
        return Ok(await ListModelsCoreAsync(probe, ct));
    }

    /// <summary>模型列表探针参数 - 数据库实体与配置条目共用（ApiKey 为已解密明文，仅驻留内存）。</summary>
    private sealed record ModelProbe(string Name, string ProviderType, string? Endpoint, string? ApiKey, string? DefaultModel);

    /// <summary>
    /// 列出模型的共享核心：优先实时 /v1/models，失败按 ProviderType 静态兜底；
    /// DefaultModel 始终合并进结果。
    /// </summary>
    private async Task<ProviderModelsDto> ListModelsCoreAsync(ModelProbe probe, CancellationToken ct)
    {
        var fallback = MergeDefault(StaticModelsFor(probe.ProviderType), probe.DefaultModel);

        var baseUrl = ResolveModelsBaseUrl(probe.Endpoint, probe.ProviderType);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new ProviderModelsDto { Models = fallback, Source = "fallback" };

        try
        {
            var live = await FetchLiveModelsAsync(probe, baseUrl!, ct);
            if (live.Count > 0)
                return new ProviderModelsDto { Models = MergeDefault(live, probe.DefaultModel), Source = "live" };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Live model listing failed for provider {Name}, using static fallback", probe.Name);
        }

        return new ProviderModelsDto { Models = fallback, Source = "fallback" };
    }

    private async Task<List<string>> FetchLiveModelsAsync(ModelProbe probe, string baseUrl, CancellationToken ct)
    {
        var isAnthropic = probe.ProviderType.Contains("anthropic", StringComparison.OrdinalIgnoreCase);
        var url = baseUrl.EndsWith("/v1", StringComparison.Ordinal) ? $"{baseUrl}/models" : $"{baseUrl}/v1/models";

        var apiKey = probe.ApiKey;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
        {
            if (isAnthropic)
            {
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            }
        }

        var client = _httpClientFactory.CreateClient(ResilientHttpClientNames.Fallback);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        // OpenAI + Anthropic both return { "data": [ { "id": "..." } ] }
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var modelId = idProp.GetString();
                    if (!string.IsNullOrWhiteSpace(modelId)) models.Add(modelId!);
                }
            }
        }
        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    /// <summary>Resolve the API base URL for a /v1/models call (explicit endpoint → type default → null).</summary>
    private static string? ResolveModelsBaseUrl(string? endpoint, string providerType)
    {
        if (!string.IsNullOrWhiteSpace(endpoint)) return endpoint!.TrimEnd('/');
        var type = providerType.ToLowerInvariant();
        return type switch
        {
            var t when t.Contains("anthropic") => "https://api.anthropic.com/v1",
            var t when t.Contains("deepseek") => "https://api.deepseek.com/v1",
            var t when t.Contains("openai") => "https://api.openai.com/v1",
            _ => null // unknown type without an endpoint → can't probe, use static fallback
        };
    }

    private static List<string> MergeDefault(List<string> models, string? defaultModel)
    {
        if (!string.IsNullOrWhiteSpace(defaultModel) && !models.Contains(defaultModel, StringComparer.OrdinalIgnoreCase))
            models.Insert(0, defaultModel!);
        return models;
    }

    /// <summary>Curated static fallback model lists by provider type (used when /v1/models is unavailable).</summary>
    private static List<string> StaticModelsFor(string providerType)
    {
        var type = (providerType ?? string.Empty).ToLowerInvariant();
        if (type.Contains("anthropic"))
            return ["claude-opus-4-1", "claude-sonnet-4-5", "claude-3-7-sonnet-latest", "claude-3-5-haiku-latest"];
        if (type.Contains("deepseek"))
            return ["deepseek-chat", "deepseek-reasoner"];
        if (type.Contains("gemini") || type.Contains("google"))
            return ["gemini-2.0-flash", "gemini-1.5-pro", "gemini-1.5-flash"];
        if (type.Contains("ollama"))
            return ["llama3.1", "qwen2.5", "mistral", "gemma2"];
        if (type.Contains("openai") || type.Contains("azure"))
            return ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini", "o3", "o4-mini"];
        return [];
    }
}
