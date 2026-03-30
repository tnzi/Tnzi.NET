
namespace Tnzi.AI.Guardrails;

/// <summary>
/// 输入长度限制 Guardrail — 拒绝超过最大长度的输入
/// </summary>
public class MaxLengthGuardrail : IInputGuardrail, IGuardrailProvider
{
    private readonly GuardrailsOptions _guardrailOptions;

    public string Name => nameof(MaxLengthGuardrail);

    public MaxLengthGuardrail(IOptions<AIOptions> options)
    {
        _guardrailOptions = Check.NotNull(options).Value.Guardrails;
    }

    public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
    {
        if (!_guardrailOptions.EnableMaxLength)
        {
            return Task.FromResult(GuardrailResult.Allowed());
        }

        if (input.Length > _guardrailOptions.MaxInputLength)
        {
            return Task.FromResult(GuardrailResult.Rejected(
                nameof(MaxLengthGuardrail),
                $"Input exceeds maximum length of {_guardrailOptions.MaxInputLength} characters (actual: {input.Length})"));
        }

        return Task.FromResult(GuardrailResult.Allowed());
    }

    public Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default)
        => IGuardrailProvider.BridgeValidateAsync(request, ValidateAsync, GuardrailReasonCodes.MaxLengthExceeded, "Input exceeds maximum length", ct);
}
