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
    /// <c>builder.Configuration.AddTnziSettings(excludedKeys: …)</c> 中排除
    /// <see cref="ThemeSettingKeyPrefix"/> 下的键（不影响本服务读写与前端消费）。
    /// </remarks>
    public const int MaxThemeJsonLength = 64 * 1024;

    /// <summary>
    /// scope 名的合法字形。它会成为 Setting 键的一段，所以必须收窄：
    /// 未经校验的 scope 等于让调用方任意拼写 Setting 键。
    /// </summary>
    private static readonly Regex ScopePattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.Compiled);

    private readonly ISettingService _settingService;
    private readonly IRepository<Setting, Guid> _settingRepository;

    public AppearanceService(IServiceProvider serviceProvider, ISettingService settingService, IRepository<Setting, Guid> settingRepository) : base(serviceProvider)
    {
        _settingService = Check.NotNull(settingService);
        _settingRepository = Check.NotNull(settingRepository);
    }

    /// <summary>某个 scope 的 Setting 键。</summary>
    public static string ThemeSettingKey(string scope) => ThemeSettingKeyPrefix + scope;

    /// <inheritdoc />
    public async Task<Result<ThemeSnapshotDto>> GetThemeAsync(string scope)
    {
        if (!TryNormalizeScope(scope, out var normalized, out var error))
            return Fail<ThemeSnapshotDto>(error, 400, ErrorCodes.VALIDATION_ERROR);

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
