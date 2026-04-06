namespace Tnzi.AI.Options;

/// <summary>
/// 对话摘要压缩触发方式
/// </summary>
public enum SummarizationTriggerType
{
    /// <summary>按上下文窗口比例触发（如 0.8 = 80%）</summary>
    Fraction = 0,

    /// <summary>按绝对 token 数触发</summary>
    Tokens = 1,

    /// <summary>按消息数触发</summary>
    Messages = 2
}

/// <summary>
/// 摘要触发条件配置
/// </summary>
public class SummarizationTrigger
{
    /// <summary>触发方式</summary>
    public SummarizationTriggerType Type { get; set; } = SummarizationTriggerType.Fraction;

    /// <summary>比例阈值（Type=Fraction 时使用，默认 0.93，与 Claude Code 一致）</summary>
    public double FractionThreshold { get; set; } = 0.93;

    /// <summary>Token 数阈值（Type=Tokens 时使用）</summary>
    public int TokenThreshold { get; set; } = 100_000;

    /// <summary>消息数阈值（Type=Messages 时使用）</summary>
    public int MessageThreshold { get; set; } = 50;
}

/// <summary>
/// 上下文保留配置
/// </summary>
public class ContextRetention
{
    /// <summary>保留最近 N 条完整消息（不参与摘要）</summary>
    public int KeepLastMessages { get; set; } = 6;

    /// <summary>始终保留系统消息</summary>
    public bool KeepSystemMessages { get; set; } = true;
}

/// <summary>
/// 对话摘要配置选项
/// </summary>
public class SummarizationOptions
{
    /// <summary>是否启用摘要（默认关闭）</summary>
    public bool Enabled { get; set; }

    /// <summary>触发条件</summary>
    public SummarizationTrigger Trigger { get; set; } = new();

    /// <summary>上下文保留策略</summary>
    public ContextRetention Keep { get; set; } = new();

    /// <summary>参与摘要的最少 token 数（低于此值不触发摘要）</summary>
    public int TrimTokensToSummarize { get; set; } = 4000;

    /// <summary>摘要用的模型名称（null=使用当前 Agent 的模型）</summary>
    public string? ModelName { get; set; }

    /// <summary>自定义摘要提示词（null=使用内置默认）</summary>
    public string? SummaryPrompt { get; set; }

    /// <summary>模型上下文窗口大小（token 数，用于 Fraction 触发计算）</summary>
    public int ModelContextWindow { get; set; } = 128_000;

    /// <summary>是否启用 MicroCompact（每次执行前清理过期工具结果）。默认 true。</summary>
    public bool EnableMicroCompact { get; set; } = true;

    /// <summary>MicroCompact 保留最近的工具结果消息数。默认 5。</summary>
    public int KeepRecentToolResults { get; set; } = 5;
}
