namespace Tnzi.AI.Options;

/// <summary>
/// 安全防护 (Guardrails) 配置选项
/// </summary>
public class GuardrailsOptions
{
    /// <summary>
    /// 是否启用 Guardrails（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否启用输入长度限制
    /// </summary>
    public bool EnableMaxLength { get; set; } = true;

    /// <summary>
    /// 最大输入长度（字符数，默认 50000）
    /// </summary>
    public int MaxInputLength { get; set; } = 50_000;

    /// <summary>
    /// 是否启用 Prompt 注入检测
    /// </summary>
    public bool EnablePromptInjectionDetection { get; set; } = true;

    /// <summary>
    /// 是否启用 PII 检测
    /// </summary>
    public bool EnablePiiDetection { get; set; }

    /// <summary>
    /// 是否启用输出内容过滤
    /// </summary>
    public bool EnableContentFilter { get; set; }

    /// <summary>
    /// 输出内容过滤的屏蔽关键词列表
    /// </summary>
    public List<string> BlockedOutputKeywords { get; set; } = [];

    /// <summary>
    /// Guardrail 执行模式（默认顺序执行，支持并行执行 + Tripwire 立即中止）
    /// </summary>
    public GuardrailExecutionMode ExecutionMode { get; set; } = GuardrailExecutionMode.Sequential;

    /// <summary>
    /// 流式输出 Guardrail 缓冲区大小（字符数，默认 500）
    /// </summary>
    /// <remarks>
    /// 流式场景下，每累积此数量的字符后执行一次输出 Guardrail 检查，
    /// 通过后才将缓冲的 chunk 释放给客户端。设为 0 禁用缓冲（退化为后验检查）。
    /// </remarks>
    public int StreamingBufferSize { get; set; } = 500;

    /// <summary>
    /// 流式输出防护滑动窗口重叠大小（Token 数），用于检测跨窗口边界的违规关键词。
    /// 默认 50。
    /// </summary>
    public int StreamingOverlapSize { get; set; } = 50;

    /// <summary>
    /// Fail-closed 模式 — 当 IGuardrailProvider 抛出异常时视为拒绝（默认 true）。
    /// 设为 false 时异常将被忽略（fail-open）。
    /// </summary>
    public bool FailClosed { get; set; } = true;

    /// <summary>
    /// 工具白名单/黑名单配置
    /// </summary>
    public AllowlistGuardrailOptions Allowlist { get; set; } = new();

    /// <summary>
    /// LLM-as-Judge guardrail 配置
    /// </summary>
    public LlmJudgeOptions LlmJudge { get; set; } = new();
}

/// <summary>
/// Guardrail 执行模式
/// </summary>
public enum GuardrailExecutionMode
{
    /// <summary>
    /// 顺序执行，遇到第一个拒绝即停止
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行执行所有 guardrail，支持 tripwire 立即中止
    /// </summary>
    Parallel
}
