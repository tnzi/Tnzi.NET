namespace Tnzi.AI.Guardrails;

/// <summary>
/// 输入 Guardrail 接口 - 在 AI 执行前验证/过滤用户输入
/// </summary>
public interface IInputGuardrail
{
    /// <summary>
    /// 验证输入内容
    /// </summary>
    /// <param name="input">用户输入文本</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>验证结果（通过/拒绝/修改）</returns>
    Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default);
}

/// <summary>
/// 输出 Guardrail 接口 - 在返回 AI 响应前验证/过滤输出内容
/// </summary>
public interface IOutputGuardrail
{
    /// <summary>
    /// 验证输出内容
    /// </summary>
    /// <param name="output">AI 响应文本</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>验证结果（通过/拒绝/修改）</returns>
    Task<GuardrailResult> ValidateAsync(string output, CancellationToken ct = default);
}

/// <summary>
/// Guardrail 验证结果
/// </summary>
public class GuardrailResult
{
    /// <summary>是否通过验证</summary>
    public bool IsAllowed { get; set; }

    /// <summary>拒绝/修改原因</summary>
    public string? Reason { get; set; }

    /// <summary>修改后的内容（null 表示使用原始内容）</summary>
    public string? ModifiedContent { get; set; }

    /// <summary>触发的 Guardrail 名称</summary>
    public string? GuardrailName { get; set; }

    /// <summary>创建通过结果</summary>
    public static GuardrailResult Allowed() => new() { IsAllowed = true };

    /// <summary>创建拒绝结果</summary>
    public static GuardrailResult Rejected(string guardrailName, string reason) =>
        new() { IsAllowed = false, GuardrailName = guardrailName, Reason = reason };

    /// <summary>创建修改结果（内容被清洗后放行）</summary>
    public static GuardrailResult Modified(string guardrailName, string modifiedContent, string reason) =>
        new() { IsAllowed = true, GuardrailName = guardrailName, ModifiedContent = modifiedContent, Reason = reason };
}
