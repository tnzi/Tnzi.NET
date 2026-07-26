namespace Tnzi.AI.Guardrails;

/// <summary>
/// 统一 Guardrail 提供者协议 - 可插拔的输入/输出/工具级评估。
/// </summary>
/// <remarks>
/// 与 IInputGuardrail/IOutputGuardrail 不同，IGuardrailProvider 接收结构化请求（含工具信息），
/// 返回结构化决策（含多原因、策略 ID、元数据），适用于工具级和更复杂的防护场景。
/// </remarks>
public interface IGuardrailProvider
{
    /// <summary>Provider 名称，用于日志和审计</summary>
    string Name { get; }

    /// <summary>
    /// 评估请求是否允许通过
    /// </summary>
    /// <param name="request">评估请求（可包含工具调用信息或内容文本）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>评估决策</returns>
    Task<GuardrailDecision> EvaluateAsync(GuardrailRequest request, CancellationToken ct = default);

    /// <summary>
    /// 桥接 IInputGuardrail/IOutputGuardrail 的 ValidateAsync 到 IGuardrailProvider 的 EvaluateAsync。
    /// 当 request.Content 为 null 时直接返回 Allow()（工具级请求无文本内容）。
    /// </summary>
    static async Task<GuardrailDecision> BridgeValidateAsync(
        GuardrailRequest request,
        Func<string, CancellationToken, Task<GuardrailResult>> validateAsync,
        string reasonCode,
        string fallbackMessage,
        CancellationToken ct = default)
    {
        if (request.Content == null) return GuardrailDecision.Allow();

        var result = await validateAsync(request.Content, ct);
        return result.IsAllowed
            ? GuardrailDecision.Allow()
            : GuardrailDecision.Deny(reasonCode, result.Reason ?? fallbackMessage);
    }
}

/// <summary>
/// Guardrail 评估请求 - 统一的输入/输出/工具评估请求
/// </summary>
public record GuardrailRequest
{
    /// <summary>工具名称（工具级评估时填充）</summary>
    public string? ToolName { get; init; }

    /// <summary>工具输入参数（工具级评估时填充）</summary>
    public Dictionary<string, object>? ToolInput { get; init; }

    /// <summary>文本内容（输入/输出评估时填充）</summary>
    public string? Content { get; init; }

    /// <summary>Agent ID</summary>
    public Guid? AgentId { get; init; }

    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>是否为子 Agent 请求</summary>
    public bool IsSubAgent { get; init; }

    /// <summary>请求时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Guardrail 评估决策 - 包含是否允许、拒绝原因列表、策略 ID 和元数据
/// </summary>
public record GuardrailDecision(
    bool IsAllowed,
    IReadOnlyList<GuardrailReason> Reasons,
    string? PolicyId = null,
    IReadOnlyDictionary<string, object>? Metadata = null)
{
    /// <summary>创建允许决策</summary>
    public static GuardrailDecision Allow() => new(true, []);

    /// <summary>创建拒绝决策</summary>
    public static GuardrailDecision Deny(string code, string message, string? policyId = null, IReadOnlyDictionary<string, object>? metadata = null)
        => new(false, [new GuardrailReason(code, message)], policyId, metadata);
}

/// <summary>
/// Guardrail 决策原因
/// </summary>
/// <param name="Code">原因代码（如 "tool_denied", "pii_detected"）</param>
/// <param name="Message">人类可读的原因描述</param>
public record GuardrailReason(string Code, string Message = "");
