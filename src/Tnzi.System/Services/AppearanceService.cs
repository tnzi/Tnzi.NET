namespace Tnzi.System.Services;

/// <summary>
/// 全局外观服务实现。
/// 主题快照以不透明 JSON 文档存入 Sys_Setting（Global 作用域），
/// 复用 <see cref="ISettingService"/> 的缓存与 SettingChangedEvent 变更链。
/// </summary>
public class AppearanceService : ApplicationService, IAppearanceService
{
    /// <summary>存储全局管理端主题信封的 Setting 键。</summary>
    public const string AdminThemeSettingKey = "Appearance:AdminTheme";

    /// <summary>外观相关 Setting 行的分组名。</summary>
    public const string AppearanceSettingGroup = "Appearance";

    /// <summary>主题文档序列化后的长度上限（字符数），防止把 Setting 表当对象存储滥用。</summary>
    /// <remarks>
    /// 该 Setting 行为 Global 非加密行，会随 AddTnziSettings 热链进入 IConfiguration
    /// （单键大字符串，无害但占空间）；宿主如需隔离可在
    /// <c>builder.Configuration.AddTnziSettings(excludedKeys: AppearanceService.AdminThemeSettingKey)</c>
    /// 中排除该键（不影响本服务读写与前端消费）。
    /// </remarks>
    public const int MaxThemeJsonLength = 64 * 1024;

    private readonly ISettingService _settingService;
    private readonly IRepository<Setting, Guid> _settingRepository;

    public AppearanceService(IServiceProvider serviceProvider, ISettingService settingService, IRepository<Setting, Guid> settingRepository) : base(serviceProvider)
    {
        _settingService = Check.NotNull(settingService);
        _settingRepository = Check.NotNull(settingRepository);
    }

    /// <inheritdoc />
    public async Task<Result<AdminThemeDto>> GetAdminThemeAsync()
    {
        var stored = await _settingService.GetSettingAsync(AdminThemeSettingKey);
        if (!stored.Succeeded)
            return Fail<AdminThemeDto>(stored.Message ?? "Failed to load admin theme", stored.Code ?? 500);

        if (string.IsNullOrWhiteSpace(stored.Data))
            return Ok(new AdminThemeDto());

        try
        {
            var envelope = JsonSerializer.Deserialize<AdminThemeEnvelope>(stored.Data, TnziJsonDefaults.Options);
            if (envelope == null || envelope.Theme.ValueKind != JsonValueKind.Object)
                return Ok(new AdminThemeDto());

            return Ok(new AdminThemeDto { Theme = envelope.Theme, UpdatedAt = envelope.UpdatedAt });
        }
        catch (JsonException ex)
        {
            // 损坏的存量行不应导致所有客户端加载失败：按未配置处理，下一次保存即自愈
            LogWarning("Stored admin theme is not valid JSON, treating as unset: {Error}", ex.Message);
            return Ok(new AdminThemeDto());
        }
    }

    /// <inheritdoc />
    public async Task<Result<AdminThemeDto>> SaveAdminThemeAsync(SaveAdminThemeDto input)
    {
        Check.NotNull(input);

        if (input.Theme.ValueKind != JsonValueKind.Object)
            return Fail<AdminThemeDto>("Theme must be a JSON object", 400, ErrorCodes.VALIDATION_ERROR);

        if (input.Theme.GetRawText().Length > MaxThemeJsonLength)
            return Fail<AdminThemeDto>($"Theme document exceeds the {MaxThemeJsonLength} character limit", 400, ErrorCodes.VALIDATION_ERROR);

        var envelope = new AdminThemeEnvelope { UpdatedAt = DateTime.UtcNow, Theme = input.Theme };
        var json = JsonSerializer.Serialize(envelope, TnziJsonDefaults.Options);

        var saved = await _settingService.SetSettingAsync(AdminThemeSettingKey, json, "Global admin theme snapshot (managed via the admin theme drawer)", AppearanceSettingGroup);
        if (!saved.Succeeded)
            return Fail<AdminThemeDto>(saved.Message ?? "Failed to save admin theme", saved.Code ?? 500);

        LogInformation("Global admin theme saved by {UserId}", CurrentUser?.Id);
        return Ok(new AdminThemeDto { Theme = input.Theme, UpdatedAt = envelope.UpdatedAt });
    }

    /// <inheritdoc />
    public async Task<Result> ResetAdminThemeAsync()
    {
        var setting = await _settingRepository.FirstOrDefaultAsync(s => s.Scope == SettingScope.Global && s.Key == AdminThemeSettingKey);
        if (setting == null)
            return Ok("No global admin theme to reset");

        return await _settingService.DeleteSettingAsync(setting.Id);
    }

    private sealed class AdminThemeEnvelope
    {
        public DateTime? UpdatedAt { get; set; }

        public JsonElement Theme { get; set; }
    }
}
