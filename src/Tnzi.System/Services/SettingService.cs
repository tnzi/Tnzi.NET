namespace Tnzi.System.Services;

/// <summary>
/// 配置服务实现
/// </summary>
public class SettingService : ApplicationService, ISettingService
{
    private readonly IRepository<Setting, Guid> _settingRepository;
    private readonly ApplicationOptions _applicationOptions;
    private readonly ICache _cache;

    // 标准配置键常量
    private const string KeyAppName = "App.AppName";
    private const string KeySiteName = "App.SiteName";

    /// <summary>
    /// 缓存条目，用于区分"不存在"和"值为null"
    /// </summary>
    internal record SettingCacheEntry(string? Value, bool Exists);

    public SettingService(
        IServiceProvider serviceProvider,
        IRepository<Setting, Guid> settingRepository,
        IOptions<ApplicationOptions> applicationOptions,
        ICache cache)
        : base(serviceProvider)
    {
        _settingRepository = Check.NotNull(settingRepository);
        _applicationOptions = Check.NotNull(applicationOptions).Value;
        _cache = Check.NotNull(cache);
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
                .Where(s => !s.IsDeleted)
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
                .Where(s => !s.IsDeleted)
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

            // 清理缓存
            await _cache.RemoveAsync($"Setting:{key}");
        });

        LogInformation("Setting updated: {Key}", key);
        return Ok("Setting updated successfully");
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<SettingDto>>> GetSettingsAsync(string? group = null)
    {
        var query = _settingRepository.AsQueryable().AsNoTracking()
            .Where(s => !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(group))
        {
            query = query.Where(s => s.Group == group);
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

        // 检查键是否已存在
        var exists = await _settingRepository
            .AsQueryable()
            .Where(s => !s.IsDeleted)
            .AnyAsync(s => s.Key == input.Key);

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

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            await _settingRepository.DeleteManyAsync(settings);

            foreach (var s in settings)
            {
                await _cache.RemoveAsync($"Setting:{s.Key}");
            }
        });

        LogInformation("Batch deleted {Count} settings", count);
        return Ok($"Deleted {count} settings successfully");
    }
}
