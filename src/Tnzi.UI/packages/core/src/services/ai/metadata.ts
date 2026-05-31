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
  QuotaWarningLevel,
  SkillScope,
  SkillSource,
  ReasoningEffort,
} from './types';
