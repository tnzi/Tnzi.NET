namespace Tnzi.AI.Options;

/// <summary>
/// 工具结果预算配置 — 截断超限的工具结果，防止上下文膨胀
/// </summary>
public class ToolResultBudgetOptions
{
    /// <summary>
    /// 是否启用工具结果截断。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 单个工具结果的最大字符数。超过此限制的结果将被截断。默认 30000。
    /// </summary>
    public int MaxResultChars { get; set; } = 30_000;

    /// <summary>
    /// 截断时保留的预览字符数（结果开头部分）。默认 2000。
    /// </summary>
    public int PreviewChars { get; set; } = 2_000;
}
