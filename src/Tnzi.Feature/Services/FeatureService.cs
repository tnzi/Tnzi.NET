namespace Tnzi.Feature.Services;

/// <summary>
/// Feature management service implementation.
/// Provides CRUD operations for feature definitions and values.
/// </summary>
/// <remarks>
/// Every value write is resolved against the registered <see cref="IFeatureValueProvider"/>s
/// first (<see cref="ResolveScope"/>). A <c>FeatureValue</c> row is only meaningful if some
/// provider reads it back at runtime; a row under an unregistered or inactive provider name,
/// or with a key shape the provider never matches, is stored successfully and then ignored
/// forever - the request answers 200 and the runtime keeps returning the definition default.
/// Refusing such writes up front is the only place that failure mode can be caught.
/// </remarks>
public class FeatureService : ApplicationService, IFeatureService
{
    private readonly IRepository<FeatureDefinition, Guid> _definitionRepository;
    private readonly IRepository<FeatureValue, Guid> _valueRepository;
    private readonly IFeatureManager _featureManager;
    private readonly IReadOnlyList<IFeatureValueProvider> _providers;
    private readonly ICurrentTenant? _currentTenant;

    /// <summary>
    /// Initialize FeatureService
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="definitionRepository">Feature definition repository.</param>
    /// <param name="valueRepository">Feature value repository.</param>
    /// <param name="featureManager">Feature manager.</param>
    /// <param name="providers">Registered feature value providers.</param>
    /// <param name="currentTenant">
    /// Current tenant accessor; the admin surface is scoped by the caller's tenant (see
    /// <see cref="CallerTenantId"/>). Absent (unit tests, hosts without it) the caller's tenant
    /// falls back to the current user's claim.
    /// </param>
    public FeatureService(
        IServiceProvider serviceProvider,
        IRepository<FeatureDefinition, Guid> definitionRepository,
        IRepository<FeatureValue, Guid> valueRepository,
        IFeatureManager featureManager,
        IEnumerable<IFeatureValueProvider> providers,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _definitionRepository = Check.NotNull(definitionRepository);
        _valueRepository = Check.NotNull(valueRepository);
        _featureManager = Check.NotNull(featureManager);
        _currentTenant = currentTenant;
        Check.NotNull(providers);
        // Same order the runtime evaluates them in (FeatureChecker), so inheritance shown to
        // the admin follows the same chain.
        _providers = providers.OrderByDescending(p => p.Priority).ToList().AsReadOnly();
    }

    // ==================== Feature Definitions ====================

    /// <inheritdoc />
    public async Task<Result<IEnumerable<FeatureDefinitionDto>>> GetDefinitionsAsync()
    {
        // Pull the full merged snapshot from IFeatureManager - this includes
        // both DB-persisted FeatureDefinition rows AND code-level definitions
        // registered via IFeatureDefinitionProvider implementations (e.g. the
        // built-in defaults shipped with the application binaries).
        var dbDefinitions = await _definitionRepository
            .AsQueryable()
            .AsNoTracking()
            .ToListAsync();
        var dbByName = dbDefinitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

        var snapshot = await _featureManager.GetAllAsync();

        var results = new List<FeatureDefinitionDto>();

        // First, project DB rows (carry their real Id + audit fields).
        foreach (var d in dbDefinitions)
        {
            var dto = d.MapTo<FeatureDefinitionDto>();
            dto.Source = "Database";
            dto.IsReadOnly = false;
            results.Add(dto);
        }

        // Then append any code-level definition that doesn't have a DB row.
        foreach (var record in snapshot)
        {
            if (dbByName.ContainsKey(record.Name)) continue; // DB wins
            results.Add(new FeatureDefinitionDto
            {
                Id = Guid.Empty,
                Name = record.Name,
                DisplayName = record.DisplayName,
                Description = record.Description,
                DefaultValue = record.DefaultValue,
                ValueType = record.ValueType,
                ParentName = record.ParentName,
                IsEnabled = record.IsEnabled,
                Group = record.Group,
                Source = "Code",
                IsReadOnly = true,
            });
        }

        var ordered = results.OrderBy(r => r.Group).ThenBy(r => r.Name).ToList();
        return Ok(ordered.AsEnumerable());
    }

