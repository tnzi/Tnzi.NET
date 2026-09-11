namespace Tnzi.System.Services;

/// <summary>
/// 配置服务实现（作用域可见性在 <c>SettingService.Scope.cs</c>）
/// </summary>
public class SettingService : ApplicationService, ISettingService
{
    private static readonly DateTime _startTime = DateTime.UtcNow;

    private readonly IRepository<Setting, Guid> _settingRepository;
    private readonly IOptionsMonitor<ApplicationOptions> _applicationOptions;
    private readonly ICache _cache;
    private readonly IEnumerable<ISettingProvider> _settingProviders;
    private readonly IEnumerable<ISettingDefinitionProvider> _definitionProviders;
    private readonly ISettingEncryptor? _settingEncryptor;
    private readonly SettingEncryptionOptions _encryptionOptions;
    private readonly ITnziApplication? _tnziApplication;
    private readonly IHostEnvironment? _hostEnvironment;
    private readonly IDistributedEventBus? _distributedEventBus;
    private readonly ICurrentTenant? _currentTenant;

    /// <summary>
    /// 缓存条目，用于区分"不存在"和"值为null"
    /// </summary>
    private record SettingCacheEntry(string? Value, bool Exists, bool IsEncrypted);

    public SettingService(
        IServiceProvider serviceProvider,
        IRepository<Setting, Guid> settingRepository,
        IOptionsMonitor<ApplicationOptions> applicationOptions,
        IOptions<SettingEncryptionOptions> encryptionOptions,
        ICache cache,
        IEnumerable<ISettingProvider> settingProviders,
        IEnumerable<ISettingDefinitionProvider> definitionProviders,
        ISettingEncryptor? settingEncryptor = null,
        ITnziApplication? tnziApplication = null,
        IHostEnvironment? hostEnvironment = null,
        IDistributedEventBus? distributedEventBus = null,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _currentTenant = currentTenant;
        _settingRepository = Check.NotNull(settingRepository);
        _applicationOptions = Check.NotNull(applicationOptions);
        _encryptionOptions = Check.NotNull(encryptionOptions).Value;
        _cache = Check.NotNull(cache);
        _settingProviders = Check.NotNull(settingProviders);
        _definitionProviders = Check.NotNull(definitionProviders);
        _settingEncryptor = settingEncryptor;
        _tnziApplication = tnziApplication;
        _hostEnvironment = hostEnvironment;
        _distributedEventBus = distributedEventBus;
    }

    /// <inheritdoc />
    public ApplicationOptions GetApplicationOptions()
    {
        return _applicationOptions.CurrentValue;
    }

    /// <inheritdoc />
    public Task<Result<string>> GetAppNameAsync()
        => Task.FromResult(Ok<string>(_applicationOptions.CurrentValue.AppName));

    /// <inheritdoc />
    public Task<Result<string>> GetSiteNameAsync()
        => Task.FromResult(Ok<string>(_applicationOptions.CurrentValue.SiteName));

    /// <inheritdoc />
    public async Task<Result<string?>> GetSettingAsync(string key, string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Ok<string?>(defaultValue);

        var cacheKey = $"Setting:{key}";
        SettingCacheEntry entry;
        try
        {
            // 优先从缓存读取（缓存存密文，读取时解密）
            var cached = await _cache.GetAsync<SettingCacheEntry>(cacheKey);
            if (cached != null)
            {
                entry = cached;
            }
            else
            {
                var setting = await _settingRepository
                    .AsQueryable()
                    .AsNoTracking()
                    .Where(s => s.Scope == SettingScope.Global)
                    .FirstOrDefaultAsync(s => s.Key == key);

                entry = new SettingCacheEntry(setting?.Value, setting != null, setting?.IsEncrypted ?? false);

                // 写入缓存，有效期 1 小时
                await _cache.SetAsync(cacheKey, entry, TimeSpan.FromHours(1));
            }
        }
        catch (Exception)
        {
            // 「读不到」（库 / 缓存故障）沿用回默认值的既定取舍：设置读取不该让业务请求整体失败。
            LogWarning("Failed to get setting {Key} from database/cache, returning default value", key);
            return Ok<string?>(defaultValue);
        }

        if (!entry.Exists)
            return Ok<string?>(defaultValue);

        if (!entry.IsEncrypted || entry.Value == null)
            return Ok<string?>(entry.Value);

        // ★「读到了但解不开」不是「读不到」：密钥轮换、密文被明文覆盖、加密被关掉，这三种情况下
        // 回默认值会让调用方拿到与「这个键没配」一模一样的答案，只剩一条没人看的 Warning。
        return DecryptOrFail(key, entry.Value);
    }

