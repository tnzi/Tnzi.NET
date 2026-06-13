namespace Tnzi.System.Services;

/// <summary>
/// 配置服务实现
/// </summary>
public class SettingService : ApplicationService, ISettingService
{
    private static readonly DateTime _startTime = DateTime.UtcNow;

    private readonly IRepository<Setting, Guid> _settingRepository;
    private readonly IOptionsMonitor<ApplicationOptions> _applicationOptions;
    private readonly ICache _cache;
    private readonly IEnumerable<ISettingProvider> _settingProviders;
    private readonly ISettingEncryptor? _settingEncryptor;
    private readonly SettingEncryptionOptions _encryptionOptions;
    private readonly ITnziApplication? _tnziApplication;
    private readonly IHostEnvironment? _hostEnvironment;

    // 标准配置键常量
    private const string KeyAppName = "App.AppName";
    private const string KeySiteName = "App.SiteName";

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
        ISettingEncryptor? settingEncryptor = null,
        ITnziApplication? tnziApplication = null,
        IHostEnvironment? hostEnvironment = null)
        : base(serviceProvider)
    {
        _settingRepository = Check.NotNull(settingRepository);
        _applicationOptions = Check.NotNull(applicationOptions);
        _encryptionOptions = Check.NotNull(encryptionOptions).Value;
        _cache = Check.NotNull(cache);
        _settingProviders = Check.NotNull(settingProviders);
        _settingEncryptor = settingEncryptor;
        _tnziApplication = tnziApplication;
        _hostEnvironment = hostEnvironment;
    }

    /// <inheritdoc />
    public ApplicationOptions GetApplicationOptions()
    {
        return _applicationOptions.CurrentValue;
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetAppNameAsync()
    {
        var result = await GetSettingAsync(KeyAppName);
        var value = result.Data ?? _applicationOptions.CurrentValue.AppName;
        return Ok<string>(value);
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetSiteNameAsync()
    {
        var result = await GetSettingAsync(KeySiteName);
        var value = result.Data ?? _applicationOptions.CurrentValue.SiteName;
        return Ok<string>(value);
    }

    /// <inheritdoc />
    public async Task<Result<string?>> GetSettingAsync(string key, string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Ok<string?>(defaultValue);

        var cacheKey = $"Setting:{key}";
        try
        {
            // 优先从缓存读取
            var cached = await _cache.GetAsync<SettingCacheEntry>(cacheKey);
            if (cached != null)
            {
                if (!cached.Exists)
                    return Ok<string?>(defaultValue);

                // Decrypt after reading from cache (cache stores ciphertext)
                var cachedValue = cached.IsEncrypted && cached.Value != null
                    ? DecryptValue(cached.Value)
                    : cached.Value;
                return Ok<string?>(cachedValue);
            }

            var setting = await _settingRepository
                .AsQueryable()
                .AsNoTracking()
                .Where(s => s.Scope == SettingScope.Global)
                .FirstOrDefaultAsync(s => s.Key == key);

            var exists = setting != null;
            var isEncrypted = exists && setting!.IsEncrypted;
            var rawValue = setting?.Value;

            // 写入缓存，有效期 1 小时（缓存密文，读取时解密）
            await _cache.SetAsync(cacheKey, new SettingCacheEntry(rawValue, exists, isEncrypted), TimeSpan.FromHours(1));

            // 解密后返回
            var value = isEncrypted && rawValue != null ? DecryptValue(rawValue) : rawValue;
            return Ok<string?>(exists ? value : defaultValue);
        }
        catch (Exception)
        {
            LogWarning("Failed to get setting {Key} from database/cache, returning default value", key);
            return Ok<string?>(defaultValue);
        }
    }

    /// <inheritdoc />
    public async Task<Result<T?>> GetSettingAsync<T>(string key, T? defaultValue = default) where T : struct
    {
        var result = await GetSettingAsync(key);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Data))
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
    public async Task<Result<IEnumerable<SettingDto>>> GetSettingsAsync(string? group = null)
    {
        var query = _settingRepository.AsQueryable().AsNoTracking();

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
        if (setting == null)
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

        // 检查键是否已存在（按 Key + Scope + ScopeId 唯一约束）
        var exists = await _settingRepository
            .AsQueryable()
            .AnyAsync(s => s.Key == input.Key && s.Scope == input.Scope && s.ScopeId == input.ScopeId);

        if (exists)
            return Fail<SettingDto>($"Setting with key '{input.Key}' already exists", 409, ErrorCodes.VALIDATION_ERROR);

        var setting = input.MapTo<Setting>();
        setting.IsSystem = false;

        await _settingRepository.InsertAsync(setting);

        await PublishSettingChangedAsync(setting.Key, setting.Scope, setting.ScopeId, setting.Value, isRemoval: false);

        LogInformation("Setting created: {Key}", input.Key);
        return Ok(setting.MapTo<SettingDto>(), "Setting created successfully");
    }

    /// <inheritdoc />
    public async Task<Result<SettingDto>> UpdateSettingAsync(Guid id, UpdateSettingDto input)
    {
        Check.NotNull(input);

        var setting = await _settingRepository.GetAsync(id);
        if (setting == null)
            return Fail<SettingDto>("Setting not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (setting.IsSystem)
            return Fail<SettingDto>("Cannot update system setting", 403, ErrorCodes.SYSTEM_ERROR);

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
        if (setting == null)
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

        // 在进入事务前校验：存在系统配置则返回 Fail
        var systemSettings = settings.Where(s => s.IsSystem).ToList();
        if (systemSettings.Count > 0)
            return Fail($"Cannot delete system settings: {string.Join(", ", systemSettings.Select(s => s.Key))}", 403, ErrorCodes.SYSTEM_ERROR);

        var count = settings.Count;
        var settingKeys = settings.Select(s => s.Key).ToList();

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            await _settingRepository.DeleteManyAsync(settings);
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
    public async Task<Result<List<SettingGroupDto>>> GetSettingGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _settingRepository.AsQueryable()
            .AsNoTracking()
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
    /// Publish a SettingChangedEvent. EventBus is optional (apps without EventBusModule loaded),
    /// failures are swallowed so config writes never block on event delivery.
    /// </summary>
    private async Task PublishSettingChangedAsync(string key, SettingScope scope, string? scopeId, string? newValue, bool isRemoval)
    {
        if (EventBus == null)
            return;

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
}
