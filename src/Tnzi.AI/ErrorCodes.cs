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
    /// Guardrail rejected the input or output content.
    /// </summary>
    public const string GuardrailRejected = "AI_GUARDRAIL_REJECTED";

    /// <summary>
    /// Guardrail tripwire triggered — immediate abort of all parallel guardrails.
    /// </summary>
    public const string GuardrailTripwire = "AI_GUARDRAIL_TRIPWIRE";

    /// <summary>
    /// Agent version not found.
    /// </summary>
    public const string AgentVersionNotFound = "AI_AGENT_VERSION_NOT_FOUND";

    /// <summary>
    /// Workflow execution not found.
    /// </summary>
    public const string WorkflowExecutionNotFound = "AI_WORKFLOW_EXECUTION_NOT_FOUND";

    /// <summary>
    /// Workflow execution is not in the expected state for the requested operation.
    /// </summary>
    public const string WorkflowExecutionInvalidState = "AI_WORKFLOW_EXECUTION_INVALID_STATE";

    /// <summary>
    /// Workflow step not found in the awaiting approval list.
    /// </summary>
    public const string WorkflowStepNotAwaitingApproval = "AI_WORKFLOW_STEP_NOT_AWAITING_APPROVAL";

    /// <summary>
    /// Workflow definition version not found.
    /// </summary>
    public const string WorkflowVersionNotFound = "AI_WORKFLOW_VERSION_NOT_FOUND";

    /// <summary>
    /// Agent run node not found.
    /// </summary>
    public const string NodeNotFound = "AI_NODE_NOT_FOUND";

    /// <summary>
    /// Agent run not found.
    /// </summary>
    public const string RunNotFound = "AI_RUN_NOT_FOUND";

    /// <summary>
    /// Agent run is not in the expected state for the requested operation.
    /// </summary>
    public const string RunInvalidState = "AI_RUN_INVALID_STATE";

    /// <summary>
    /// Skill not found.
    /// </summary>
    public const string SkillNotFound = "AI_SKILL_NOT_FOUND";

    /// <summary>
    /// Skill is disabled.
    /// </summary>
    public const string SkillDisabled = "AI_SKILL_DISABLED";

    /// <summary>
    /// Skill slug is invalid (format or reserved).
    /// </summary>
    public const string SkillInvalidSlug = "AI_SKILL_INVALID_SLUG";

    /// <summary>
    /// Skill slug already exists in the same scope.
    /// </summary>
    public const string SkillSlugConflict = "AI_SKILL_SLUG_CONFLICT";

    /// <summary>
    /// Skill activation (template rendering) failed.
    /// </summary>
    public const string SkillActivationFailed = "AI_SKILL_ACTIVATION_FAILED";

    /// <summary>
    /// Skill operation requires authentication or higher privileges.
    /// </summary>
    public const string SkillUnauthorized = "AI_SKILL_UNAUTHORIZED";

    /// <summary>
    /// Skill category not found.
    /// </summary>
    public const string SkillCategoryNotFound = "AI_SKILL_CATEGORY_NOT_FOUND";

    /// <summary>
    /// Skill category slug already exists in the same tenant.
    /// </summary>
    public const string SkillCategorySlugConflict = "AI_SKILL_CATEGORY_SLUG_CONFLICT";

    /// <summary>
    /// Cannot delete category because it has child categories.
    /// </summary>
    public const string SkillCategoryHasChildren = "AI_SKILL_CATEGORY_HAS_CHILDREN";

    /// <summary>
    /// Cannot delete category because it has associated skills.
    /// </summary>
    public const string SkillCategoryHasSkills = "AI_SKILL_CATEGORY_HAS_SKILLS";

    /// <summary>
    /// Agent validation failed (one or more checks did not pass).
    /// </summary>
    public const string AgentValidationFailed = "AI_AGENT_VALIDATION_FAILED";

    /// <summary>
    /// Feedback can only be submitted on assistant messages.
    /// </summary>
    public const string FeedbackOnlyAssistant = "AI_FEEDBACK_ONLY_ASSISTANT";

    /// <summary>
    /// Message not found in the specified thread.
    /// </summary>
    public const string MessageNotFound = "AI_MESSAGE_NOT_FOUND";

    /// <summary>
    /// No feedback exists on this message to revoke.
    /// </summary>
    public const string FeedbackNotFound = "AI_FEEDBACK_NOT_FOUND";

    /// <summary>
    /// Evaluation run not found.
    /// </summary>
    public const string EvaluationNotFound = "AI_EVALUATION_NOT_FOUND";

    /// <summary>
    /// Batch embedding generation failed.
    /// </summary>
    public const string EmbeddingBatchFailed = "AI_EMBEDDING_BATCH_FAILED";

    /// <summary>
    /// MCP tool execution failed.
    /// </summary>
    public const string McpToolExecutionFailed = "AI_MCP_TOOL_EXECUTION_FAILED";

    /// <summary>
    /// Thread export operation failed.
    /// </summary>
    public const string ThreadExportFailed = "AI_THREAD_EXPORT_FAILED";

    /// <summary>
    /// Cost calculation failed.
    /// </summary>
    public const string CostCalculationFailed = "AI_COST_CALCULATION_FAILED";
}
