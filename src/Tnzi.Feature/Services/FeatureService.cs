namespace Tnzi.Feature.Services;

/// <summary>
/// Feature management service implementation.
/// Provides CRUD operations for feature definitions and values.
/// </summary>
public class FeatureService : ApplicationService, IFeatureService
{
    private readonly IRepository<FeatureDefinition, Guid> _definitionRepository;
    private readonly IRepository<FeatureValue, Guid> _valueRepository;
    private readonly IFeatureManager _featureManager;

    /// <summary>
    /// Initialize FeatureService
    /// </summary>
    public FeatureService(
        IServiceProvider serviceProvider,
        IRepository<FeatureDefinition, Guid> definitionRepository,
        IRepository<FeatureValue, Guid> valueRepository,
        IFeatureManager featureManager)
        : base(serviceProvider)
    {
        _definitionRepository = Check.NotNull(definitionRepository);
        _valueRepository = Check.NotNull(valueRepository);
        _featureManager = Check.NotNull(featureManager);
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

    // ==================== Feature Values ====================

    /// <inheritdoc />
    public async Task<Result<IEnumerable<FeatureValueDto>>> GetValuesAsync(string providerName, string? providerKey)
    {
        Check.NotNullOrWhiteSpace(providerName);

        var query = _valueRepository
            .Where(v => v.ProviderName == providerName);

        if (providerKey != null)
        {
            query = query.Where(v => v.ProviderKey == providerKey);
        }
        else
        {
            query = query.Where(v => v.ProviderKey == null);
        }

        var values = await query
            .Include(v => v.FeatureDefinition)
            .AsNoTracking()
            .ToListAsync();

        return Ok(values.MapToList<FeatureValueDto>().AsEnumerable());
    }

    /// <inheritdoc />
    public async Task<Result<FeatureValueDto>> SetValueAsync(SetFeatureValueRequest input)
    {
        Check.NotNull(input);

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

        // Find existing value or create new one
        var existingValue = await _valueRepository
            .Where(v => v.FeatureDefinitionId == input.FeatureDefinitionId
                        && v.ProviderName == input.ProviderName
                        && v.ProviderKey == input.ProviderKey)
            .FirstOrDefaultAsync();

        if (existingValue != null)
        {
            var previousValue = existingValue.Value;
            existingValue.Value = input.Value;
            await _valueRepository.UpdateAsync(existingValue);

            Logger.LogInformation("Feature value updated: {FeatureName} = {Value} for {ProviderName}/{ProviderKey}",
                definition.Name, input.Value, input.ProviderName, input.ProviderKey);

            if (EventBus != null)
            {
                await EventBus.PublishAsync(new FeatureValueChangedEvent
                {
                    FeatureName = definition.Name,
                    ProviderName = input.ProviderName,
                    ProviderKey = input.ProviderKey,
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
            ProviderName = input.ProviderName,
            ProviderKey = input.ProviderKey,
            Value = input.Value
        };

        await _valueRepository.InsertAsync(entity);
        // 同 CreateDefinitionAsync：先 flush 让框架生成 Id，返回的 DTO 才带真实主键。
        await _valueRepository.SaveChangesAsync();

        Logger.LogInformation("Feature value created: {FeatureName} = {Value} for {ProviderName}/{ProviderKey}",
            definition.Name, input.Value, input.ProviderName, input.ProviderKey);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FeatureValueChangedEvent
            {
                FeatureName = definition.Name,
                ProviderName = input.ProviderName,
                ProviderKey = input.ProviderKey,
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

        if (entity == null)
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
        Check.NotNullOrWhiteSpace(input.ProviderName);
        Check.NotNullOrEmpty(input.Values);

        var result = new BatchSetFeatureValuesResultDto();

        // 批量加载所有涉及的定义
        var definitionIds = input.Values.Select(v => v.FeatureDefinitionId).Distinct().ToList();
        var definitions = await _definitionRepository
            .Where(d => definitionIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);

        // 批量加载该 provider 已有的值
        var existingValues = await _valueRepository
            .Where(v => v.ProviderName == input.ProviderName
                        && v.ProviderKey == input.ProviderKey
                        && definitionIds.Contains(v.FeatureDefinitionId))
            .ToListAsync();

        var existingLookup = existingValues.ToDictionary(v => v.FeatureDefinitionId);

        var toInsert = new List<FeatureValue>();
        var toUpdate = new List<FeatureValue>();

        foreach (var item in input.Values)
        {
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

                if (EventBus != null)
                {
                    await EventBus.PublishAsync(new FeatureValueChangedEvent
                    {
                        FeatureName = definition.Name,
                        ProviderName = input.ProviderName,
                        ProviderKey = input.ProviderKey,
                        Value = item.Value,
                        PreviousValue = previousValue
                    });
                }
            }
            else
            {
                var entity = new FeatureValue
                {
                    FeatureDefinitionId = item.FeatureDefinitionId,
                    ProviderName = input.ProviderName,
                    ProviderKey = input.ProviderKey,
                    Value = item.Value
                };
                toInsert.Add(entity);

                if (EventBus != null)
                {
                    await EventBus.PublishAsync(new FeatureValueChangedEvent
                    {
                        FeatureName = definition.Name,
                        ProviderName = input.ProviderName,
                        ProviderKey = input.ProviderKey,
                        Value = item.Value,
                        PreviousValue = null
                    });
                }
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

        Logger.LogInformation("Batch set feature values: {Succeeded} succeeded, {Failed} failed for {ProviderName}/{ProviderKey}",
            result.SucceededCount, result.FailedCount, input.ProviderName, input.ProviderKey);

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<FeatureValueWithDefinitionDto>>> GetAllValuesAsync(string providerName, string? providerKey)
    {
        Check.NotNullOrWhiteSpace(providerName);

        // 加载所有已启用的功能定义
        var definitions = await _definitionRepository
            .AsQueryable()
            .AsNoTracking()
            .Where(d => d.IsEnabled)
            .OrderBy(d => d.Group)
            .ThenBy(d => d.Name)
            .ToListAsync();

        // 加载该 provider 已设置的值
        var valueQuery = _valueRepository
            .Where(v => v.ProviderName == providerName);

        if (providerKey != null)
        {
            valueQuery = valueQuery.Where(v => v.ProviderKey == providerKey);
        }
        else
        {
            valueQuery = valueQuery.Where(v => v.ProviderKey == null);
        }

        var existingValues = await valueQuery
            .AsNoTracking()
            .ToListAsync();

        var valueLookup = existingValues.ToDictionary(v => v.FeatureDefinitionId);

        // 合并定义和值，构建完整视图
        var result = definitions.Select(d =>
        {
            var hasValue = valueLookup.TryGetValue(d.Id, out var featureValue);
            return new FeatureValueWithDefinitionDto
            {
                Id = hasValue ? featureValue!.Id : Guid.Empty,
                FeatureDefinitionId = d.Id,
                FeatureName = d.Name,
                DisplayName = d.DisplayName,
                Description = d.Description,
                Group = d.Group,
                ValueType = d.ValueType,
                DefaultValue = d.DefaultValue,
                EffectiveValue = hasValue ? featureValue!.Value : (d.DefaultValue ?? string.Empty),
                IsExplicitlySet = hasValue,
                IsEnabled = d.IsEnabled
            };
        }).ToList();

        return Ok(result.AsEnumerable());
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
