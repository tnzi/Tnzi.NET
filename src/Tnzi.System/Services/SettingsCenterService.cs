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
        // Per-group authorization: only return groups the caller may VIEW. Each
        // settings group is guarded by its own `{group}.settings.{slug}.view`
        // code (super-admin passes automatically via IPermissionChecker). When
        // the Authorization module isn't loaded (PermissionChecker == null) the
        // config center falls back to open - no fine-grained permission system.
        var visible = new List<SettingDefinitionGroup>();
        foreach (var group in CollectGroups())
        {
            if (await CanViewAsync(group))
                visible.Add(group);
        }

        var overrides = await LoadGlobalOverridesAsync(visible.SelectMany(g => g.Fields).Select(f => f.Key), cancellationToken);
        var dtos = new List<SettingsCenterGroupDto>(visible.Count);
        foreach (var group in visible)
        {
            var dto = ToDto(group, overrides);
            dto.CanEdit = await CanEditAsync(group);
            dtos.Add(dto);
        }
        return Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<Result<SettingsCenterGroupDto>> SaveGroupAsync(string groupKey, Dictionary<string, string?> changedValues, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(groupKey);
        Check.NotNull(changedValues);

        var group = FindGroup(groupKey);
        if (group == null)
            return Fail<SettingsCenterGroupDto>($"Setting group '{groupKey}' was not found", 404);

        if (!await CanEditAsync(group))
            return Fail<SettingsCenterGroupDto>($"You do not have permission to modify settings group '{groupKey}'", 403);

        var fieldMap = group.Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        // 校验同时把请求键规范化为定义里的 field.Key - 字段匹配大小写不敏感，
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

        // Options validator 预检：把「当前生效配置 + 本次候选值」绑成候选实例，跑注册的
        // IValidateOptions<T>（与运行时重绑定完全同一套验证）。没有这一步，字段级校验放行的
        // 跨字段非法组合会被持久化，reload 后消费端绑定抛 OptionsValidationException（500）。
        var candidateError = await ValidateCandidateOptionsAsync(group, fieldMap, canonicalValues);
        if (candidateError != null)
            return Fail<SettingsCenterGroupDto>(candidateError, 400);

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
        // 在事务/环境 UoW 激活时推迟落库），而下面的回读是 AsNoTracking 直查数据库 -
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

        if (!await CanEditAsync(group))
            return Fail<SettingsCenterGroupDto>($"You do not have permission to modify settings group '{groupKey}'", 403);

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
        var dto = ToDto(group, overrides);
        dto.CanEdit = await CanEditAsync(group);
        return Ok(dto);
    }

    /// <summary>用户是否可查看该组（持有 view 码或超管）。Authorization 未加载时 fail-open。</summary>
    private async Task<bool> CanViewAsync(SettingDefinitionGroup group)
    {
        if (PermissionChecker == null) return true;
        return await PermissionChecker.IsGrantedAsync(SettingsPermissionNaming.ViewCode(group));
    }

    /// <summary>用户是否可修改该组（持有 update 码或超管）。Authorization 未加载时 fail-open。</summary>
    private async Task<bool> CanEditAsync(SettingDefinitionGroup group)
    {
        if (PermissionChecker == null) return true;
        return await PermissionChecker.IsGrantedAsync(SettingsPermissionNaming.UpdateCode(group));
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

            case SettingFieldType.Duration:
                // Canonical TimeSpan 字符串（如 "00:05:00" / "1.12:00:00"）。用与 ConfigurationBinder
                // 相同的 InvariantCulture 解析，保证写回后运行时绑定不失败。
                if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _))
                    return $"Setting field '{field.Key}' must be a valid duration (e.g. d.hh:mm:ss or hh:mm:ss)";
                break;

            case SettingFieldType.Select:
                if (field.Options == null || !field.Options.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return $"Setting field '{field.Key}' must be one of: {string.Join(", ", field.Options ?? [])}";
                break;

            case SettingFieldType.String:
            case SettingFieldType.Text:
                // Pattern 在定义提取期已验证合法（无效正则启动 fail-fast），此处仅执行。
                if (!string.IsNullOrEmpty(field.Pattern) && value.Length > 0
                    && !Regex.IsMatch(value, $"^(?:{field.Pattern})$", RegexOptions.None, TimeSpan.FromSeconds(1)))
                    return $"Setting field '{field.Key}' does not match the required format";
                break;
        }

        return null;
    }

    /// <summary>
    /// 对组内每个贡献 Options 类型（GROUP MERGE 的组有多个）绑定候选实例并跑注册的
    /// IValidateOptions&lt;T&gt;。返回 null 表示通过。绑定本身失败（如经原始 CRUD 混入的
    /// 不可转换值）同样按验证失败处理。
    /// </summary>
    private async Task<string?> ValidateCandidateOptionsAsync(
        SettingDefinitionGroup group,
        Dictionary<string, SettingFieldDefinition> fieldMap,
        Dictionary<string, string?> canonicalValues)
    {
        if (group.OptionsTypes is not { Count: > 0 } || ServiceProvider == null)
            return null;

        // 候选覆盖集与各类型无关，统一构建：null（删除覆盖）回退到排除覆盖层后的
        // base 值；非 null 直接覆盖。加密字段不进 IConfiguration - 本次候选用请求明文，
        // 未变更但已存的用解密值注入，让 validator 看到与真实运行状态一致的实例
        //（明文仅在本方法栈内存活）。
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in canonicalValues)
        {
            if (fieldMap[key].IsEncrypted) continue;
            overrides[key] = value ?? GetBaseConfigurationValue(key);
        }

        foreach (var field in group.Fields.Where(f => f.IsEncrypted))
        {
            if (canonicalValues.TryGetValue(field.Key, out var candidate))
            {
                overrides[field.Key] = candidate;
            }
            else
            {
                var stored = await _settingService.GetDecryptedAsync(group.Key, field.Key);
                if (stored.Succeeded && stored.Data != null)
                    overrides[field.Key] = stored.Data;
            }
        }

        foreach (var optionsType in group.OptionsTypes)
        {
            var error = ValidateCandidateForType(optionsType, overrides);
            if (error != null)
                return error;
        }

        return null;
    }

    private string? ValidateCandidateForType(Type optionsType, Dictionary<string, string?> overrides)
    {
        // 当前生效值（IConfiguration 已含 DB 覆盖层）为基底 + 本次候选覆盖。
        var section = ConfigSectionResolver.Resolve(optionsType);
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _configuration.GetSection(section).AsEnumerable())
            data[key] = value;
        foreach (var (key, value) in overrides)
            data[key] = value;

        object candidateInstance;
        try
        {
            var candidateConfig = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
            candidateInstance = candidateConfig.GetSection(section).Get(optionsType)
                ?? Activator.CreateInstance(optionsType)!;
        }
        catch (InvalidOperationException ex)
        {
            return $"Setting values cannot be bound to '{optionsType.Name}': {ex.GetBaseException().Message}";
        }

        var validatorContract = typeof(IValidateOptions<>).MakeGenericType(optionsType);
        var validateMethod = validatorContract.GetMethod(nameof(IValidateOptions<object>.Validate))!;
        foreach (var validator in ServiceProvider!.GetServices(validatorContract))
        {
            if (validator == null) continue;
            var result = (ValidateOptionsResult)validateMethod.Invoke(validator, [Microsoft.Extensions.Options.Options.DefaultName, candidateInstance])!;
            if (result.Failed)
                return $"Setting validation failed: {string.Join("; ", result.Failures ?? [result.FailureMessage ?? "unknown"])}";
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
        // Setting 表是小表（运行时覆盖项），全量加载后内存过滤 -
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
            IsBuiltIn = group.IsBuiltIn,
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
            Pattern = field.Pattern,
            Options = field.Options?.ToList(),
            Subsection = field.Subsection,
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
