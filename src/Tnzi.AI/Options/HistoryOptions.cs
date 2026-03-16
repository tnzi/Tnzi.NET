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
public class HistoryStoreOptions
{
    /// <summary>
    /// 是否启用历史存储（通过 ConversationContext 持久化）
    /// </summary>
    /// <remarks>
    /// 启用后，消息历史将通过 ConversationContext 机制管理，
    /// 而非在 Service 层手动持久化。默认关闭以保持向后兼容。
    /// </remarks>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 从存储加载的最大消息数量（防止长对话一次性加载全部历史）
    /// </summary>
    /// <remarks>
    /// 仅限制初始加载量，HistoryReducer 在此基础上进一步裁剪/摘要。
    /// 设为 null 则不限制。默认 100 条。
    /// </remarks>
    public int? MaxLoadedMessages { get; set; } = 100;
}

/// <summary>
/// 历史压缩/裁剪配置选项
/// </summary>
public class HistoryReductionOptions
{
    /// <summary>
    /// 压缩模式
    /// </summary>
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
    Summarize = 2
}

/// <summary>
/// Prune（裁剪）配置选项
/// </summary>
public class PruneOptions
{
    /// <summary>
    /// 保留最近的对话轮数
    /// </summary>
    public int KeepLastTurns { get; set; } = 20;

    /// <summary>
    /// 删除超过指定轮数的工具输出
    /// </summary>
    /// <remarks>
    /// 工具输出通常较长，可以更激进地裁剪
    /// </remarks>
    public int? DropToolOutputsOlderThan { get; set; }

    /// <summary>
    /// 受保护的工具名称列表（不裁剪其输出）
    /// </summary>
    public List<string> ProtectedTools { get; set; } = [];
}

/// <summary>
/// Summarize（摘要）配置选项
/// </summary>
public class SummarizeOptions
{
    /// <summary>
    /// 用于摘要的提供商名称（为空则使用默认提供商）
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 用于摘要的模型 ID（为空则使用提供商默认模型）
    /// </summary>
    public string? SummaryModelId { get; set; }

    /// <summary>
    /// 摘要分段数（将历史分成多段分别摘要）
    /// </summary>
    public int Parts { get; set; } = 1;

    /// <summary>
    /// 自定义摘要提示词
    /// </summary>
    public string? SummaryPrompt { get; set; }

    /// <summary>
    /// 触发摘要的消息数量阈值
    /// </summary>
    public int MessageThreshold { get; set; } = 50;

    /// <summary>
    /// 触发摘要的 Token 数量阈值（可选）
    /// </summary>
    public int? TokenThreshold { get; set; } = 64_000;

    /// <summary>
    /// 保留最近的对话轮数（不摘要）
    /// </summary>
    public int KeepRecentTurns { get; set; } = 10;

    /// <summary>
    /// 摘要的最大 Token 数
    /// </summary>
    public int MaxSummaryTokens { get; set; } = 4000;
}
