namespace Tnzi.AI.Metadata;

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
    /// The agent is a host-level shared definition (synced from YAML): tenants can resolve and list it but not modify or delete it.
    /// </summary>
    public const string AgentSharedDefinitionReadOnly = "AI_AGENT_SHARED_DEFINITION_READ_ONLY";

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
    /// The AI cost budget (<c>AI:Budget</c>) is exhausted.
    /// </summary>
    public const string QuotaExceeded = "AI_QUOTA_EXCEEDED";

    /// <summary>
    /// The user has reached a usage limit (the 5-hour window or the weekly window). The error details carry the window
    /// and when it resets (<c>UsageLimitDenialDto</c>). HTTP 429.
    /// </summary>
    public const string UsageLimitReached = "AI_USAGE_LIMIT_REACHED";

    /// <summary>
    /// The usage limit could not be checked or recorded (the meter itself failed). This is not a limit; the turn is not run.
    /// </summary>
    public const string UsageLimitCheckFailed = "AI_USAGE_LIMIT_CHECK_FAILED";

    /// <summary>
    /// A usage policy could not be saved: an unknown model, a default model outside the allowed list, or a limited
    /// setting without an amount. HTTP 400.
    /// </summary>
    public const string UsagePolicyInvalid = "AI_USAGE_POLICY_INVALID";

    /// <summary>
    /// The requested model is not one this user may use with this agent. HTTP 403.
    /// </summary>
    public const string ModelNotAllowed = "AI_MODEL_NOT_ALLOWED";

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
    /// Guardrail tripwire triggered - immediate abort of all parallel guardrails.
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
    /// A feedback tag is not one of <see cref="Dtos.FeedbackTags.All"/>.
    /// </summary>
    public const string FeedbackTagInvalid = "AI_FEEDBACK_TAG_INVALID";

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

    /// <summary>
    /// A request without an AgentId asked for a tool group that is not in AI:AdHocTools:AllowedGroups.
    /// </summary>
    public const string ToolGroupNotAllowed = "AI_TOOL_GROUP_NOT_ALLOWED";

    /// <summary>
    /// A request without an AgentId asked for a tool name that is not in AI:AdHocTools:AllowedTools.
    /// </summary>
    public const string ToolNameNotAllowed = "AI_TOOL_NAME_NOT_ALLOWED";

    /// <summary>
    /// Provider entity not found.
    /// </summary>
    public const string ProviderNotFound = "AI_PROVIDER_NOT_FOUND";

    /// <summary>
    /// Provider with same name already exists.
    /// </summary>
    public const string ProviderAlreadyExists = "AI_PROVIDER_ALREADY_EXISTS";

    /// <summary>
    /// Provider operation failed.
    /// </summary>
    public const string ProviderOperationFailed = "AI_PROVIDER_OPERATION_FAILED";

    /// <summary>
    /// MCP server registration entity not found.
    /// </summary>
    public const string McpServerRegistrationNotFound = "AI_MCP_SERVER_REGISTRATION_NOT_FOUND";

    /// <summary>
    /// MCP server registration with same name already exists.
    /// </summary>
    public const string McpServerRegistrationAlreadyExists = "AI_MCP_SERVER_REGISTRATION_ALREADY_EXISTS";

    /// <summary>
    /// MCP server registration operation failed.
    /// </summary>
    public const string McpServerRegistrationOperationFailed = "AI_MCP_SERVER_REGISTRATION_OPERATION_FAILED";

    /// <summary>
    /// Agent memory entry not found.
    /// </summary>
    public const string MemoryNotFound = "AI_MEMORY_NOT_FOUND";

    /// <summary>
    /// Persisted tool permission rule not found.
    /// </summary>
    public const string PermissionRuleNotFound = "AI_PERMISSION_RULE_NOT_FOUND";

    /// <summary>
    /// Sub-agent type definition not found.
    /// </summary>
    public const string SubAgentTypeNotFound = "AI_SUB_AGENT_TYPE_NOT_FOUND";

    /// <summary>
    /// A sub-agent type with the same name already exists in this tenant.
    /// </summary>
    public const string SubAgentTypeAlreadyExists = "AI_SUB_AGENT_TYPE_ALREADY_EXISTS";

    /// <summary>
    /// Sub-agent spawning is switched off (AI:SubAgent:Enabled = false).
    /// </summary>
    public const string SubAgentsDisabled = "AI_SUB_AGENTS_DISABLED";

    /// <summary>
    /// The root run already has AI:SubAgent:MaxConcurrentSubAgents descendants still running.
    /// </summary>
    public const string SubAgentLimitExceeded = "AI_SUB_AGENT_LIMIT_EXCEEDED";

    /// <summary>
    /// External CLI agent capability requested but the Tnzi.AI.Cli module is not loaded.
    /// </summary>
    public const string CliModuleNotLoaded = "AI_CLI_MODULE_NOT_LOADED";

    /// <summary>
    /// External CLI agent execution is disabled by configuration (AI:Cli:Enabled=false).
    /// </summary>
    public const string CliDisabled = "AI_CLI_DISABLED";

    /// <summary>
    /// External CLI runtime registration not found.
    /// </summary>
    public const string CliRuntimeNotFound = "AI_CLI_RUNTIME_NOT_FOUND";

    /// <summary>
    /// External CLI runtimes are host-level resources: a tenant-scoped caller may read them but not probe, change or delete them.
    /// </summary>
    public const string CliRuntimeHostManaged = "AI_CLI_RUNTIME_HOST_MANAGED";

    /// <summary>
    /// The CLI version registered for the runtime is outside the range this deployment supports for that provider.
    /// </summary>
    public const string CliRuntimeVersionUnsupported = "AI_CLI_RUNTIME_VERSION_UNSUPPORTED";

    /// <summary>
    /// The agent has no external CLI runtime binding.
    /// </summary>
    public const string CliBindingNotFound = "AI_CLI_BINDING_NOT_FOUND";

    /// <summary>
    /// External CLI run record not found.
    /// </summary>
    public const string CliRunNotFound = "AI_CLI_RUN_NOT_FOUND";

    /// <summary>
    /// External CLI run is not in the expected state for the requested operation.
    /// </summary>
    public const string CliRunInvalidState = "AI_CLI_RUN_INVALID_STATE";

    /// <summary>
    /// The registered <c>ICliAgentDispatcher</c> implementation does not provide the system (no user context) cancellation.
    /// </summary>
    public const string CliSystemCancelNotSupported = "AI_CLI_SYSTEM_CANCEL_NOT_SUPPORTED";

    /// <summary>
    /// The permission request of an external CLI run does not exist (or the caller cannot see the run).
    /// </summary>
    public const string CliPermissionRequestNotFound = "AI_CLI_PERMISSION_REQUEST_NOT_FOUND";

    /// <summary>
    /// The permission request of an external CLI run has already been answered, timed out or ended with its run.
    /// </summary>
    public const string CliPermissionRequestClosed = "AI_CLI_PERMISSION_REQUEST_CLOSED";

    /// <summary>
    /// The answer names an option the permission request did not offer.
    /// </summary>
    public const string CliPermissionOptionInvalid = "AI_CLI_PERMISSION_OPTION_INVALID";

    /// <summary>
    /// The registered <c>ICliAgentDispatcher</c> implementation does not relay permission requests.
    /// </summary>
    public const string CliPermissionNotSupported = "AI_CLI_PERMISSION_NOT_SUPPORTED";

    /// <summary>
    /// The requested external agent provider is unknown or not enabled in this deployment.
    /// </summary>
    public const string CliProviderNotAvailable = "AI_CLI_PROVIDER_NOT_AVAILABLE";

    /// <summary>
    /// No protocol adapter is implemented for the provider's protocol family.
    /// </summary>
    public const string CliProtocolNotImplemented = "AI_CLI_PROTOCOL_NOT_IMPLEMENTED";

    /// <summary>
    /// The USD cost budget is exhausted, so no external CLI run may be started.
    /// </summary>
    /// <remarks>
    /// External execution deliberately bypasses the middleware pipeline, and with it
    /// <c>QuotaMiddleware</c> — the budget gate therefore has to be applied explicitly on this
    /// path. Without it the external domain would be an unmetered way around the budget, and it
    /// is the more expensive of the two: an external run lasts minutes to hours.
    /// </remarks>
    public const string CliBudgetExceeded = "AI_CLI_BUDGET_EXCEEDED";

    /// <summary>
    /// An attachment is larger than <c>AI:Chat:Attachments:MaxBytes</c>.
    /// </summary>
    public const string AttachmentTooLarge = "AI_ATTACHMENT_TOO_LARGE";

    /// <summary>
    /// A message carries more attachments than <c>AI:Chat:Attachments:MaxCount</c>.
    /// </summary>
    public const string TooManyAttachments = "AI_TOO_MANY_ATTACHMENTS";

    /// <summary>
    /// Attachment not found, or not visible to the caller.
    /// </summary>
    public const string AttachmentNotFound = "AI_ATTACHMENT_NOT_FOUND";

    /// <summary>
    /// The chat agent policy does not let this user chat with the requested agent (or without one).
    /// </summary>
    public const string ChatAgentNotAllowed = "AI_CHAT_AGENT_NOT_ALLOWED";

    /// <summary>
    /// A consumer's turn handler refused a turn for an agent on an external CLI runtime without giving its own code.
    /// </summary>
    public const string CliTurnRefused = "AI_CLI_TURN_REFUSED";

    /// <summary>
    /// A consumer's turn handler threw while a turn for an agent on an external CLI runtime was being prepared;
    /// the turn was not queued.
    /// </summary>
    public const string CliTurnHandlerFailed = "AI_CLI_TURN_HANDLER_FAILED";

    /// <summary>
    /// The thread has a run in progress, so its messages cannot be edited or regenerated until it ends or is cancelled.
    /// </summary>
    public const string ThreadRunInProgress = "AI_THREAD_RUN_IN_PROGRESS";

    /// <summary>
    /// The message to edit or regenerate from is not a user message of the thread.
    /// </summary>
    public const string MessageNotEditable = "AI_MESSAGE_NOT_EDITABLE";

    /// <summary>
    /// The thread has no run with this id, or the caller cannot see it.
    /// </summary>
    public const string ChatRunNotFound = "AI_CHAT_RUN_NOT_FOUND";

    /// <summary>
    /// The external CLI's account reached its usage limit (a subscription's session or weekly limit, or a spend cap).
    /// The stream's error event and the run carry <c>usageLimitResetsAt</c> when the CLI reported when it resets.
    /// </summary>
    public const string CliUsageLimit = "AI_CLI_USAGE_LIMIT";

    /// <summary>
    /// A chat request's per-turn metadata is not valid (too many keys, a malformed key, or too large).
    /// </summary>
    public const string InvalidTurnMetadata = "AI_INVALID_TURN_METADATA";
}
