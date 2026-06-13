namespace Tnzi.Settings;

/// <summary>
/// 配置中心单个字段定义。Key 必须是完整配置路径（如 "AI:Budget:Enabled"），
/// 与 Setting 表的 Key 和 IConfiguration 键一致 — 这是值能经
/// SettingConfigurationProvider 流入 IOptionsMonitor 热生效的前提。
/// </summary>
[ExperimentalApi(Reason = "Settings center contract is new and may evolve")]
public sealed class SettingFieldDefinition
{
    public required string Key { get; init; }

    /// <summary>英文显示名（前端 i18n 未命中 I18nKey 时的 fallback）</summary>
    public required string Label { get; init; }

    public string? I18nKey { get; init; }
    public string? Description { get; init; }
    public SettingFieldType Type { get; init; } = SettingFieldType.String;

    /// <summary>
    /// 只读派生属性：true 当且仅当 <see cref="Type"/> 为 <see cref="SettingFieldType.Password"/>，
    /// Type 是唯一真相源，下游 DTO/序列化不得将其当独立可写字段。
    /// 加密值不进 IConfiguration，仅 ISettingService.GetDecryptedAsync 单点读取。
    /// </summary>
    public bool IsEncrypted => Type == SettingFieldType.Password;

    /// <summary>只读展示（如由 appsettings 独占控制的 fail-safe 开关），PUT 时拒绝。</summary>
    public bool IsReadOnly { get; init; }

    public bool IsRequired { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>Select 类型的候选值。</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>编译期默认值访问器（如 () => new BudgetOptions().CacheTtlSeconds.ToString()）。
    /// 仅当 appsettings 未配置该键时作为 defaultValue 兜底。</summary>
    public Func<string?>? DefaultValueAccessor { get; init; }
}