    /// <summary>
    /// 解密一条加密设置；解不开时返回<b>失败</b>的 Result 而不是默认值。
    /// </summary>
    private Result<string?> DecryptOrFail(string key, string cipherText)
    {
        if (_settingEncryptor == null)
        {
            LogError(
                "Setting {Key} is stored encrypted but setting encryption is not configured (System:Encryption); the value cannot be read.",
                key);
            return Fail<string?>(
                $"Setting '{key}' is encrypted but setting encryption is not configured",
                500,
                ErrorCodes.CONFIGURATION_MISSING);
        }

        try
        {
            return Ok<string?>(_settingEncryptor.Decrypt(cipherText));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Setting {Key} could not be decrypted (wrong key or corrupted ciphertext).", key);
            return Fail<string?>(
                $"Setting '{key}' is encrypted but could not be decrypted (wrong key or corrupted ciphertext)",
                500,
                ErrorCodes.CONFIGURATION_INVALID);
        }
    }

    /// <inheritdoc />
    public async Task<Result<T?>> GetSettingAsync<T>(string key, T? defaultValue = default) where T : struct
    {
        var result = await GetSettingAsync(key);
        if (!result.Succeeded)
        {
            // 解密失败必须一路向上传，不能在这一层又被折叠成默认值。
            return Fail<T?>(result.Message ?? $"Failed to read setting '{key}'", result.Code ?? 500, result.ErrorCode);
        }

        if (string.IsNullOrWhiteSpace(result.Data))
            return Ok<T?>(defaultValue);

        try
        {
            var value = (T)Convert.ChangeType(result.Data, typeof(T));
            return Ok<T?>(value);
        }
        catch (Exception)
        {
            LogWarning("Failed to convert setting {Key} to type {Type}, returning default value", key, typeof(T).Name);
            return Ok<T?>(defaultValue);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SetSettingAsync(string key, string value, string? description = null, string? group = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Fail("Key cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var setting = await _settingRepository
                .AsQueryable()
                .Where(s => s.Scope == SettingScope.Global)
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

            if (setting != null)
            {
                // 加密设置必须通过 SetEncryptedAsync 更新，防止明文覆盖密文后 IsEncrypted 标记不一致
                if (setting.IsEncrypted)
                    throw new BusinessException("Cannot update encrypted setting via SetSettingAsync, use SetEncryptedAsync instead", ErrorCodes.VALIDATION_ERROR);

                // 更新现有配置
                setting.Value = value;
                if (!string.IsNullOrWhiteSpace(description))
                    setting.Description = description;
                if (!string.IsNullOrWhiteSpace(group))
                    setting.Group = group;
                await _settingRepository.UpdateAsync(setting, cancellationToken);
            }
            else
            {
                // 创建新配置
                setting = new Setting
                {
                    Key = key,
                    Value = value,
                    Description = description,
                    Group = group ?? "General",
                    IsSystem = key.StartsWith("App.", StringComparison.OrdinalIgnoreCase)
                };
                await _settingRepository.InsertAsync(setting, cancellationToken);
            }
        });

        // 缓存清理在事务提交后执行，避免事务回滚时缓存已被清理
        await _cache.RemoveAsync($"Setting:{key}");

        await PublishSettingChangedAsync(key, SettingScope.Global, null, value, isRemoval: false);

        LogInformation("Setting updated: {Key}", key);
        return Ok("Setting updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<SettingDto>>> GetSettingsAsync(string? group = null, SettingScope? scope = null, string? scopeId = null)
    {
        var scopeFilter = BuildScopeFilter(scope, scopeId);
        if (!scopeFilter.Succeeded)
            return Fail<IEnumerable<SettingDto>>(scopeFilter.Message ?? "Invalid scope", scopeFilter.Code ?? 400, scopeFilter.ErrorCode);

        var query = _settingRepository.AsQueryable().AsNoTracking().Where(scopeFilter.Data!);

        if (!string.IsNullOrWhiteSpace(group))
        {
            var groupLower = group.ToLower();
            query = query.Where(s => s.Group != null && s.Group.ToLower() == groupLower);
        }

        var settings = await query
            .OrderBy(s => s.Group)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Key)
            .ToListAsync();

        var settingDtos = settings.MapToList<SettingDto>();

        // Mask encrypted setting values to prevent ciphertext exposure
        foreach (var dto in settingDtos.Where(d => d.IsEncrypted))
        {
            dto.Value = "******";
        }

        return Ok((IEnumerable<SettingDto>)settingDtos);
    }

    /// <inheritdoc />
    public async Task<Result<SettingDto>> GetSettingByIdAsync(Guid id)
    {
        var setting = await _settingRepository.GetAsync(id);
        if (setting == null || !CanAccess(setting))
            return Fail<SettingDto>("Setting not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var dto = setting.MapTo<SettingDto>();

        // Mask encrypted setting value to prevent ciphertext exposure
        if (dto.IsEncrypted)
        {
            dto.Value = "******";
        }

        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<Result<SettingDto>> CreateSettingAsync(CreateSettingDto input)
    {
        Check.NotNull(input);

        // 后门收口：命中配置中心（RuntimeSetting）管理的 Global 键必须走 settings-center
        // 端点 - 原始 CRUD 无 schema 校验，Global 行会经 SettingConfigurationProvider 流入
        // IConfiguration，非法值将在下次重绑定强类型 Options 时抛异常。
        if (IsManagedBySettingsCenter(input.Key, input.Scope))
            return Fail<SettingDto>($"Setting key '{input.Key}' is managed by the settings center; use the settings center endpoints instead", 400, ErrorCodes.VALIDATION_ERROR);

        // 检查键是否已存在（按 Key + Scope + ScopeId 唯一约束）
        var exists = await _settingRepository
            .AsQueryable()
            .AnyAsync(s => s.Key == input.Key && s.Scope == input.Scope && s.ScopeId == input.ScopeId);

        if (exists)
            return Fail<SettingDto>($"Setting with key '{input.Key}' already exists", 409, ErrorCodes.VALIDATION_ERROR);

        var setting = input.MapTo<Setting>();
        setting.IsSystem = false;

        await _settingRepository.InsertAsync(setting);
        // 环境事务下仓储会推迟 SaveChanges，此时实体 Id 仍是默认值（框架在 SaveChanges
        // 里生成），返回的 DTO 会带 Guid.Empty，调用方无法据此再编辑/删除该行。
        await _settingRepository.SaveChangesAsync();

        // 读路径会缓存"键不存在"的结果（1 小时），新建后必须清掉该键的缓存，
        // 否则新值在缓存过期前一直读不到（其余写路径均已清理）。
        await _cache.RemoveAsync($"Setting:{setting.Key}");

        await PublishSettingChangedAsync(setting.Key, setting.Scope, setting.ScopeId, setting.Value, isRemoval: false);

        LogInformation("Setting created: {Key}", input.Key);
        return Ok(setting.MapTo<SettingDto>(), "Setting created successfully");
    }

    /// <inheritdoc />
    public async Task<Result<SettingDto>> UpdateSettingAsync(Guid id, UpdateSettingDto input)
    {
        Check.NotNull(input);

        var setting = await _settingRepository.GetAsync(id);
        if (setting == null || !CanAccess(setting))
            return Fail<SettingDto>("Setting not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (setting.IsSystem)
            return Fail<SettingDto>("Cannot update system setting", 403, ErrorCodes.SYSTEM_ERROR);

        // 加密行必须经 SetEncryptedAsync 更新：这里写入的是明文，而 IsEncrypted 仍为 true，
        // 之后每次读取都会拿明文去解密（GetSettingAsync 静默回退默认值、GetDecryptedAsync 400）。
        // 与 SetSettingAsync 的同名防护对齐。
        if (setting.IsEncrypted)
            return Fail<SettingDto>("Cannot update an encrypted setting here; use the encrypted setting endpoint instead", 400, ErrorCodes.VALIDATION_ERROR);

        // 后门收口：与 CreateSettingAsync 相同 - 受管键的值必须经配置中心的 schema 校验写入
        if (IsManagedBySettingsCenter(setting.Key, setting.Scope))
            return Fail<SettingDto>($"Setting key '{setting.Key}' is managed by the settings center; use the settings center endpoints instead", 400, ErrorCodes.VALIDATION_ERROR);

        input.MapTo(setting);
        await _settingRepository.UpdateAsync(setting);

        // 清理缓存
        await _cache.RemoveAsync($"Setting:{setting.Key}");

        await PublishSettingChangedAsync(setting.Key, setting.Scope, setting.ScopeId, setting.Value, isRemoval: false);

        LogInformation("Setting updated: {Key}", setting.Key);
        var dto = setting.MapTo<SettingDto>();
        if (dto.IsEncrypted)
            dto.Value = "******";
        return Ok(dto, "Setting updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteSettingAsync(Guid id)
    {
        var setting = await _settingRepository.GetAsync(id);
        if (setting == null || !CanAccess(setting))
            return Fail("Setting not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (setting.IsSystem)
            return Fail("Cannot delete system setting", 403, ErrorCodes.SYSTEM_ERROR);

        await _settingRepository.DeleteAsync(id);

        // 清理缓存
        await _cache.RemoveAsync($"Setting:{setting.Key}");

        await PublishSettingChangedAsync(setting.Key, setting.Scope, setting.ScopeId, null, isRemoval: true);

        LogInformation("Setting deleted: {Key}", setting.Key);
        return Ok("Setting deleted successfully");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteSettingsAsync(IEnumerable<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);

        var idList = ids.ToList();
        var settings = await _settingRepository
            .Where(s => idList.Contains(s.Id))
            .ToListAsync();

        // 别的租户的行对本调用者等同于不存在：与单条删除同一口径，绝不静默删一部分。
        if (settings.Any(s => !CanAccess(s)))
            return Fail("One or more settings were not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 在进入事务前校验：存在系统配置则返回 Fail
        var systemSettings = settings.Where(s => s.IsSystem).ToList();
        if (systemSettings.Count > 0)
            return Fail($"Cannot delete system settings: {string.Join(", ", systemSettings.Select(s => s.Key))}", 403, ErrorCodes.SYSTEM_ERROR);

        var count = settings.Count;
        var settingKeys = settings.Select(s => s.Key).ToList();

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            await _settingRepository.DeleteManyAsync(settings, cancellationToken);
        });

        // 缓存清理在事务提交后执行，避免事务回滚时缓存已被清理
        foreach (var key in settingKeys)
        {
            await _cache.RemoveAsync($"Setting:{key}");
        }

        foreach (var s in settings)
        {
            await PublishSettingChangedAsync(s.Key, s.Scope, s.ScopeId, null, isRemoval: true);
        }

        LogInformation("Batch deleted {Count} settings", count);
        return Ok($"Deleted {count} settings successfully");
    }

    /// <inheritdoc />
    public async Task<Result<string?>> GetSettingValueAsync(string key, string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Ok<string?>(defaultValue);

        try
        {
            // 按 Priority 降序遍历 provider 链，返回第一个非 null 值
            foreach (var provider in _settingProviders.OrderByDescending(p => p.Priority))
            {
                var value = await provider.GetOrNullAsync(key);
                if (value != null)
                    return Ok<string?>(value);
            }

            return Ok<string?>(defaultValue);
        }
        catch (Exception)
        {
            LogWarning("Failed to get setting {Key} from provider chain, returning default value", key);
            return Ok<string?>(defaultValue);
        }
    }

    /// <inheritdoc />
    public async Task<Result<string?>> GetSettingAsync(string key, SettingScope scope, string? scopeId = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Ok<string?>(null);

        var setting = await _settingRepository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == scope && s.ScopeId == scopeId)
            .FirstOrDefaultAsync();

        if (setting == null)
            return Ok<string?>(null);

        // 自动解密加密配置
        var value = setting.IsEncrypted ? DecryptValue(setting.Value) : setting.Value;
        return Ok<string?>(value);
    }

    /// <inheritdoc />
    public async Task<Result> SetSettingAsync(string key, string value, SettingScope scope, string? scopeId = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Fail("Key cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        // 与 Create / Update 同一道收口：受配置中心管理的 Global 键必须经 schema 校验写入。
        if (IsManagedBySettingsCenter(key, scope))
            return Fail($"Setting key '{key}' is managed by the settings center; use the settings center endpoints instead", 400, ErrorCodes.VALIDATION_ERROR);

        // 与另外两条写路径同一道防护：明文盖掉密文而 IsEncrypted 仍为 true，之后每次读取都拿明文去解密。
        // 在事务外先查一次并返回失败的 Result，而不是在事务里抛异常。
        var existing = await _settingRepository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == scope && s.ScopeId == scopeId)
            .Select(s => new { s.IsEncrypted })
            .FirstOrDefaultAsync();
        if (existing is { IsEncrypted: true })
            return Fail("Cannot update an encrypted setting here; use the encrypted setting endpoint instead", 400, ErrorCodes.VALIDATION_ERROR);

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var setting = await _settingRepository.AsQueryable()
                .Where(s => s.Key == key && s.Scope == scope && s.ScopeId == scopeId)
                .FirstOrDefaultAsync(cancellationToken);

            if (setting != null)
            {
                setting.Value = value;
                await _settingRepository.UpdateAsync(setting, cancellationToken);
            }
            else
            {
                setting = new Setting
                {
                    Key = key,
                    Value = value,
                    Scope = scope,
                    ScopeId = scopeId,
                    Group = "General"
                };
                await _settingRepository.InsertAsync(setting, cancellationToken);
            }
        });

        // 缓存清理在事务提交后执行
        await _cache.RemoveAsync($"Setting:{key}");

        await PublishSettingChangedAsync(key, scope, scopeId, value, isRemoval: false);

        return Ok("Setting updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result> SetEncryptedAsync(string group, string key, string value, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Fail("Key cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        Check.NotNullOrWhiteSpace(value);

        if (!_encryptionOptions.Enabled || _settingEncryptor == null)
            return Fail("Setting encryption is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);

        var encryptedValue = _settingEncryptor.Encrypt(value);

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var setting = await _settingRepository
                .AsQueryable()
                .Where(s => s.Scope == SettingScope.Global && s.Group == group)
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

            if (setting != null)
            {
                setting.Value = encryptedValue;
                setting.IsEncrypted = true;
                if (!string.IsNullOrWhiteSpace(description))
                    setting.Description = description;
                await _settingRepository.UpdateAsync(setting, cancellationToken);
            }
            else
            {
                setting = new Setting
                {
                    Key = key,
                    Value = encryptedValue,
                    Description = description,
                    Group = group,
                    IsEncrypted = true,
                    ValueType = SettingValueType.String
                };
                await _settingRepository.InsertAsync(setting, cancellationToken);
            }
        });

        // 缓存清理在事务提交后执行
        await _cache.RemoveAsync($"Setting:{key}");

        // 加密配置不进 IConfiguration dict（Provider 已过滤 IsEncrypted），事件仍发出供其他订阅者使用
        await PublishSettingChangedAsync(key, SettingScope.Global, null, encryptedValue, isRemoval: false);

        LogInformation("Encrypted setting updated: {Key}", key);
        return Ok("Encrypted setting saved successfully");
    }

    /// <inheritdoc />
    public async Task<Result<string?>> GetDecryptedAsync(string group, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Ok<string?>(null);

        var setting = await _settingRepository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == SettingScope.Global && s.Group == group)
            .FirstOrDefaultAsync();

        if (setting == null)
            return Ok<string?>(null);

        if (!setting.IsEncrypted)
            return Ok<string?>(setting.Value);

        var decrypted = DecryptValue(setting.Value);
        return Ok<string?>(decrypted);
    }

    /// <summary>
    /// Decrypt value using the configured encryptor.
    /// Returns null when encryptor is unavailable (fail-safe: never expose ciphertext).
    /// </summary>
    private string? DecryptValue(string encryptedValue)
    {
        if (_settingEncryptor == null)
        {
            LogWarning("Encrypted setting value found but encryption is not enabled, returning null for safety");
            return null;
        }

        return _settingEncryptor.Decrypt(encryptedValue);
    }

    /// <inheritdoc />
    public Task<Result<SystemInfoDto>> GetSystemInfoAsync()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var frameworkAssembly = typeof(ITnziApplication).Assembly;

        var info = new SystemInfoDto
        {
            AppName = _applicationOptions.CurrentValue.AppName,
            FrameworkVersion = frameworkAssembly.GetName().Version?.ToString() ?? "0.0.0",
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            StartTime = _startTime,
            Uptime = FormatUptime(uptime),
            Environment = _hostEnvironment?.EnvironmentName ?? "Unknown"
        };

        if (_tnziApplication != null)
        {
            info.LoadedModules = _tnziApplication.Modules.Select(m => new SystemModuleInfoDto
            {
                Name = m.Type.Name,
                Assembly = m.Assembly.GetName().Name ?? string.Empty,
                IsEnabled = m.IsEnabled,
                LoadOrder = m.Instance.LoadOrder
            }).ToList();
        }

        return Task.FromResult(Ok(info));
    }

    /// <inheritdoc />
    public async Task<Result<List<SettingGroupDto>>> GetSettingGroupsAsync(SettingScope? scope = null, string? scopeId = null, CancellationToken cancellationToken = default)
    {
        // 与 GetSettingsAsync 同一条作用域口径：计数必须与列表对得上。
        var scopeFilter = BuildScopeFilter(scope, scopeId);
        if (!scopeFilter.Succeeded)
            return Fail<List<SettingGroupDto>>(scopeFilter.Message ?? "Invalid scope", scopeFilter.Code ?? 400, scopeFilter.ErrorCode);

        var groups = await _settingRepository.AsQueryable()
            .AsNoTracking()
            .Where(scopeFilter.Data!)
            .GroupBy(s => s.Group ?? "General")
            .Select(g => new SettingGroupDto
            {
                GroupName = g.Key,
                SettingCount = g.Count()
            })
            .OrderBy(g => g.GroupName)
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    /// <summary>
    /// 判断键是否由配置中心（[RuntimeSetting] schema）管理。仅 Global 作用域受管 -
    /// 配置中心只写 Global，Tenant/User 行不进 IConfiguration，无污染风险。
    /// </summary>
    private bool IsManagedBySettingsCenter(string key, SettingScope scope)
    {
        if (scope != SettingScope.Global || string.IsNullOrWhiteSpace(key))
            return false;

        return _definitionProviders
            .SelectMany(p => p.GetGroups())
            .SelectMany(g => g.Fields)
            .Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Publish a SettingChangedEvent. EventBus is optional (apps without EventBusModule loaded),
    /// failures are swallowed so config writes never block on event delivery.
    /// </summary>
    private async Task PublishSettingChangedAsync(string key, SettingScope scope, string? scopeId, string? newValue, bool isRemoval)
    {
        if (EventBus != null)
        {
            try
            {
                await EventBus.PublishAsync(new SettingChangedEvent
                {
                    Key = key,
                    Scope = scope,
                    ScopeId = scopeId,
                    NewValue = newValue,
                    IsRemoval = isRemoval
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to publish SettingChangedEvent for key {Key}", key);
            }
        }

        // 多实例一致性：分布式总线可用时把 Global 变更广播给其他实例（仅 key，不带值 -
        // 收端直查数据库 reload，broker 上不落配置值/机密）。本地链已处理本实例，
        // 收端用 OriginInstanceId 跳过回环投递。
        if (_distributedEventBus != null && scope == SettingScope.Global)
        {
            try
            {
                await _distributedEventBus.PublishAsync(new SettingChangedIntegrationEvent
                {
                    Key = key,
                    Scope = scope,
                    ScopeId = scopeId,
                    IsRemoval = isRemoval,
                    OriginInstanceId = SettingChangedIntegrationEvent.LocalInstanceId
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to publish SettingChangedIntegrationEvent for key {Key}", key);
            }
        }
    }

    public async Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, string? group = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var result = await ExecuteInUnitOfWorkAsync(
            ct => _settingRepository.ReorderAsync(ids, s => s.Group == group, ct),
            cancellationToken);

        if (!result.Succeeded)
            return Result.Failure(result.Message ?? "Reorder failed.", result.Code ?? 400, result.ErrorCode);

        LogInformation("Reordered {Count} setting(s) in group {Group}", result.Data, group);
        return Ok();
    }

    // ------------------------------------------------------------------------
    // <see cref="SettingService"/> 的作用域可见性：管理端读路径按调用者的租户收口。
    // ------------------------------------------------------------------------

    /// <summary>
    /// 调用者所属租户（字符串形式，与 <see cref="Setting.ScopeId"/> 同口径）；宿主 / 单租户部署为 null。
    /// 与 <c>TenantSettingProvider</c> 同源：先看 <see cref="ICurrentTenant"/>，退回当前用户的租户。
    /// </summary>
    private string? CallerTenantId => (_currentTenant?.Id ?? CurrentUser?.TenantId)?.ToString();

    /// <summary>
    /// 管理端读路径的作用域谓词。★ 租户归属由<b>身份</b>决定，不由客户端参数决定：
    /// 租户内的调用者请求别的租户返回 403 而不是静默改写成本租户。
    /// </summary>
    /// <remarks>
    /// <c>Setting</c> 不实现 <c>IMultiTenant</c>（租户归属在 <c>ScopeId</c>，Global 行没有租户），
    /// 全局租户过滤器管不到它；此前列表只按分组过滤，多租户部署里租户 A 的管理员拿到了租户 B 的
    /// 全部 Tenant 行与所有用户的 User 行。User 行没有租户列，租户内的调用者列不出「全部用户」，
    /// 只能一次指定一个明确的人。
    /// </remarks>
    private Result<Expression<Func<Setting, bool>>> BuildScopeFilter(SettingScope? scope, string? scopeId)
    {
        var tenantId = CallerTenantId;

        switch (scope)
        {
            case null:
                return Ok<Expression<Func<Setting, bool>>>(tenantId == null
                    ? s => s.Scope == SettingScope.Global
                    : s => s.Scope == SettingScope.Global || (s.Scope == SettingScope.Tenant && s.ScopeId == tenantId));

            case SettingScope.Global:
                return Ok<Expression<Func<Setting, bool>>>(s => s.Scope == SettingScope.Global);

            case SettingScope.Tenant:
                if (tenantId != null)
                {
                    if (scopeId != null && !string.Equals(scopeId, tenantId, StringComparison.OrdinalIgnoreCase))
                        return Fail<Expression<Func<Setting, bool>>>("Settings of another tenant are not accessible", 403, ErrorCodes.FORBIDDEN);

                    return Ok<Expression<Func<Setting, bool>>>(s => s.Scope == SettingScope.Tenant && s.ScopeId == tenantId);
                }

                return Ok<Expression<Func<Setting, bool>>>(scopeId == null
                    ? s => s.Scope == SettingScope.Tenant
                    : s => s.Scope == SettingScope.Tenant && s.ScopeId == scopeId);

            case SettingScope.User:
                if (tenantId != null && scopeId == null)
                    return Fail<Expression<Func<Setting, bool>>>("scopeId (user id) is required to list user-scoped settings", 400, ErrorCodes.VALIDATION_ERROR);

                return Ok<Expression<Func<Setting, bool>>>(scopeId == null
                    ? s => s.Scope == SettingScope.User
                    : s => s.Scope == SettingScope.User && s.ScopeId == scopeId);

            default:
                return Fail<Expression<Func<Setting, bool>>>($"Unknown setting scope '{scope}'", 400, ErrorCodes.VALIDATION_ERROR);
        }
    }

    /// <summary>
    /// 按 id 的读 / 改 / 删是否对调用者可见：别的租户的 Tenant 行等同于不存在（404，不泄露存在性）。
    /// </summary>
    private bool CanAccess(Setting setting)
    {
        if (setting.Scope != SettingScope.Tenant)
            return true;

        var tenantId = CallerTenantId;
        return tenantId == null || string.Equals(setting.ScopeId, tenantId, StringComparison.OrdinalIgnoreCase);
    }

}
