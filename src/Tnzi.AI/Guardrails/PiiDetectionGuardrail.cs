namespace Tnzi.AI.Guardrails;

/// <summary>
/// PII（个人身份信息）检测 Guardrail — 基于正则匹配检测常见 PII 模式
/// </summary>
/// <remarks>
/// Detects common PII patterns (email, phone, ID card, credit card) by regex.
/// This guardrail is reject-only: when PII is detected the input is REJECTED
/// and the user sees an error message. There is no redaction/sanitize-then-allow mode.
/// </remarks>
public partial class PiiDetectionGuardrail : IInputGuardrail, IGuardrailProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public string Name => nameof(PiiDetectionGuardrail);

    // 邮箱地址
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    // 中国手机号
    [GeneratedRegex(@"1[3-9]\d{9}", RegexOptions.Compiled)]
    private static partial Regex ChinaPhoneRegex();

    // 中国身份证号（18 位）
    [GeneratedRegex(@"\d{17}[\dXx]", RegexOptions.Compiled)]
    private static partial Regex ChinaIdCardRegex();

    // 信用卡号（基本模式：13-19 位数字）
    [GeneratedRegex(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{1,7}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();

    private static readonly (Regex Regex, string Name)[] PiiPatterns =
    [
        (EmailRegex(), "email"),
        (ChinaPhoneRegex(), "phone"),
        (ChinaIdCardRegex(), "id_card"),
        (CreditCardRegex(), "credit_card")
    ];

    public PiiDetectionGuardrail(IOptionsMonitor<AIOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Guardrails.EnablePiiDetection)
        {
            return Task.FromResult(GuardrailResult.Allowed());
        }

        foreach (var (regex, name) in PiiPatterns)
        {
            if (regex.IsMatch(input))
            {
                return Task.FromResult(GuardrailResult.Rejected(
                    nameof(PiiDetectionGuardrail),
                    $"Input contains potential PII ({name}). Remove personal information before sending."));
            }
        }

        return Task.FromResult(GuardrailResult.Allowed());
    }

    public Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default)
        => IGuardrailProvider.BridgeValidateAsync(request, ValidateAsync, GuardrailReasonCodes.PiiDetected, "PII detected", ct);
}
