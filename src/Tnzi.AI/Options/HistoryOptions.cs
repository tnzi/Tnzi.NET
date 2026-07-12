namespace Tnzi.AI.Options;

/// <summary>
/// 历史记录配置选项
/// </summary>
public class HistoryOptions
{
    /// <summary>
    /// 历史存储配置
    /// </summary>
    public HistoryStoreOptions Store { get; set; } = new();

    /// <summary>
    /// 历史压缩/裁剪配置
    /// </summary>
    public HistoryReductionOptions Reduction { get; set; } = new();
}

/// <summary>
/// 历史存储配置选项
/// </summary>
[ConfigSection("AI:History:Store")]
[RuntimeSettingGroup(Key = "ai-history", Module = "AI", DisplayName = "History",
    I18nKey = "admin.modules.system.settings.groups.aiHistory", Icon = "mdi:history", Order = 145)]
public class HistoryStoreOptions
{
    /// <summary>
    /// 是否启用历史存储（通过 ConversationContext 持久化）
    /// </summary>
    /// <remarks>
    /// 启用后，消息历史将通过 ConversationContext 机制管理，
    /// 而非在 Service 层手动持久化。默认关闭以保持向后兼容。
    /// 注意：当前无运行时消费者，故不作为可热配字段暴露。
    /// </remarks>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 从存储加载的最大消息数量（防止长对话一次性加载全部历史）
    /// </summary>
    /// <remarks>
    /// 仅限制初始加载量，HistoryReducer 在此基础上进一步裁剪/摘要。
    /// 设为 null 则不限制。默认 100 条。
    /// </remarks>
    [RuntimeSetting(Label = "Max Loaded Messages", I18n = "admin.modules.system.settings.fields.historyMaxLoadedMessages",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Store",
        Description = "Maximum number of history messages loaded per turn")]
    public int? MaxLoadedMessages { get; set; } = 100;
}

/// <summary>
/// 历史压缩/裁剪配置选项
/// </summary>
[ConfigSection("AI:History:Reduction")]
[RuntimeSettingGroup(Key = "ai-history", Module = "AI", DisplayName = "History",
    I18nKey = "admin.modules.system.settings.groups.aiHistory", Icon = "mdi:history", Order = 145)]
public class HistoryReductionOptions
{
    /// <summary>
    /// 压缩模式
    /// </summary>
    [RuntimeSetting(Label = "Reduction Mode", I18n = "admin.modules.system.settings.fields.historyReductionMode",
        Type = SettingFieldType.Select,
        Description = "How conversation history is compacted: None / Prune / Summarize / PruneThenSummarize")]
    public HistoryReductionMode Mode { get; set; } = HistoryReductionMode.None;

    /// <summary>
    /// Prune（裁剪）模式配置
    /// </summary>
    public PruneOptions Prune { get; set; } = new();

    /// <summary>
    /// Summarize（摘要）模式配置
    /// </summary>
    public SummarizeOptions Summarize { get; set; } = new();
}

/// <summary>
/// 历史压缩模式
/// </summary>
public enum HistoryReductionMode
{
    /// <summary>
    /// 不压缩，保留完整历史
    /// </summary>
    None = 0,

    /// <summary>
    /// 裁剪模式：移除旧消息，保留最近的消息
    /// </summary>
    Prune = 1,

    /// <summary>
    /// 摘要模式：对历史消息生成摘要
    /// </summary>
    Summarize = 2,

    /// <summary>
    /// 链式模式：先裁剪旧消息，再对剩余消息生成摘要。
    /// 结合低成本裁剪和选择性摘要，实现最优 Token 效率。
    /// </summary>
    PruneThenSummarize = 3
}

/// <summary>
/// Prune（裁剪）配置选项
/// </summary>
[ConfigSection("AI:History:Reduction:Prune")]
[RuntimeSettingGroup(Key = "ai-history", Module = "AI", DisplayName = "History",
    I18nKey = "admin.modules.system.settings.groups.aiHistory", Icon = "mdi:history", Order = 145)]
