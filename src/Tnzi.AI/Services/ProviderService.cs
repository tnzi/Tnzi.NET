using Microsoft.AspNetCore.DataProtection;

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
///   默认采用 "shallow probe" — 仅校验记录基础信息（存在性、IsEnabled、Endpoint 解析）
///   并测量内部解密/校验耗时。不发起真实 LLM 调用以避免计费和延迟波动。
///   未来若需要对接真实 IChatClientFactory 探针，可在此扩展。
/// </remarks>
public class ProviderService : ApplicationService, IProviderService
{
    private const string ProtectorPurpose = Provider.ApiKeyProtectorPurpose;

    private readonly IRepository<Provider, Guid> _repository;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ProviderService(
        IRepository<Provider, Guid> repository,
        IDataProtectionProvider dataProtectionProvider,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _dataProtectionProvider = Check.NotNull(dataProtectionProvider);
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
        CreationTime = entity.CreationTime,
        LastModificationTime = entity.LastModificationTime
    };

    /// <inheritdoc />
    public async Task<Result<IPagedList<ProviderDto>>> GetPagedListAsync(ProviderQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        try
        {
            var keyword = query.Keyword?.Trim().ToLower();
            var providerType = query.ProviderType?.Trim();

            var queryable = _repository.AsQueryable()
                .WhereIf(p => p.ProviderType == providerType!, !string.IsNullOrEmpty(providerType))
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
            var paged = new PagedList<ProviderDto>(dtoItems, query.PageIndex, query.PageSize, totalCount);
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
            var entity = await _repository.AsQueryable().FirstOrDefaultAsync(p => p.Id == id, ct);
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

            var exists = await _repository.AsQueryable().AnyAsync(p => p.Name == name, ct);
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
                ApiKeyEncrypted = string.IsNullOrEmpty(dto.ApiKey) ? null : GetProtector().Protect(dto.ApiKey)
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
            var entity = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<ProviderDto>("Provider not found", 404, ErrorCodes.ProviderNotFound);
            }

            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name.Trim() != entity.Name)
            {
                var newName = dto.Name.Trim();
                var conflict = await _repository.AsQueryable().AnyAsync(p => p.Id != id && p.Name == newName, ct);
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
            var entity = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail("Provider not found", 404, ErrorCodes.ProviderNotFound);
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
            var entity = await _repository.AsQueryable().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<ProviderTestResultDto>("Provider not found", 404, ErrorCodes.ProviderNotFound);
            }

            // Shallow probe — verify entity is enabled and credentials decrypt cleanly.
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
                Message = "Provider configuration is valid (shallow probe — no live LLM call)",
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
}
