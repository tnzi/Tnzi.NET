namespace Tnzi.AI.Guardrails;

/// <summary>
/// 输入长度限制 Guardrail - 拒绝超过最大长度的输入
/// </summary>
public class MaxLengthGuardrail : IInputGuardrail, IGuardrailProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public string Name => nameof(MaxLengthGuardrail);

    public MaxLengthGuardrail(IOptionsMonitor<AIOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
    {
        var guardrailOptions = _options.CurrentValue.Guardrails;
        if (!guardrailOptions.EnableMaxLength)
        {
            return Task.FromResult(GuardrailResult.Allowed());
        }

        if (input.Length > guardrailOptions.MaxInputLength)
        {
            return Task.FromResult(GuardrailResult.Rejected(
                nameof(MaxLengthGuardrail),
                $"Input exceeds maximum length of {guardrailOptions.MaxInputLength} characters (actual: {input.Length})"));
        }

        return Task.FromResult(GuardrailResult.Allowed());
    }

    public Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default)
        => IGuardrailProvider.BridgeValidateAsync(request, ValidateAsync, GuardrailReasonCodes.MaxLengthExceeded, "Input exceeds maximum length", ct);
}
