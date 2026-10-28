namespace Tnzi.AI.Options;

/// <summary>
/// 实体记忆配置选项
/// </summary>
[ConfigSection("AI:ContextProviders:EntityMemory")]
[RuntimeSettingGroup(Key = "ai-memory", Module = "AI", DisplayName = "Memory",
    I18nKey = "admin.modules.system.settings.groups.aiMemory", Icon = "mdi:brain", Order = 150)]
public class EntityMemoryOptions
{
    /// <summary>
    /// 是否启用实体记忆上下文提供器
    /// </summary>
    [RuntimeSetting(Label = "Entity Memory Enabled", I18n = "admin.modules.system.settings.fields.entityMemoryEnabled",
        Type = SettingFieldType.Boolean, Subsection = "Entity Memory")]
    public bool Enabled { get; set; }

    /// <summary>
    /// 每次上下文注入的最大实体数量（按最近提及时间取最新的这么多个；存储的实体数不设上限）
    /// </summary>
    public int MaxEntitiesPerContext { get; set; } = 30;

    /// <summary>
    /// 实体抽取使用的提供商名称（null 使用默认提供商）
    /// </summary>
    public string? ExtractionProvider { get; set; }

    /// <summary>
    /// 实体抽取使用的模型 ID（null 使用提供商默认模型）
    /// </summary>
    public string? ExtractionModel { get; set; }

    /// <summary>
    /// 实体过期时间（基于 LastMentioned，null=不过期）
    /// </summary>
    public TimeSpan? EntityExpiration { get; set; }
}