public class PruneOptions
{
    /// <summary>
    /// 保留最近的对话轮数
    /// </summary>
    [RuntimeSetting(Label = "Keep Last Turns", I18n = "admin.modules.system.settings.fields.historyPruneKeepLastTurns",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Prune")]
    public int KeepLastTurns { get; set; } = 20;

    /// <summary>
    /// 删除超过指定轮数的工具输出
    /// </summary>
    /// <remarks>
    /// 工具输出通常较长，可以更激进地裁剪
    /// </remarks>
    [RuntimeSetting(Label = "Drop Tool Outputs Older Than (turns)", I18n = "admin.modules.system.settings.fields.historyPruneDropToolOutputsOlderThan",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Prune",
        Description = "Drop tool outputs older than this many turns (empty = keep all)")]
    public int? DropToolOutputsOlderThan { get; set; }

    /// <summary>
    /// 受保护的工具名称列表（不裁剪其输出）
    /// </summary>
    public List<string> ProtectedTools { get; set; } = [];
}

/// <summary>
/// Summarize（摘要）配置选项
/// </summary>
[ConfigSection("AI:History:Reduction:Summarize")]
[RuntimeSettingGroup(Key = "ai-history", Module = "AI", DisplayName = "History",
    I18nKey = "admin.modules.system.settings.groups.aiHistory", Icon = "mdi:history", Order = 145)]
public class SummarizeOptions
{
    /// <summary>
    /// 用于摘要的提供商名称（为空则使用默认提供商）
    /// </summary>
    [RuntimeSetting(Label = "Summary Provider", I18n = "admin.modules.system.settings.fields.historySummarizeProvider",
        Subsection = "Summarize", Description = "Provider used to generate summaries (empty = default provider)")]
    public string? Provider { get; set; }

    /// <summary>
    /// 用于摘要的模型 ID（为空则使用提供商默认模型）
    /// </summary>
    [RuntimeSetting(Label = "Summary Model", I18n = "admin.modules.system.settings.fields.historySummarizeModelId",
        Subsection = "Summarize")]
    public string? SummaryModelId { get; set; }

    /// <summary>
    /// 摘要分段数（将历史分成多段分别摘要）
    /// </summary>
    [RuntimeSetting(Label = "Summary Parts", I18n = "admin.modules.system.settings.fields.historySummarizeParts",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Summarize")]
    public int Parts { get; set; } = 1;

    /// <summary>
    /// 自定义摘要提示词
    /// </summary>
    [RuntimeSetting(Label = "Summary Prompt", I18n = "admin.modules.system.settings.fields.historySummarizeSummaryPrompt",
        Type = SettingFieldType.Text, Subsection = "Summarize")]
    public string? SummaryPrompt { get; set; }

    /// <summary>
    /// 触发摘要的消息数量阈值
    /// </summary>
    [RuntimeSetting(Label = "Message Threshold", I18n = "admin.modules.system.settings.fields.historySummarizeMessageThreshold",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Summarize")]
    public int MessageThreshold { get; set; } = 50;

    /// <summary>
    /// 触发摘要的 Token 数量阈值（可选）
    /// </summary>
    [RuntimeSetting(Label = "Token Threshold", I18n = "admin.modules.system.settings.fields.historySummarizeTokenThreshold",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Summarize",
        Description = "Token count that triggers summarization (empty = disabled)")]
    public int? TokenThreshold { get; set; } = 64_000;

    /// <summary>
    /// 保留最近的对话轮数（不摘要）
    /// </summary>
    [RuntimeSetting(Label = "Keep Recent Turns", I18n = "admin.modules.system.settings.fields.historySummarizeKeepRecentTurns",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Summarize")]
    public int KeepRecentTurns { get; set; } = 10;

    /// <summary>
    /// 摘要的最大 Token 数
    /// </summary>
    [RuntimeSetting(Label = "Max Summary Tokens", I18n = "admin.modules.system.settings.fields.historySummarizeMaxSummaryTokens",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Summarize")]
    public int MaxSummaryTokens { get; set; } = 4000;
}
