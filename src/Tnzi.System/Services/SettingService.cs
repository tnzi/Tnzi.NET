namespace Tnzi.System.Services;

/// <summary>
/// 配置服务实现
/// </summary>
public class SettingService : ApplicationService, ISettingService
{
    private static readonly DateTime _startTime = DateTime.UtcNow;

    private readonly IRepository<Setting, Guid> _settingRepository;
    private readonly ApplicationOptions _applicationOptions;
    private readonly ICache _cache;
    private readonly IEnumerable<ISettingProvider> _settingProviders;
    private readonly ITnziApplication? _tnziApplication;
    private readonly IHostEnvironment? _hostEnvironment;

    // 标准配置键常量
    private const string KeyAppName = "App.AppName";
    private const string KeySiteName = "App.SiteName";

    /// <summary>
    /// 缓存条目，用于区分"不存在"和"值为null"
    /// </summary>
    private record SettingCacheEntry(string? Value, bool Exists);

    public SettingService(
        IServiceProvider serviceProvider,
        IRepository<Setting, Guid> settingRepository,
        IOptions<ApplicationOptions> applicationOptions,
        ICache cache,
        IEnumerable<ISettingProvider> settingProviders,
        ITnziApplication? tnziApplication = null,
        IHostEnvironment? hostEnvironment = null)
        : base(serviceProvider)
    {
        _settingRepository = Check.NotNull(settingRepository);
        _applicationOptions = Check.NotNull(applicationOptions).Value;
        _cache = Check.NotNull(cache);
        _settingProviders = Check.NotNull(settingProviders);
        _tnziApplication = tnziApplication;
        _hostEnvironment = hostEnvironment;
    }

    /// <inheritdoc />
    public ApplicationOptions GetApplicationOptions()
    {
        return _applicationOptions;
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetAppNameAsync()
    {
        var result = await GetSettingAsync(KeyAppName);
        var value = result.Data ?? _applicationOptions.AppName;
        return Ok<string>(value);
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetSiteNameAsync()
    {
        var result = await GetSettingAsync(KeySiteName);
        var value = result.Data ?? _applicationOptions.SiteName;
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
                return Ok<string?>(cached.Exists ? cached.Value : defaultValue);
            }

            var setting = await _settingRepository
                .AsQueryable()
                .AsNoTracking()
                .Where(s => s.Scope == SettingScope.Global)
                .FirstOrDefaultAsync(s => s.Key == key);

            var exists = setting != null;
            var value = setting?.Value;

            // 写入缓存，有效期 1 小时
            await _cache.SetAsync(cacheKey, new SettingCacheEntry(value, exists), TimeSpan.FromHours(1));

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
            LogWarning("Failed to convert setting {Key} value '{Value}' to type {Type}, returning default value", key, result.Data, typeof(T).Name);
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
        return Ok((IEnumerable<SettingDto>)settingDtos);
    }

    /// <inheritdoc />
    public async Task<Result<SettingDto>> GetSettingByIdAsync(Guid id)
    {
        var setting = await _settingRepository.GetAsync(id);
        if (setting == null)
            return Fail<SettingDto>("Setting not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        return Ok(setting.MapTo<SettingDto>());
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

        LogInformation("Setting updated: {Key}", setting.Key);
        return Ok(setting.MapTo<SettingDto>(), "Setting updated successfully");
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

        return Ok<string?>(setting?.Value);
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

        return Ok("Setting updated successfully");
    }

    /// <inheritdoc />
    public Task<Result<SystemInfoDto>> GetSystemInfoAsync()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var frameworkAssembly = typeof(ITnziApplication).Assembly;

        var info = new SystemInfoDto
        {
            AppName = _applicationOptions.AppName,
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
}
