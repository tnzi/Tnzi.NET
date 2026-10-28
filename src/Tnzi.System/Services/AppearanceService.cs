namespace Tnzi.System.Services;

/// <summary>
/// 全局外观服务实现。
/// 主题快照以不透明 JSON 文档存入 Sys_Setting（Global 作用域，每个 scope 一行），
/// 复用 <see cref="ISettingService"/> 的缓存与 SettingChangedEvent 变更链。
/// </summary>
public class AppearanceService : ApplicationService, IAppearanceService
{
    /// <summary>主题信封 Setting 键的前缀；完整键为 <c>Appearance:Theme:{scope}</c>。</summary>
    public const string ThemeSettingKeyPrefix = "Appearance:Theme:";

    /// <summary>
    /// 管理端主题在 scope 化之前使用的 Setting 键。
    /// <para>
    /// 仍会被<b>读取</b>（当 <c>admin</c> scope 的新键不存在时回退到它），这样既有部署
    /// 升级后不会突然失去已配置的主题。<b>写入</b>一律用新键，所以下一次保存即完成迁移。
    /// </para>
    /// </summary>
    public const string LegacyAdminThemeSettingKey = "Appearance:AdminTheme";

    /// <summary>外观相关 Setting 行的分组名。</summary>
    public const string AppearanceSettingGroup = "Appearance";

    /// <summary>主题文档序列化后的长度上限（字符数），防止把 Setting 表当对象存储滥用。</summary>
    /// <remarks>
    /// 这些 Setting 行为 Global 非加密行，会随 AddTnziSettings 热链进入 IConfiguration
    /// （大字符串，无害但占空间），且 scope 化之后行数会随前端产品数增长；宿主如需隔离可在
    /// <c>builder.Configuration.AddTnziSettings("Appearance:Theme", "Appearance:AdminTheme")</c> 中排除：
    /// 排除项挡住它下面的整棵子树，所以 <c>Appearance:Theme</c> 一项就覆盖了每个 scope（不影响本服务读写与前端消费）。
    /// </remarks>
    public const int MaxThemeJsonLength = 64 * 1024;

