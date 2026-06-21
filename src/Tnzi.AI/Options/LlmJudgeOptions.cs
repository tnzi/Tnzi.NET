namespace Tnzi.AI.Options;

/// <summary>
/// LLM-as-Judge guardrail 配置
/// </summary>
public class LlmJudgeOptions
{
    /// <summary>是否启用 LLM 评估</summary>
    public bool Enabled { get; set; }

    /// <summary>LLM 提供者名称（null 使用默认）</summary>
    public string? Provider { get; set; }

    /// <summary>LLM 模型 ID</summary>
    public string? Model { get; set; }

    /// <summary>输入评估系统提示词</summary>
    public string? InputJudgePrompt { get; set; }

    /// <summary>输出评估系统提示词</summary>
    public string? OutputJudgePrompt { get; set; }

    /// <summary>工具调用评估系统提示词（工具级 IGuardrailProvider 评估时使用）</summary>
    public string? ToolJudgePrompt { get; set; }

    /// <summary>超时秒数</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
