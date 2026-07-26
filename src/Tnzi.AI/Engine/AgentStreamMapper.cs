namespace Tnzi.AI.Engine;

/// <summary>
/// 共享流式事件与失败原因映射工具 - 消除 AgentService / ChatService / AgentRunService 中的重复逻辑。
/// </summary>
public static class AgentStreamMapper
{
    /// <summary>
    /// 从 <see cref="AgentStreamChunk"/> 构造 <see cref="StreamEvent"/>。
    /// 当 chunk 不含任何有效载荷时返回 null。
    /// </summary>
    /// <param name="chunk">流式块</param>
    /// <param name="threadId">当前线程 ID</param>
    /// <param name="defaultErrorCode">非 guardrail 错误时使用的错误码</param>
    public static StreamEvent? BuildStreamEvent(AgentStreamChunk chunk, Guid? threadId, string defaultErrorCode)
    {
        var hasPayload =
            chunk.Error != null ||
            chunk.AgentName != null ||
            chunk.ReasoningText != null ||
            chunk.Text != null ||
            chunk.ToolCalls != null ||
            chunk.IsToolCall ||
            chunk.ToolCallNames != null ||
            chunk.EventType != null ||
            chunk.EventData != null ||
            chunk.Suggestions != null ||
            chunk.Todos != null ||
            chunk.Artifacts != null;

        if (!hasPayload)
            return null;

        return new StreamEvent
        {
            Delta = chunk.Text,
            FinishReason = chunk.FinishReason,
            Model = chunk.Model,
            ThreadId = threadId,
            IsError = chunk.Error != null,
            ErrorMessage = chunk.Error,
            ErrorCode = chunk.Error != null && chunk.FinishReason == FinishReasons.GuardrailRejected
                ? ErrorCodes.GuardrailRejected
                : chunk.Error != null
                    ? defaultErrorCode
                    : null,
            IsToolCall = chunk.IsToolCall,
            ToolCallNames = chunk.ToolCallNames,
            ReasoningDelta = chunk.ReasoningText,
            AgentName = chunk.AgentName,
            ToolCalls = chunk.ToolCalls,
            EventType = chunk.EventType,
            EventData = chunk.EventData,
            Suggestions = chunk.Suggestions,
            Todos = chunk.Todos,
            Artifacts = chunk.Artifacts
        };
    }

    /// <summary>
    /// 根据 <see cref="AgentRunResult.FinishReason"/> 映射失败的 HTTP 状态码与错误码。
    /// </summary>
    /// <param name="result">运行结果</param>
    /// <param name="failureErrorCode">
    /// 用于 Rejected / MaxHandoffs / Error / Failed 情形的业务错误码。
    /// 调用方传入适合自身上下文的常量（如 <see cref="ErrorCodes.AgentRunFailed"/> 或
    /// <see cref="ErrorCodes.ChatFailed"/>）。
    /// </param>
    /// <param name="statusCode">输出 HTTP 状态码；映射不到时为 0</param>
    /// <param name="errorCode">输出业务错误码；映射不到时为空字符串</param>
    /// <returns>true 表示结果是一种失败情形，false 表示不是已知失败（调用方自行处理）</returns>
    public static bool TryMapFailure(AgentRunResult result, string failureErrorCode, out int statusCode, out string errorCode)
    {
        switch (result.FinishReason)
        {
            case FinishReasons.QuotaExceeded:
                statusCode = 429;
                errorCode = ErrorCodes.QuotaExceeded;
                return true;
            case FinishReasons.GuardrailRejected:
                statusCode = 400;
                errorCode = ErrorCodes.GuardrailRejected;
                return true;
            case FinishReasons.Rejected:
                statusCode = 400;
                errorCode = failureErrorCode;
                return true;
            case FinishReasons.MaxHandoffs:
            case FinishReasons.Error:
            case FinishReasons.Failed:
                statusCode = 500;
                errorCode = failureErrorCode;
                return true;
            default:
                statusCode = 0;
                errorCode = string.Empty;
                return false;
        }
    }
}
