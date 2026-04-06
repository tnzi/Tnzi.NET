namespace Tnzi.AI.Engine;

/// <summary>
/// 多级错误恢复策略 — 借鉴 Claude Code query.ts 的分级恢复机制。
/// 根据异常类型选择恢复动作：prompt-too-long → reactive compact, max_output_tokens → 升级/resume。
/// </summary>
public class ErrorRecoveryStrategy
{
    /// <summary>
    /// 错误恢复动作
    /// </summary>
    public enum RecoveryAction
    {
        /// <summary>无法恢复，放弃</summary>
        Abort,
        /// <summary>触发 reactive compact（压缩上下文后重试）</summary>
        ReactiveCompact,
        /// <summary>升级 MaxOutputTokens 后重试</summary>
        UpgradeMaxOutputTokens,
        /// <summary>注入 "Resume directly from where you left off" 提示后重试</summary>
        InjectResumeHint
    }

    /// <summary>
    /// 恢复结果
    /// </summary>
    public record RecoveryResult(RecoveryAction Action, string? Message = null, int? NewMaxOutputTokens = null);

    private int _promptTooLongAttempts;
    private int _maxOutputAttempts;

    /// <summary>
    /// 分析异常并决定恢复动作
    /// </summary>
    public RecoveryResult Analyze(Exception ex)
    {
        // prompt-too-long: L1 → reactive compact, L2 → abort
        if (IsPromptTooLongError(ex))
        {
            _promptTooLongAttempts++;
            return _promptTooLongAttempts <= 1
                ? new RecoveryResult(RecoveryAction.ReactiveCompact, "Context too long, triggering reactive compact")
                : new RecoveryResult(RecoveryAction.Abort, "Context still too long after compact, aborting");
        }

        // max_output_tokens: L1 → upgrade to 64K, L2 → inject resume, L3 → abort
        if (IsMaxOutputTokensError(ex))
        {
            _maxOutputAttempts++;
            return _maxOutputAttempts switch
            {
                1 => new RecoveryResult(RecoveryAction.UpgradeMaxOutputTokens,
                    "Output truncated, upgrading max_output_tokens", NewMaxOutputTokens: 64_000),
                2 => new RecoveryResult(RecoveryAction.InjectResumeHint,
                    "Output still truncated, injecting resume hint"),
                _ => new RecoveryResult(RecoveryAction.Abort,
                    "Output truncated after multiple retries, aborting")
            };
        }

        return new RecoveryResult(RecoveryAction.Abort);
    }

    /// <summary>
    /// 检测 prompt-too-long 错误（上下文超过模型限制）
    /// </summary>
    public static bool IsPromptTooLongError(Exception ex)
    {
        var message = ex.Message;
        if (string.IsNullOrEmpty(message)) return false;

        // Anthropic: "prompt is too long"
        // OpenAI: "maximum context length" or "token limit"
        return message.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase)
            || message.Contains("maximum context length", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("token limit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检测 max_output_tokens 错误（输出被截断）
    /// </summary>
    public static bool IsMaxOutputTokensError(Exception ex)
    {
        var message = ex.Message;
        if (string.IsNullOrEmpty(message)) return false;

        return message.Contains("max_tokens", StringComparison.OrdinalIgnoreCase)
            || message.Contains("max_output_tokens", StringComparison.OrdinalIgnoreCase)
            || message.Contains("output truncated", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resume 提示消息（注入到对话末尾）
    /// </summary>
    public const string ResumeHint = "Resume directly from where you left off. Do not repeat any content that was already generated.";
}
