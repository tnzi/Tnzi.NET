namespace Tnzi.AI;

/// <summary>
/// AI module error code constants.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Agent not found.
    /// </summary>
    public const string AgentNotFound = "AI_AGENT_NOT_FOUND";

    /// <summary>
    /// Agent is disabled.
    /// </summary>
    public const string AgentDisabled = "AI_AGENT_DISABLED";

    /// <summary>
    /// Workflow not found.
    /// </summary>
    public const string WorkflowNotFound = "AI_WORKFLOW_NOT_FOUND";

    /// <summary>
    /// Workflow is disabled.
    /// </summary>
    public const string WorkflowDisabled = "AI_WORKFLOW_DISABLED";

    /// <summary>
    /// Agent thread not found.
    /// </summary>
    public const string ThreadNotFound = "AI_THREAD_NOT_FOUND";

    /// <summary>
    /// ThreadId requires AgentId for conversation thread association.
    /// </summary>
    public const string ThreadRequiresAgent = "AI_THREAD_REQUIRES_AGENT";

    /// <summary>
    /// Quota check failed.
    /// </summary>
    public const string QuotaCheckFailed = "AI_QUOTA_CHECK_FAILED";

    /// <summary>
    /// Quota exceeded (daily or monthly limit).
    /// </summary>
    public const string QuotaExceeded = "AI_QUOTA_EXCEEDED";

    /// <summary>
    /// User quota not found.
    /// </summary>
    public const string QuotaNotFound = "AI_QUOTA_NOT_FOUND";

    /// <summary>
    /// Quota update failed.
    /// </summary>
    public const string QuotaUpdateFailed = "AI_QUOTA_UPDATE_FAILED";

    /// <summary>
    /// Quota get failed.
    /// </summary>
    public const string QuotaGetFailed = "AI_QUOTA_GET_FAILED";

    /// <summary>
    /// Quota set failed.
    /// </summary>
    public const string QuotaSetFailed = "AI_QUOTA_SET_FAILED";

    /// <summary>
    /// Quota reset failed.
    /// </summary>
    public const string QuotaResetFailed = "AI_QUOTA_RESET_FAILED";

    /// <summary>
    /// Quota concurrency conflict after max retries.
    /// </summary>
    public const string QuotaConcurrencyConflict = "AI_QUOTA_CONCURRENCY_CONFLICT";

    /// <summary>
    /// Agent run failed.
    /// </summary>
    public const string AgentRunFailed = "AI_AGENT_RUN_FAILED";

    /// <summary>
    /// Chat request failed.
    /// </summary>
    public const string ChatFailed = "AI_CHAT_FAILED";

    /// <summary>
    /// Workflow execution failed.
    /// </summary>
    public const string WorkflowFailed = "AI_WORKFLOW_FAILED";

    /// <summary>
    /// Generic AI module error.
    /// </summary>
    public const string InternalError = "AI_ERROR";

    /// <summary>
    /// Invalid content (missing or malformed multimodal content).
    /// </summary>
    public const string InvalidContent = "AI_INVALID_CONTENT";

    /// <summary>
    /// Streaming operation failed.
    /// </summary>
    public const string StreamingFailed = "AI_STREAMING_FAILED";

    /// <summary>
    /// Unsupported media type in multimodal content.
    /// </summary>
    public const string UnsupportedMediaType = "AI_UNSUPPORTED_MEDIA_TYPE";

    /// <summary>
    /// Embedding provider not found or not configured.
    /// </summary>
    public const string EmbeddingProviderNotFound = "AI_EMBEDDING_PROVIDER_NOT_FOUND";

    /// <summary>
    /// Embedding generation failed.
    /// </summary>
    public const string EmbeddingFailed = "AI_EMBEDDING_FAILED";

    /// <summary>
    /// Structured output generation failed after max retries.
    /// </summary>
    public const string StructuredOutputFailed = "AI_STRUCTURED_OUTPUT_FAILED";

    /// <summary>
    /// MCP server connection failed.
    /// </summary>
    public const string McpConnectionFailed = "AI_MCP_CONNECTION_FAILED";

    /// <summary>
    /// Failed to load tools from MCP server.
    /// </summary>
    public const string McpToolLoadFailed = "AI_MCP_TOOL_LOAD_FAILED";

    /// <summary>
    /// Workflow parallel execution had one or more agent failures (partial success).
    /// </summary>
    public const string WorkflowPartialFailure = "AI_WORKFLOW_PARTIAL_FAILURE";

    /// <summary>
    /// Guardrail tripwire triggered — immediate abort of all parallel guardrails.
    /// </summary>
    public const string GuardrailTripwire = "AI_GUARDRAIL_TRIPWIRE";

    /// <summary>
    /// Agent version not found.
    /// </summary>
    public const string AgentVersionNotFound = "AI_AGENT_VERSION_NOT_FOUND";
}
