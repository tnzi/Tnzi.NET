namespace Tnzi.AI.Guardrails;

/// <summary>
/// 输出内容过滤 Guardrail — 检测 AI 响应中的敏感内容
/// </summary>
/// <remarks>
/// 支持通过配置自定义关键词列表。默认包含基本的安全检查。
/// </remarks>
public class ContentFilterGuardrail : IOutputGuardrail, IGuardrailProvider
{
    private readonly bool _enabled;
    private readonly List<string> _blockedKeywords;

    public string Name => nameof(ContentFilterGuardrail);

    public ContentFilterGuardrail(IOptions<AIOptions> options)
    {
        var guardrails = Check.NotNull(options).Value.Guardrails;
        _enabled = guardrails.EnableContentFilter;
        _blockedKeywords = guardrails.BlockedOutputKeywords;
    }

    public Task<GuardrailResult> ValidateAsync(string output, CancellationToken ct = default)
    {
        if (!_enabled || _blockedKeywords.Count == 0)
        {
            return Task.FromResult(GuardrailResult.Allowed());
        }

        foreach (var keyword in _blockedKeywords)
        {
            if (output.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(GuardrailResult.Rejected(
                    nameof(ContentFilterGuardrail),
                    "Response contains blocked content"));
            }
        }

        return Task.FromResult(GuardrailResult.Allowed());
    }

    public Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default)
        => IGuardrailProvider.BridgeValidateAsync(request, ValidateAsync, GuardrailReasonCodes.BlockedContent, "Blocked content detected", ct);
}
