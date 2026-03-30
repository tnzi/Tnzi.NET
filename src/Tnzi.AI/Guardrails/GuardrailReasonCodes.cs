namespace Tnzi.AI.Guardrails;

/// <summary>
/// Guardrail 拒绝原因代码常量
/// </summary>
public static class GuardrailReasonCodes
{
    public const string MaxLengthExceeded = "max_length_exceeded";
    public const string PromptInjectionDetected = "prompt_injection_detected";
    public const string PiiDetected = "pii_detected";
    public const string BlockedContent = "blocked_content";
    public const string LlmJudgeRejected = "llm_judge_rejected";
    public const string ToolDenied = "tool_denied";
    public const string ToolNotAllowed = "tool_not_allowed";
}

/// <summary>
/// GuardrailRejectionEvent.Direction 常量
/// </summary>
public static class GuardrailDirections
{
    public const string Input = "input";
    public const string Output = "output";
    public const string Tool = "tool";
}
