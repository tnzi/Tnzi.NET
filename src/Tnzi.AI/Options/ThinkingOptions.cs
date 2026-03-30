namespace Tnzi.AI.Options;

/// <summary>
/// 推理强度等级
/// </summary>
public enum ReasoningEffort
{
    /// <summary>不启用推理</summary>
    None = 0,

    /// <summary>低强度推理</summary>
    Low = 1,

    /// <summary>中等强度推理</summary>
    Medium = 2,

    /// <summary>高强度推理（深度推理）</summary>
    High = 3,

    /// <summary>最高强度（Anthropic Opus / OpenAI xhigh）</summary>
    Max = 4
}

/// <summary>
/// Thinking/reasoning 配置选项
/// </summary>
public class ThinkingOptions
{
    /// <summary>
    /// 推理强度等级。None = 不启用推理。
    /// </summary>
    public ReasoningEffort Effort { get; set; } = ReasoningEffort.None;

    /// <summary>
    /// 最大推理 Token 预算（可选，Provider 特定）
    /// </summary>
    public int? BudgetTokens { get; set; }
}