    /// <summary>
    /// scope 名的合法字形。它会成为 Setting 键的一段，所以必须收窄：
    /// 未经校验的 scope 等于让调用方任意拼写 Setting 键。
    /// </summary>
    private static readonly Regex ScopePattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.Compiled);

    /// <summary>
    /// 「已经配置了主题的 Setting 键」这一份清单的缓存键。任何主题键的变更都会清掉它
    /// （<c>SettingCacheInvalidationHandler</c> / <c>SettingChangedDistributedEventHandler</c>，见 <see cref="IsThemeSettingKey"/>）。
    /// </summary>
    public const string ConfiguredThemeKeysCacheKey = "Appearance:ConfiguredThemeKeys";

    /// <summary>清单的缓存时长，与按键缓存相同。</summary>
    private static readonly TimeSpan ConfiguredThemeKeysLifetime = TimeSpan.FromHours(1);

    private readonly ISettingService _settingService;
    private readonly IRepository<Setting, Guid> _settingRepository;
    private readonly ICache _cache;

    public AppearanceService(IServiceProvider serviceProvider, ISettingService settingService, IRepository<Setting, Guid> settingRepository, ICache cache) : base(serviceProvider)
    {
        _settingService = Check.NotNull(settingService);
        _settingRepository = Check.NotNull(settingRepository);
        _cache = Check.NotNull(cache);
    }

    /// <summary>某个 scope 的 Setting 键。</summary>
    public static string ThemeSettingKey(string scope) => ThemeSettingKeyPrefix + scope;

    /// <summary>这个 Setting 键是不是一份主题快照（任一 scope，或 scope 化之前的管理端旧键）。</summary>
    public static bool IsThemeSettingKey(string? key)
        => key is not null
           && (key.StartsWith(ThemeSettingKeyPrefix, StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, LegacyAdminThemeSettingKey, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task<Result<ThemeSnapshotDto>> GetThemeAsync(string scope)
    {
        if (!TryNormalizeScope(scope, out var normalized, out var error))
            return Fail<ThemeSnapshotDto>(error, 400, ErrorCodes.VALIDATION_ERROR);

        // ★ 读端点是匿名的，而按键读取对「没有这一行」也缓存一小时：任何合法字形的 scope 都要一次查库、留一条缓存，
        //   循环请求随机 scope 就能让缓存无界增长。先对一份「已配置的主题键」清单（一条缓存）判一次，没配置的 scope 不碰按键缓存。
        var configured = await GetConfiguredThemeKeysAsync();
        if (configured is not null
            && !configured.Contains(ThemeSettingKey(normalized))
            && !(normalized == IAppearanceService.AdminScope && configured.Contains(LegacyAdminThemeSettingKey)))
        {
            return Ok(new ThemeSnapshotDto());
        }

        var stored = await _settingService.GetSettingAsync(ThemeSettingKey(normalized));
        if (!stored.Succeeded)
            return Fail<ThemeSnapshotDto>(stored.Message ?? "Failed to load theme", stored.Code ?? 500);

        var json = stored.Data;

        // Pre-scope deployments stored the admin theme under a different key.
        // Read through to it so an upgrade does not look like "theme was reset".
        if (string.IsNullOrWhiteSpace(json) && normalized == IAppearanceService.AdminScope)
        {
            var legacy = await _settingService.GetSettingAsync(LegacyAdminThemeSettingKey);
            if (legacy.Succeeded) json = legacy.Data;
        }

        if (string.IsNullOrWhiteSpace(json))
            return Ok(new ThemeSnapshotDto());

        try
        {
            var envelope = JsonSerializer.Deserialize<ThemeEnvelope>(json, TnziJsonDefaults.Options);
            if (envelope == null || envelope.Theme.ValueKind != JsonValueKind.Object)
                return Ok(new ThemeSnapshotDto());

            return Ok(new ThemeSnapshotDto { Theme = envelope.Theme, UpdatedAt = envelope.UpdatedAt });
        }
        catch (JsonException ex)
        {
            // 损坏的存量行不应导致所有客户端加载失败：按未配置处理，下一次保存即自愈
            LogWarning("Stored theme for scope {Scope} is not valid JSON, treating as unset: {Error}", normalized, ex.Message);
            return Ok(new ThemeSnapshotDto());
        }
    }

    /// <inheritdoc />
    public async Task<Result<ThemeSnapshotDto>> SaveThemeAsync(string scope, SaveThemeSnapshotDto input)
    {
        Check.NotNull(input);

        if (!TryNormalizeScope(scope, out var normalized, out var error))
            return Fail<ThemeSnapshotDto>(error, 400, ErrorCodes.VALIDATION_ERROR);

        if (input.Theme.ValueKind != JsonValueKind.Object)
            return Fail<ThemeSnapshotDto>("Theme must be a JSON object", 400, ErrorCodes.VALIDATION_ERROR);

        if (input.Theme.GetRawText().Length > MaxThemeJsonLength)
            return Fail<ThemeSnapshotDto>($"Theme document exceeds the {MaxThemeJsonLength} character limit", 400, ErrorCodes.VALIDATION_ERROR);

        var envelope = new ThemeEnvelope { UpdatedAt = DateTime.UtcNow, Theme = input.Theme };
        var json = JsonSerializer.Serialize(envelope, TnziJsonDefaults.Options);

        var saved = await _settingService.SetSettingAsync(
            ThemeSettingKey(normalized),
            json,
            $"Global '{normalized}' theme snapshot (managed from that product's appearance settings)",
            AppearanceSettingGroup);
        if (!saved.Succeeded)
            return Fail<ThemeSnapshotDto>(saved.Message ?? "Failed to save theme", saved.Code ?? 500);

        LogInformation("Global theme for scope {Scope} saved by {UserId}", normalized, CurrentUser?.Id);
        return Ok(new ThemeSnapshotDto { Theme = input.Theme, UpdatedAt = envelope.UpdatedAt });
    }

    /// <inheritdoc />
    public async Task<Result> ResetThemeAsync(string scope)
    {
        if (!TryNormalizeScope(scope, out var normalized, out var error))
            return Fail(error, 400, ErrorCodes.VALIDATION_ERROR);

        var keys = normalized == IAppearanceService.AdminScope
            // Clear the pre-scope row too, otherwise a reset would appear to do
            // nothing: the read path falls back to it.
            ? new[] { ThemeSettingKey(normalized), LegacyAdminThemeSettingKey }
            : [ThemeSettingKey(normalized)];

        var removed = false;
        foreach (var key in keys)
        {
            var setting = await _settingRepository.FirstOrDefaultAsync(s => s.Scope == SettingScope.Global && s.Key == key);
            if (setting == null) continue;

            var deleted = await _settingService.DeleteSettingAsync(setting.Id);
            if (!deleted.Succeeded) return deleted;
            removed = true;
        }

        return Ok(removed ? "Global theme reset" : "No global theme to reset");
    }

    /// <summary>
    /// 已经配置了主题的 Setting 键（Global 作用域，不分大小写）；读不出来时为 <see langword="null"/>，调用方退回逐键读取。
    /// </summary>
    /// <remarks>
    /// 只有一条缓存，行数随前端产品数（不随请求）增长。任何主题键的写入、删除都经 <c>SettingChangedEvent</c> 清掉它，
    /// 与按键缓存同一条失效链（提交之后清），所以新保存的 scope 与按键缓存一样及时可见。
    /// </remarks>
    private async Task<HashSet<string>?> GetConfiguredThemeKeysAsync()
    {
        try
        {
            var keys = await _cache.GetAsync<List<string>>(ConfiguredThemeKeysCacheKey);
            if (keys is null)
            {
                keys = await _settingRepository.AsQueryable()
                    .AsNoTracking()
                    .Where(s => s.Scope == SettingScope.Global
                                && (s.Key.StartsWith(ThemeSettingKeyPrefix) || s.Key == LegacyAdminThemeSettingKey))
                    .Select(s => s.Key)
                    .ToListAsync();
                await _cache.SetAsync(ConfiguredThemeKeysCacheKey, keys, ConfiguredThemeKeysLifetime);
            }

            return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // 与按键读取同一取舍：读设置不该让请求整体失败。退回逐键读取（它自己也有读不到时的处理）。
            Logger.LogWarning(ex, "The list of configured theme scopes could not be read; reading the requested scope directly.");
            return null;
        }
    }

    private static bool TryNormalizeScope(string scope, out string normalized, out string error)
    {
        normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        if (!ScopePattern.IsMatch(normalized))
        {
            error = "Scope must be 1-32 characters, start with a letter and contain only lowercase letters, digits or hyphens";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed class ThemeEnvelope
    {
        public DateTime? UpdatedAt { get; set; }

        public JsonElement Theme { get; set; }
    }
}
