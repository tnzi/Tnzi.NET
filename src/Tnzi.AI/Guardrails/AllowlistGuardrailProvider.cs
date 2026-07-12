namespace Tnzi.AI.Guardrails;

/// <summary>
/// 工具白名单/黑名单 Guardrail Provider — 基于配置允许或拒绝特定工具调用
/// </summary>
/// <remarks>
/// 评估逻辑优先级：
/// 1. 黑名单检查（DeniedTools）— 命中即拒绝
/// 2. 白名单检查（AllowedTools）— 非空时，未命中即拒绝
/// 3. 无工具名称的请求直接放行
/// </remarks>
public class AllowlistGuardrailProvider : IGuardrailProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public string Name => nameof(AllowlistGuardrailProvider);

    public AllowlistGuardrailProvider(IOptionsMonitor<AIOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default)
    {
        var options = _options.CurrentValue.Guardrails.Allowlist;

        // 非工具请求直接放行
        if (string.IsNullOrEmpty(request.ToolName))
        {
            return Task.FromResult(GuardrailDecision.Allow());
        }

        // 1. 黑名单检查（优先）
        if (options.DeniedTools.Count > 0 && MatchesTool(request.ToolName, options.DeniedTools, options.MatchExact))
        {
            return Task.FromResult(GuardrailDecision.Deny(
                GuardrailReasonCodes.ToolDenied,
                $"Tool '{request.ToolName}' is explicitly denied by policy",
                "allowlist"));
        }

        if (options.AllowedTools.Count > 0 && !MatchesTool(request.ToolName, options.AllowedTools, options.MatchExact))
        {
            return Task.FromResult(GuardrailDecision.Deny(
                GuardrailReasonCodes.ToolNotAllowed,
                $"Tool '{request.ToolName}' is not in the allowed tools list",
                "allowlist"));
        }

        return Task.FromResult(GuardrailDecision.Allow());
    }

    private static bool MatchesTool(string toolName, List<string> toolList, bool matchExact)
    {
        foreach (var tool in toolList)
        {
            if (matchExact)
            {
                if (string.Equals(toolName, tool, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                if (toolName.StartsWith(tool, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