    /// <inheritdoc />
    public async Task<Result<FeatureDefinitionDto>> GetDefinitionByIdAsync(Guid id)
    {
        var definition = await _definitionRepository.FindAsync(id);
        if (definition == null)
        {
            return Fail<FeatureDefinitionDto>("Feature definition not found", 404, ErrorCodes.FeatureDefinitionNotFound);
        }

        return Ok(definition.MapTo<FeatureDefinitionDto>());
    }

    /// <inheritdoc />
    public async Task<Result<FeatureDefinitionDto>> CreateDefinitionAsync(CreateFeatureDefinitionRequest input)
    {
        Check.NotNull(input);

        // Check if name already exists (case-insensitive)
        var exists = await _definitionRepository
            .Where(d => d.Name.ToLower() == input.Name.ToLower())
            .AnyAsync();

        if (exists)
        {
            return Fail<FeatureDefinitionDto>(
                $"Feature definition with name '{input.Name}' already exists",
                409,
                ErrorCodes.FeatureDefinitionAlreadyExists);
        }

        var entity = input.MapTo<FeatureDefinition>();
        entity.IsEnabled = true;

        await _definitionRepository.InsertAsync(entity);
        // 环境事务下仓储推迟 SaveChanges，而 Id 是框架在 SaveChanges 里生成的 ——
        // 不 flush 则下面的事件与返回 DTO 都带 Guid.Empty，调用方无法据此再编辑/删除。
        await _definitionRepository.SaveChangesAsync();
        await _featureManager.InvalidateCacheAsync();

        Logger.LogInformation("Feature definition created: {Name}", input.Name);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FeatureDefinitionCreatedEvent
            {
                DefinitionId = entity.Id,
                Name = entity.Name,
                ValueType = entity.ValueType,
                Group = entity.Group
            });
        }

        return Ok(entity.MapTo<FeatureDefinitionDto>(), "Feature definition created successfully");
    }

    /// <inheritdoc />
    public async Task<Result<FeatureDefinitionDto>> UpdateDefinitionAsync(Guid id, UpdateFeatureDefinitionRequest input)
    {
        Check.NotNull(input);

        var entity = await _definitionRepository.FindAsync(id);
        if (entity == null)
        {
            return Fail<FeatureDefinitionDto>("Feature definition not found", 404, ErrorCodes.FeatureDefinitionNotFound);
        }

        input.MapTo(entity);
        await _definitionRepository.UpdateAsync(entity);
        await _featureManager.InvalidateCacheAsync();

        Logger.LogInformation("Feature definition updated: {Name}", entity.Name);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FeatureDefinitionUpdatedEvent
            {
                DefinitionId = entity.Id,
                Name = entity.Name,
                IsEnabled = entity.IsEnabled
            });
        }

        return Ok(entity.MapTo<FeatureDefinitionDto>(), "Feature definition updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteDefinitionAsync(Guid id)
    {
        var entity = await _definitionRepository.FindAsync(id);
        if (entity == null)
        {
            return Fail("Feature definition not found", 404, ErrorCodes.FeatureDefinitionNotFound);
        }

        var name = entity.Name;
        // Cascade delete will automatically remove associated FeatureValue records
        await _definitionRepository.DeleteAsync(entity);
        await _featureManager.InvalidateCacheAsync();

        Logger.LogInformation("Feature definition deleted: {Name} (associated values cascade-deleted)", name);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FeatureDefinitionDeletedEvent
            {
                DefinitionId = id,
                Name = name
            });
        }

        return Ok("Feature definition deleted successfully");
    }

    // ==================== Feature Value Providers ====================

    /// <inheritdoc />
    public Task<Result<IEnumerable<FeatureValueProviderDto>>> GetValueProvidersAsync()
    {
        var dtos = _providers.Select(p => new FeatureValueProviderDto
        {
            Name = p.Name,
            Priority = p.Priority,
            RequiresKey = p.RequiresKey,
            IsActive = p.IsActive,
            InactiveReason = p.IsActive ? null : p.InactiveReason
        }).ToList();

        return Task.FromResult(Ok(dtos.AsEnumerable()));
    }

    /// <summary>
    /// A resolved value scope: the registered provider (canonical casing) and the
    /// normalized key (trimmed; null when blank).
    /// </summary>
    private sealed record ValueScope(IFeatureValueProvider Provider, string? Key);

    /// <summary>
    /// Resolve a (providerName, providerKey) pair against the registered providers.
    /// Writes additionally require the provider to be active; reads are allowed on an
    /// inactive provider so its leftover rows remain visible for cleanup.
    /// </summary>
    private Result<ValueScope> ResolveScope(string? providerName, string? providerKey, bool forWrite)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Fail<ValueScope>("Provider name is required", 400, ErrorCodes.FeatureValueProviderUnknown);
        }

        var name = providerName.Trim();
        var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
        {
            var registered = string.Join(", ", _providers.Select(p => p.Name));
            return Fail<ValueScope>(
                $"Unknown feature value provider '{name}'. Registered providers: {registered}",
                400,
                ErrorCodes.FeatureValueProviderUnknown);
        }

        if (forWrite && !provider.IsActive)
        {
            return Fail<ValueScope>(
                $"Feature value provider '{provider.Name}' is inactive: {provider.InactiveReason}. A value written to it would never be evaluated",
                400,
                ErrorCodes.FeatureValueProviderInactive);
        }

        var key = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim();
        if (provider.RequiresKey && key == null)
        {
            return Fail<ValueScope>(
                $"Feature value provider '{provider.Name}' is keyed and requires a provider key",
                400,
                ErrorCodes.FeatureValueProviderKeyRequired);
        }

        if (!provider.RequiresKey && key != null)
        {
            return Fail<ValueScope>(
                $"Feature value provider '{provider.Name}' is keyless and does not accept a provider key",
                400,
                ErrorCodes.FeatureValueProviderKeyNotAllowed);
        }

        // ★ 租户归属由身份决定，不由客户端参数决定：租户内的调用者点名别的键（通常是别的租户 id）
        // 返回 403 而不是静默改写成本租户。宿主（无租户）可点名任意键。
        var tenantId = CallerTenantId;
        if (tenantId != null && !provider.IsKeyAccessibleTo(tenantId, key))
        {
            return Fail<ValueScope>(
                "Feature values of another tenant are not accessible",
                403,
                ErrorCodes.FeatureValueScopeForbidden);
        }

        return Ok(new ValueScope(provider, key));
    }

    /// <summary>
    /// 调用者所属租户（字符串形式，与 <see cref="FeatureValue.ProviderKey"/> 同口径）；宿主 / 单租户部署为 null。
    /// 与 <see cref="TenantFeatureValueProvider"/> 同源：先看 <see cref="ICurrentTenant"/>，退回当前用户的租户。
    /// </summary>
    private string? CallerTenantId => (_currentTenant?.Id ?? CurrentUser?.TenantId)?.ToString();

    /// <summary>
    /// 按 id 的删除是否对调用者可见：别的租户的行等同于不存在（404，不泄露存在性）。
    /// </summary>
    /// <remarks>
    /// 行的 provider 仍注册时由它裁决（<see cref="IFeatureValueProvider.IsKeyAccessibleTo"/>）；
    /// 已注销 provider 的遗留行没人替它的键作证，失败关闭：有键的行只有键等于本租户才可见。
    /// </remarks>
    private bool CanAccess(FeatureValue value)
    {
        var tenantId = CallerTenantId;
        if (tenantId == null)
        {
            return true;
        }

        var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, value.ProviderName, StringComparison.OrdinalIgnoreCase));
        return provider != null
            ? provider.IsKeyAccessibleTo(tenantId, value.ProviderKey)
            : value.ProviderKey == null || string.Equals(value.ProviderKey, tenantId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Re-type a failed result without losing message, status or error code.</summary>
    private static Result<T> Forward<T>(Result failure)
    {
        return Result.Failure<T>(failure.Message ?? "Invalid request", failure.Code ?? 400, failure.ErrorCode);
    }

    // ==================== Feature Values ====================

    /// <inheritdoc />
    public async Task<Result<IEnumerable<FeatureValueDto>>> GetValuesAsync(string providerName, string? providerKey)
    {
        var scopeResult = ResolveScope(providerName, providerKey, forWrite: false);
        if (scopeResult.Failed)
        {
            return Forward<IEnumerable<FeatureValueDto>>(scopeResult);
        }

        var scope = scopeResult.Data!;
        var values = await _valueRepository
            .Where(v => v.ProviderName == scope.Provider.Name && v.ProviderKey == scope.Key)
            .Include(v => v.FeatureDefinition)
            .AsNoTracking()
            .ToListAsync();

        return Ok(values.MapToList<FeatureValueDto>().AsEnumerable());
    }

    /// <inheritdoc />
    public async Task<Result<FeatureValueDto>> SetValueAsync(SetFeatureValueRequest input)
    {
        Check.NotNull(input);

        if (input.FeatureDefinitionId == Guid.Empty)
        {
            return Fail<FeatureValueDto>(
                "Code-defined feature definitions have no database id and cannot carry values; create a database definition with the same name to override it",
                400,
                ErrorCodes.FeatureDefinitionNotOverridable);
        }

        var scopeResult = ResolveScope(input.ProviderName, input.ProviderKey, forWrite: true);
        if (scopeResult.Failed)
        {
            return Forward<FeatureValueDto>(scopeResult);
        }

        var scope = scopeResult.Data!;

        // Validate feature definition exists
        var definition = await _definitionRepository.FindAsync(input.FeatureDefinitionId);
        if (definition == null)
        {
            return Fail<FeatureValueDto>("Feature definition not found", 404, ErrorCodes.FeatureDefinitionNotFound);
        }

        // Validate value against definition's ValueType
        if (!ValidateFeatureValue(definition.ValueType, input.Value))
        {
            return Fail<FeatureValueDto>(
                $"Invalid value '{input.Value}' for feature type {definition.ValueType}",
                400,
                ErrorCodes.InvalidFeatureValueType);
        }

        var providerName = scope.Provider.Name;
        var providerKey = scope.Key;

        // Find existing value or create new one
        var existingValue = await _valueRepository
            .Where(v => v.FeatureDefinitionId == input.FeatureDefinitionId
                        && v.ProviderName == providerName
                        && v.ProviderKey == providerKey)
            .FirstOrDefaultAsync();

        if (existingValue != null)
        {
            var previousValue = existingValue.Value;
            existingValue.Value = input.Value;
            await _valueRepository.UpdateAsync(existingValue);

            Logger.LogInformation("Feature value updated: {FeatureName} = {Value} for {ProviderName}/{ProviderKey}",
                definition.Name, input.Value, providerName, providerKey);

            if (EventBus != null)
            {
                await EventBus.PublishAsync(new FeatureValueChangedEvent
                {
                    FeatureName = definition.Name,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    Value = input.Value,
                    PreviousValue = previousValue
                });
            }

            var dto = existingValue.MapTo<FeatureValueDto>();
            dto.FeatureName = definition.Name;
            return Ok(dto, "Feature value updated successfully");
        }

        var entity = new FeatureValue
        {
            FeatureDefinitionId = input.FeatureDefinitionId,
            ProviderName = providerName,
            ProviderKey = providerKey,
            Value = input.Value
        };

        await _valueRepository.InsertAsync(entity);
        // 同 CreateDefinitionAsync：先 flush 让框架生成 Id，返回的 DTO 才带真实主键。
        await _valueRepository.SaveChangesAsync();

        Logger.LogInformation("Feature value created: {FeatureName} = {Value} for {ProviderName}/{ProviderKey}",
            definition.Name, input.Value, providerName, providerKey);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FeatureValueChangedEvent
            {
                FeatureName = definition.Name,
                ProviderName = providerName,
                ProviderKey = providerKey,
                Value = input.Value,
                PreviousValue = null
            });
        }

        var newDto = entity.MapTo<FeatureValueDto>();
        newDto.FeatureName = definition.Name;
        return Ok(newDto, "Feature value created successfully");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteValueAsync(Guid id)
    {
        var entity = await _valueRepository
            .Where(v => v.Id == id)
            .Include(v => v.FeatureDefinition)
            .FirstOrDefaultAsync();

        if (entity == null || !CanAccess(entity))
        {
            return Fail("Feature value not found", 404, ErrorCodes.FeatureValueNotFound);
        }

        var featureName = entity.FeatureDefinition?.Name ?? "Unknown";
        var providerName = entity.ProviderName;
        var providerKey = entity.ProviderKey;

        await _valueRepository.DeleteAsync(entity);

        Logger.LogInformation("Feature value deleted: {ProviderName}/{ProviderKey}", providerName, providerKey);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FeatureValueDeletedEvent
            {
                FeatureName = featureName,
                ProviderName = providerName,
                ProviderKey = providerKey
            });
        }

        return Ok("Feature value deleted successfully");
    }

    /// <inheritdoc />
    public async Task<Result<BatchSetFeatureValuesResultDto>> BatchSetValuesAsync(BatchSetFeatureValuesRequest input)
    {
        Check.NotNull(input);
        Check.NotNullOrEmpty(input.Values);

        var scopeResult = ResolveScope(input.ProviderName, input.ProviderKey, forWrite: true);
        if (scopeResult.Failed)
        {
            return Forward<BatchSetFeatureValuesResultDto>(scopeResult);
        }

        var scope = scopeResult.Data!;
        var providerName = scope.Provider.Name;
        var providerKey = scope.Key;

        var result = new BatchSetFeatureValuesResultDto();

        // 批量加载所有涉及的定义
        var definitionIds = input.Values.Select(v => v.FeatureDefinitionId).Distinct().ToList();
        var definitions = await _definitionRepository
            .Where(d => definitionIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);

        // 批量加载该 provider 已有的值
        var existingValues = await _valueRepository
            .Where(v => v.ProviderName == providerName
                        && v.ProviderKey == providerKey
                        && definitionIds.Contains(v.FeatureDefinitionId))
            .ToListAsync();

        var existingLookup = existingValues.ToDictionary(v => v.FeatureDefinitionId);

        var toInsert = new List<FeatureValue>();
        var toUpdate = new List<FeatureValue>();
        // 事件先攒着，持久化成功之后再发。默认 AspNetCore:EnableGlobalUnitOfWork=false 下没有环境事务，
        // TransactionAwarePublish 的延迟发布不生效 —— 循环里边发边写，写库一失败订阅者已经收到「值变了」。
        // SetValueAsync 一直是先写后发，两条路径必须同一顺序。
        var pendingEvents = new List<FeatureValueChangedEvent>();

        foreach (var item in input.Values)
        {
            if (item.FeatureDefinitionId == Guid.Empty)
            {
                result.Errors.Add("Code-defined feature definitions cannot carry values (no database id)");
                result.FailedCount++;
                continue;
            }

            if (!definitions.TryGetValue(item.FeatureDefinitionId, out var definition))
            {
                result.Errors.Add($"Feature definition '{item.FeatureDefinitionId}' not found");
                result.FailedCount++;
                continue;
            }

            if (!ValidateFeatureValue(definition.ValueType, item.Value))
            {
                result.Errors.Add($"Invalid value '{item.Value}' for feature '{definition.Name}' (type: {definition.ValueType})");
                result.FailedCount++;
                continue;
            }

            if (existingLookup.TryGetValue(item.FeatureDefinitionId, out var existing))
            {
                var previousValue = existing.Value;
                existing.Value = item.Value;
                toUpdate.Add(existing);

                pendingEvents.Add(new FeatureValueChangedEvent
                {
                    FeatureName = definition.Name,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    Value = item.Value,
                    PreviousValue = previousValue
                });
            }
            else
            {
                var entity = new FeatureValue
                {
                    FeatureDefinitionId = item.FeatureDefinitionId,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    Value = item.Value
                };
                toInsert.Add(entity);

                pendingEvents.Add(new FeatureValueChangedEvent
                {
                    FeatureName = definition.Name,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    Value = item.Value,
                    PreviousValue = null
                });
            }

            result.SucceededCount++;
        }

        // 批量持久化
        if (toUpdate.Count > 0)
        {
            await _valueRepository.UpdateManyAsync(toUpdate);
        }

        if (toInsert.Count > 0)
        {
            await _valueRepository.InsertManyAsync(toInsert);
        }

        // 写库成功之后才通知订阅者
        if (EventBus != null)
        {
            foreach (var changed in pendingEvents)
            {
                await EventBus.PublishAsync(changed);
            }
        }

        Logger.LogInformation("Batch set feature values: {Succeeded} succeeded, {Failed} failed for {ProviderName}/{ProviderKey}",
            result.SucceededCount, result.FailedCount, providerName, providerKey);

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<FeatureValueWithDefinitionDto>>> GetAllValuesAsync(string providerName, string? providerKey)
    {
        var scopeResult = ResolveScope(providerName, providerKey, forWrite: false);
        if (scopeResult.Failed)
        {
            return Forward<IEnumerable<FeatureValueWithDefinitionDto>>(scopeResult);
        }

        var scope = scopeResult.Data!;

        // 加载所有已启用的功能定义（数据库行）
        var definitions = await _definitionRepository
            .AsQueryable()
            .AsNoTracking()
            .Where(d => d.IsEnabled)
            .ToListAsync();
        var dbByName = definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

        // 代码声明的定义也要出现在值视图里 —— 否则管理员看到的清单与运行时评估的清单不是同一份。
        // 它们没有数据库 id，故不可覆盖（CanOverride=false）。
        var snapshot = await _featureManager.GetAllAsync();

        // 该作用域自己显式设置的值
        var scopeValues = await _valueRepository
            .Where(v => v.ProviderName == scope.Provider.Name && v.ProviderKey == scope.Key)
            .AsNoTracking()
            .ToListAsync();
        var explicitLookup = scopeValues.ToDictionary(v => v.FeatureDefinitionId);

        // 运行时链上排在该作用域之后的无键 provider（Global）：作用域没设值时由它们兜底。
        // 与 FeatureChecker 的评估顺序一致，管理员看到的「生效值」才等于运行时答案。
        var fallbackProviders = _providers
            .Where(p => p.Priority < scope.Provider.Priority && !p.RequiresKey)
            .ToList();
        var fallbackLookup = await LoadFallbackValuesAsync(fallbackProviders);

        var result = new List<FeatureValueWithDefinitionDto>();

        foreach (var d in definitions)
        {
            var hasValue = explicitLookup.TryGetValue(d.Id, out var featureValue);
            var dto = new FeatureValueWithDefinitionDto
            {
                Id = hasValue ? featureValue!.Id : Guid.Empty,
                FeatureDefinitionId = d.Id,
                FeatureName = d.Name,
                DisplayName = d.DisplayName,
                Description = d.Description,
                Group = d.Group,
                ValueType = d.ValueType,
                DefaultValue = d.DefaultValue,
                IsExplicitlySet = hasValue,
                IsEnabled = d.IsEnabled,
                Source = "Database",
                CanOverride = true
            };

            if (hasValue)
            {
                dto.EffectiveValue = featureValue!.Value;
                dto.EffectiveSource = FeatureValueSource.Explicit;
                dto.EffectiveProvider = scope.Provider.Name;
            }
            else if (TryResolveFallback(fallbackProviders, fallbackLookup, d.Id, out var fallbackProvider, out var fallbackValue))
            {
                dto.EffectiveValue = fallbackValue;
                dto.EffectiveSource = FeatureValueSource.Inherited;
                dto.EffectiveProvider = fallbackProvider;
            }
            else
            {
                dto.EffectiveValue = d.DefaultValue ?? string.Empty;
                dto.EffectiveSource = FeatureValueSource.Default;
                dto.EffectiveProvider = null;
            }

            result.Add(dto);
        }

        foreach (var record in snapshot)
        {
            if (!record.IsEnabled || dbByName.ContainsKey(record.Name)) continue; // DB wins
            result.Add(new FeatureValueWithDefinitionDto
            {
                Id = Guid.Empty,
                FeatureDefinitionId = Guid.Empty,
                FeatureName = record.Name,
                DisplayName = record.DisplayName,
                Description = record.Description,
                Group = record.Group,
                ValueType = record.ValueType,
                DefaultValue = record.DefaultValue,
                EffectiveValue = record.DefaultValue ?? string.Empty,
                IsExplicitlySet = false,
                EffectiveSource = FeatureValueSource.Default,
                EffectiveProvider = null,
                IsEnabled = record.IsEnabled,
                Source = "Code",
                CanOverride = false
            });
        }

        var ordered = result.OrderBy(r => r.Group).ThenBy(r => r.FeatureName).ToList();
        return Ok(ordered.AsEnumerable());
    }

    /// <summary>
    /// Load the keyless rows of every fallback provider in one query, keyed by
    /// (provider name, definition id).
    /// </summary>
    private async Task<Dictionary<(string Provider, Guid DefinitionId), string>> LoadFallbackValuesAsync(
        IReadOnlyList<IFeatureValueProvider> fallbackProviders)
    {
        var lookup = new Dictionary<(string, Guid), string>();
        if (fallbackProviders.Count == 0)
        {
            return lookup;
        }

        var names = fallbackProviders.Select(p => p.Name).ToList();
        var rows = await _valueRepository
            .Where(v => names.Contains(v.ProviderName) && v.ProviderKey == null)
            .AsNoTracking()
            .ToListAsync();

        foreach (var row in rows)
        {
            lookup[(row.ProviderName, row.FeatureDefinitionId)] = row.Value;
        }

        return lookup;
    }

    /// <summary>
    /// Walk the fallback providers in evaluation order and return the first one holding a value.
    /// </summary>
    private static bool TryResolveFallback(
        IReadOnlyList<IFeatureValueProvider> fallbackProviders,
        Dictionary<(string Provider, Guid DefinitionId), string> fallbackLookup,
        Guid definitionId,
        out string? providerName,
        out string value)
    {
        foreach (var provider in fallbackProviders)
        {
            if (fallbackLookup.TryGetValue((provider.Name, definitionId), out var found))
            {
                providerName = provider.Name;
                value = found;
                return true;
            }
        }

        providerName = null;
        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Validate a feature value against its definition's value type
    /// </summary>
    private static bool ValidateFeatureValue(FeatureValueType valueType, string value)
    {
        return valueType switch
        {
            FeatureValueType.Boolean => bool.TryParse(value, out _),
            FeatureValueType.Integer => int.TryParse(value, out _),
            FeatureValueType.String => true,
            _ => true
        };
    }
}
