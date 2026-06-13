namespace Tnzi.System.Services;

/// <summary>
/// 配置中心服务实现。
/// 值链路：Setting 表 Global 覆盖 → appsettings（排除 SettingConfigurationProvider，
/// 否则覆盖值会污染"出厂默认"）→ 字段 DefaultValueAccessor 编译期默认。
/// </summary>
public class SettingsCenterService : ApplicationService, ISettingsCenterService
{
    private readonly ISettingService _settingService;
    private readonly IRepository<Setting, Guid> _settingRepository;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<ISettingDefinitionProvider> _providers;
    private readonly IEnumerable<ISettingGroupHandler> _groupHandlers;

    public SettingsCenterService(
        IServiceProvider serviceProvider,
        ISettingService settingService,
        IRepository<Setting, Guid> settingRepository,
        IConfiguration configuration,
        IEnumerable<ISettingDefinitionProvider> providers,
        IEnumerable<ISettingGroupHandler> groupHandlers)
        : base(serviceProvider)
    {
        _settingService = Check.NotNull(settingService);
        _settingRepository = Check.NotNull(settingRepository);
        _configuration = Check.NotNull(configuration);
        _providers = Check.NotNull(providers);
        _groupHandlers = Check.NotNull(groupHandlers);
    }

    /// <inheritdoc />
    public async Task<Result<List<SettingsCenterGroupDto>>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var groups = CollectGroups();
        var overrides = await LoadGlobalOverridesAsync(groups.SelectMany(g => g.Fields).Select(f => f.Key), cancellationToken);
        return Ok(groups.Select(g => ToDto(g, overrides)).ToList());
    }

    /// <inheritdoc />
    public async Task<Result<SettingsCenterGroupDto>> SaveGroupAsync(string groupKey, Dictionary<string, string?> changedValues, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(groupKey);
        Check.NotNull(changedValues);

        var group = FindGroup(groupKey);
        if (group == null)
            return Fail<SettingsCenterGroupDto>($"Setting group '{groupKey}' was not found", 404);

        var fieldMap = group.Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        // 校验同时把请求键规范化为定义里的 field.Key — 字段匹配大小写不敏感，
        // 但持久化必须用规范键，否则大小写漂移的请求会在大小写敏感数据库
        //（如 PostgreSQL）里产生重复 Setting 行。
        var canonicalValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in changedValues)
        {
            if (!fieldMap.TryGetValue(key, out var field))
                return Fail<SettingsCenterGroupDto>($"Unknown setting field '{key}' in group '{groupKey}'", 400);
            if (field.IsReadOnly)
                return Fail<SettingsCenterGroupDto>($"Setting field '{key}' is read-only", 400);

            var error = ValidateFieldValue(field, value);
            if (error != null)
                return Fail<SettingsCenterGroupDto>(error, 400);

            canonicalValues[field.Key] = value;
        }

        var handler = _groupHandlers.FirstOrDefault(h => string.Equals(h.GroupKey, group.Key, StringComparison.OrdinalIgnoreCase));
        if (handler != null)
        {
            var validation = await handler.ValidateAsync(canonicalValues, cancellationToken);
            if (!validation.Succeeded)
                return Fail<SettingsCenterGroupDto>(validation.Message ?? "Setting group validation failed", validation.Code ?? 400);
        }

        foreach (var (key, value) in canonicalValues)
        {
            var field = fieldMap[key];
            if (value == null)
            {
                var removed = await RemoveOverrideAsync(field.Key, cancellationToken);
                if (!removed.Succeeded)
                    return Fail<SettingsCenterGroupDto>(removed.Message ?? $"Failed to remove override for '{key}'", removed.Code ?? 500);
                continue;
            }

            var saved = field.IsEncrypted
                ? await _settingService.SetEncryptedAsync(group.Key, field.Key, value, field.Description)
                : await _settingService.SetSettingAsync(field.Key, value, field.Description, group.Key);
            if (!saved.Succeeded)
                return Fail<SettingsCenterGroupDto>(saved.Message ?? $"Failed to save setting '{key}'", saved.Code ?? 500);
        }

        // 显式 flush：写经 ISettingService 可能滞留在 UoW change tracker（智能保存
        // 在事务/环境 UoW 激活时推迟落库），而下面的回读是 AsNoTracking 直查数据库 —
        // 不 flush 则响应返回写之前的旧值（同事务内 SELECT 可见自己 flush 过的写）。
        await _settingRepository.SaveChangesAsync(cancellationToken);

        if (handler != null)
        {
            try
            {
                await handler.OnSavedAsync(canonicalValues, cancellationToken);
            }
            catch (Exception ex)
            {
                // 值已提交成功；副作用钩子失败不回滚也不向调用方报失败（与事件
                // 处理器同样的隔离纪律），仅记录日志供运维排查。
                Logger.LogWarning(ex, "Setting group handler OnSavedAsync failed for group {GroupKey}", group.Key);
            }
        }

        return await GetGroupDtoAsync(group, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<SettingsCenterGroupDto>> ResetGroupAsync(string groupKey, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(groupKey);

        var group = FindGroup(groupKey);
        if (group == null)
            return Fail<SettingsCenterGroupDto>($"Setting group '{groupKey}' was not found", 404);

        var overrides = await LoadGlobalOverridesAsync(group.Fields.Select(f => f.Key), cancellationToken);
        foreach (var row in overrides.Values)
        {
            var deleted = await _settingService.DeleteSettingAsync(row.Id);
            if (!deleted.Succeeded)
                return Fail<SettingsCenterGroupDto>(deleted.Message ?? $"Failed to delete override '{row.Key}'", deleted.Code ?? 500);
        }

        // 与 SaveGroupAsync 相同：flush 挂起删除，保证回读反映删除后的状态。
        await _settingRepository.SaveChangesAsync(cancellationToken);

        return await GetGroupDtoAsync(group, cancellationToken);
    }

    private SettingDefinitionGroup? FindGroup(string groupKey)
        => CollectGroups().FirstOrDefault(g => string.Equals(g.Key, groupKey, StringComparison.OrdinalIgnoreCase));

    private async Task<Result<SettingsCenterGroupDto>> GetGroupDtoAsync(SettingDefinitionGroup group, CancellationToken cancellationToken)
    {
        var overrides = await LoadGlobalOverridesAsync(group.Fields.Select(f => f.Key), cancellationToken);
        return Ok(ToDto(group, overrides));
    }

    private async Task<Result> RemoveOverrideAsync(string key, CancellationToken cancellationToken)
    {
        var overrides = await LoadGlobalOverridesAsync([key], cancellationToken);
        if (!overrides.TryGetValue(key, out var row))
            return Result.Success();
        return await _settingService.DeleteSettingAsync(row.Id);
    }

    private static string? ValidateFieldValue(SettingFieldDefinition field, string? value)
    {
        if (value == null)
            return field.IsRequired ? $"Setting field '{field.Key}' is required" : null;
        if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            return $"Setting field '{field.Key}' is required";

        switch (field.Type)
        {
            case SettingFieldType.Int:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                    return $"Setting field '{field.Key}' must be an integer";
                if (field.Min.HasValue && longValue < field.Min.Value)
                    return $"Setting field '{field.Key}' must be >= {field.Min.Value}";
                if (field.Max.HasValue && longValue > field.Max.Value)
                    return $"Setting field '{field.Key}' must be <= {field.Max.Value}";
                break;

            case SettingFieldType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                    return $"Setting field '{field.Key}' must be a number";
                if (field.Min.HasValue && decimalValue < (decimal)field.Min.Value)
                    return $"Setting field '{field.Key}' must be >= {field.Min.Value}";
                if (field.Max.HasValue && decimalValue > (decimal)field.Max.Value)
                    return $"Setting field '{field.Key}' must be <= {field.Max.Value}";
                break;

            case SettingFieldType.Boolean:
                if (!bool.TryParse(value, out _))
                    return $"Setting field '{field.Key}' must be 'true' or 'false'";
                break;

            case SettingFieldType.Select:
                if (field.Options == null || !field.Options.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return $"Setting field '{field.Key}' must be one of: {string.Join(", ", field.Options ?? [])}";
                break;
        }

        return null;
    }

    private List<SettingDefinitionGroup> CollectGroups()
    {
        return _providers
            .SelectMany(p => p.GetGroups())
            .OrderBy(g => g.Order)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<string, Setting>> LoadGlobalOverridesAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        // Setting 表是小表（运行时覆盖项），全量加载后内存过滤 —
        // 与既有测试基建（mock ToListAsync）兼容，避免 mock IQueryable 异步算子。
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var settings = await _settingRepository.ToListAsync(null, cancellationToken);
        return settings
            .Where(s => s.Scope == SettingScope.Global && keySet.Contains(s.Key))
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private SettingsCenterGroupDto ToDto(SettingDefinitionGroup group, Dictionary<string, Setting> overrides)
    {
        return new SettingsCenterGroupDto
        {
            Key = group.Key,
            ModuleName = group.ModuleName,
            DisplayName = group.DisplayName,
            I18nKey = group.I18nKey,
            Description = group.Description,
            Icon = group.Icon,
            Order = group.Order,
            Fields = group.Fields.Select(f => ToDto(f, overrides)).ToList(),
        };
    }

    private SettingsCenterFieldDto ToDto(SettingFieldDefinition field, Dictionary<string, Setting> overrides)
    {
        var hasOverride = overrides.TryGetValue(field.Key, out var overrideRow);
        var defaultValue = GetBaseConfigurationValue(field.Key) ?? field.DefaultValueAccessor?.Invoke();

        return new SettingsCenterFieldDto
        {
            Key = field.Key,
            Label = field.Label,
            I18nKey = field.I18nKey,
            Description = field.Description,
            Type = field.Type.ToString(),
            IsEncrypted = field.IsEncrypted,
            IsReadOnly = field.IsReadOnly,
            IsRequired = field.IsRequired,
            Min = field.Min,
            Max = field.Max,
            Options = field.Options?.ToList(),
            // 加密字段明文绝不出网（含默认值，避免 appsettings 中的密文/明文泄露）。
            Value = field.IsEncrypted ? null : (hasOverride ? overrideRow!.Value : defaultValue),
            DefaultValue = field.IsEncrypted ? null : defaultValue,
            IsOverridden = hasOverride,
            IsSet = hasOverride,
        };
    }

    /// <summary>
    /// 读 appsettings 原始值：遍历 IConfigurationRoot.Providers 按序后者覆盖前者，
    /// 排除 SettingConfigurationProvider（数据库覆盖层）。非 root 配置（测试场景）直接索引。
    /// ChainedConfigurationProvider（AddConfiguration 嵌套根）需递归展开，
    /// 否则嵌套根内的 SettingConfigurationProvider 会漏排除、配置值会漏读。
    /// </summary>
    private string? GetBaseConfigurationValue(string key)
    {
        if (_configuration is not IConfigurationRoot root)
            return _configuration[key];

        string? value = null;
        foreach (var provider in root.Providers)
            CollectBaseValue(provider, key, ref value);
        return value;
    }

    private static void CollectBaseValue(IConfigurationProvider provider, string key, ref string? value)
    {
        if (provider is ChainedConfigurationProvider { Configuration: IConfigurationRoot chainedRoot })
        {
            foreach (var inner in chainedRoot.Providers)
                CollectBaseValue(inner, key, ref value);
            return;
        }

        if (provider is SettingConfigurationProvider)
            return;

        if (provider.TryGet(key, out var candidate))
            value = candidate;
    }
}
