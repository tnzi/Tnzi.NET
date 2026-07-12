namespace Tnzi.AI.Guardrails;

/// <summary>
/// Prompt 注入检测 Guardrail — 基于关键词模式匹配检测常见注入模式
/// </summary>
/// <remarks>
/// 基础防护层，使用关键词匹配检测明显的 Prompt 注入尝试。
/// 对于高安全场景，建议配合 LLM-based 检测（自定义 IInputGuardrail 实现）。
/// </remarks>
public class PromptInjectionGuardrail : IInputGuardrail, IGuardrailProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public string Name => nameof(PromptInjectionGuardrail);

    // 常见 Prompt 注入模式（不区分大小写匹配）
    private static readonly string[] InjectionPatterns =
    [
        "ignore previous instructions",
        "ignore all previous",
        "disregard previous",
        "forget your instructions",
        "override your instructions",
        "you are now",
        "act as if you",
        "pretend you are",
        "new instructions:",
        "system prompt:",
        "ignore the above",
        "do not follow",
        "bypass your",
        "jailbreak"
    ];

    public PromptInjectionGuardrail(IOptionsMonitor<AIOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Guardrails.EnablePromptInjectionDetection)
        {
            return Task.FromResult(GuardrailResult.Allowed());
        }

        var lowerInput = input.ToLowerInvariant();

        foreach (var pattern in InjectionPatterns)
        {
            if (lowerInput.Contains(pattern, StringComparison.Ordinal))
            {
                return Task.FromResult(GuardrailResult.Rejected(
                    nameof(PromptInjectionGuardrail),
                    "Input contains a potential prompt injection pattern"));
            }
        }

        return Task.FromResult(GuardrailResult.Allowed());
    }

    public Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default)
        => IGuardrailProvider.BridgeValidateAsync(request, ValidateAsync, GuardrailReasonCodes.PromptInjectionDetected, "Prompt injection detected", ct);
}
