/**
 * AI Module Metadata - Enums and label helpers
 *
 * Re-exports enums from types.ts for backward compatibility.
 * New code should import enums directly from './types'.
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

// ============================================
// Label Helpers
// ============================================

import {
  AgentExecutionMode,
  AgentRunStatus,
  AgentRunNodeStatus,
  WorkflowExecutionMode,
  WorkflowExecutionStatus,
  EvaluationRunStatus,
  QuotaWarningLevel,
  SkillScope,
  SkillSource,
} from './types';

/** Get agent execution mode label */
export function getAgentExecutionModeLabel(mode: AgentExecutionMode): string {
  switch (mode) {
    case AgentExecutionMode.Single: return 'Single';
    case AgentExecutionMode.Handoff: return 'Handoff';
    case AgentExecutionMode.AgentAsTools: return 'Agent as Tools';
    case AgentExecutionMode.Router: return 'Router';
    case AgentExecutionMode.ExternalCli: return 'External CLI';
    default: return 'Unknown';
  }
}

/** Get agent run status label */
export function getAgentRunStatusLabel(status: AgentRunStatus): string {
  switch (status) {
    case AgentRunStatus.Pending: return 'Pending';
    case AgentRunStatus.Running: return 'Running';
    case AgentRunStatus.AwaitingApproval: return 'Awaiting Approval';
    case AgentRunStatus.Completed: return 'Completed';
    case AgentRunStatus.Failed: return 'Failed';
    case AgentRunStatus.Cancelled: return 'Cancelled';
    default: return 'Unknown';
  }
}

/** Get agent run node status label */
export function getAgentRunNodeStatusLabel(status: AgentRunNodeStatus): string {
  switch (status) {
    case AgentRunNodeStatus.Pending: return 'Pending';
    case AgentRunNodeStatus.Running: return 'Running';
    case AgentRunNodeStatus.Completed: return 'Completed';
    case AgentRunNodeStatus.Failed: return 'Failed';
    case AgentRunNodeStatus.Skipped: return 'Skipped';
    case AgentRunNodeStatus.AwaitingApproval: return 'Awaiting Approval';
    case AgentRunNodeStatus.Approved: return 'Approved';
    case AgentRunNodeStatus.Rejected: return 'Rejected';
    default: return 'Unknown';
  }
}

/** Get workflow execution mode label */
export function getWorkflowExecutionModeLabel(mode: WorkflowExecutionMode): string {
  switch (mode) {
    case WorkflowExecutionMode.Sequential: return 'Sequential';
    case WorkflowExecutionMode.Parallel: return 'Parallel';
    case WorkflowExecutionMode.Dag: return 'DAG';
    default: return 'Unknown';
  }
}

/** Get workflow execution status label */
export function getWorkflowExecutionStatusLabel(status: WorkflowExecutionStatus): string {
  switch (status) {
    case WorkflowExecutionStatus.Running: return 'Running';
    case WorkflowExecutionStatus.Completed: return 'Completed';
    case WorkflowExecutionStatus.Failed: return 'Failed';
    case WorkflowExecutionStatus.Paused: return 'Paused';
    case WorkflowExecutionStatus.AwaitingApproval: return 'Awaiting Approval';
    default: return 'Unknown';
  }
}

/** Get evaluation run status label */
export function getEvaluationRunStatusLabel(status: EvaluationRunStatus): string {
  switch (status) {
    case EvaluationRunStatus.Running: return 'Running';
    case EvaluationRunStatus.Completed: return 'Completed';
    case EvaluationRunStatus.Failed: return 'Failed';
    default: return 'Unknown';
  }
}

/** Get quota warning level label */
export function getQuotaWarningLevelLabel(level: QuotaWarningLevel): string {
  switch (level) {
    case QuotaWarningLevel.None: return 'Normal';
    case QuotaWarningLevel.Warning: return 'Warning';
    case QuotaWarningLevel.Critical: return 'Critical';
    default: return 'Unknown';
  }
}

/** Get skill scope label */
export function getSkillScopeLabel(scope: SkillScope): string {
  switch (scope) {
    case SkillScope.System: return 'System';
    case SkillScope.Tenant: return 'Tenant';
    case SkillScope.User: return 'User';
    default: return 'Unknown';
  }
}

/** Get skill source label */
export function getSkillSourceLabel(source: SkillSource): string {
  switch (source) {
    case SkillSource.FileSystem: return 'File System';
    case SkillSource.Database: return 'Database';
    default: return 'Unknown';
  }
}
