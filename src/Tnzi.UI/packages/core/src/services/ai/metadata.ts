/**
 * AI Module Metadata - Enum re-exports
 *
 * Re-exports enums from types.ts for backward compatibility.
 * New code should import enums directly from './types'.
 *
 * The per-enum `getXxxLabel` helpers were removed: they hard-coded English
 * labels, had zero consumers (admin AI pages localize via i18n keys +
 * `TStatusBadge` labelKey instead), and shipped ~100 dead lines in dist.
 */

export {
  AgentExecutionMode,
  AgentRunStatus,
  AgentRunNodeStatus,
  WorkflowExecutionMode,
  WorkflowExecutionStatus,
  EvaluationRunStatus,
  UsageGranularity,
  UsageLimitWindow,
  UsageLimitMode,
  SkillScope,
  SkillSource,
  ReasoningEffort,
} from './types';

/**
 * Error codes of the chat endpoints a client is likely to branch on (backend `ErrorCodes`).
 */
export const AiChatErrorCodes = {
  /** The external CLI's account reached its usage limit; see `usageLimitResetsAt`. */
  CliUsageLimit: 'AI_CLI_USAGE_LIMIT',
  /**
   * The user reached a usage limit (5-hour or weekly window). The error details are a `UsageLimitDenialDto`
   * (on a stream: `usageLimitWindow` / `usageLimitResetsAt`); nothing was written for the refused turn.
   */
  UsageLimitReached: 'AI_USAGE_LIMIT_REACHED',
  /** The requested model is not one this user may use with this agent. */
  ModelNotAllowed: 'AI_MODEL_NOT_ALLOWED',
  /** The thread has a reply in progress: stop it before editing or regenerating. */
  ThreadRunInProgress: 'AI_THREAD_RUN_IN_PROGRESS',
  /** The message to edit or regenerate from is not a user message of the thread. */
  MessageNotEditable: 'AI_MESSAGE_NOT_EDITABLE',
  /** The agent is not available for chat to this user. */
  ChatAgentNotAllowed: 'AI_CHAT_AGENT_NOT_ALLOWED',
  /** A per-turn handler refused the turn. */
  CliTurnRefused: 'AI_CLI_TURN_REFUSED',
  /** The per-turn metadata is not valid. */
  InvalidTurnMetadata: 'AI_INVALID_TURN_METADATA',
  /** An attachment is larger than allowed. */
  AttachmentTooLarge: 'AI_ATTACHMENT_TOO_LARGE',
  /** More attachments than allowed. */
  TooManyAttachments: 'AI_TOO_MANY_ATTACHMENTS',
  /** An attachment type the agent cannot receive. */
  UnsupportedMediaType: 'AI_UNSUPPORTED_MEDIA_TYPE',
} as const;
