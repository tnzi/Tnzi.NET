namespace Tnzi.AI.Guardrails;

/// <summary>
/// 输出内容过滤 Guardrail - 检测 AI 响应中的敏感内容
/// </summary>
/// <remarks>
/// 支持通过配置自定义关键词列表。默认包含基本的安全检查。
/// </remarks>
public class ContentFilterGuardrail : IOutputGuardrail, IGuardrailProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public string Name => nameof(ContentFilterGuardrail);

    public ContentFilterGuardrail(IOptionsMonitor<AIOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<GuardrailResult> ValidateAsync(string output, CancellationToken ct = default)
    {
        var guardrails = _options.CurrentValue.Guardrails;
        if (!guardrails.EnableContentFilter || guardrails.BlockedOutputKeywords.Count == 0)
        {
            return Task.FromResult(GuardrailResult.Allowed());
        }

        foreach (var keyword in guardrails.BlockedOutputKeywords)
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
