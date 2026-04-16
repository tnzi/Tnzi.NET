import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Phase 5 Task 5.6 — WorkflowRunViewer page config.
 *
 * Read-only viewer over workflow execution summaries. Shape derives from
 * @tnzi/core/services/ai WorkflowExecutionSummaryDto (returned by
 * useAdminWorkflowApi.getExecutions, surfaced via bridge.workflowRuns.fetch).
 *
 * Plan deviations vs the task sketch:
 *   - The plan listed columns workflowName / runId / startedAt / completedAt /
 *     durationMs / triggeredBy. The real DTO has none of those; it has
 *     workflowDefinitionId, executionId (the run id), creationTime,
 *     completedTime, completedStepCount, awaitingApprovalCount, and updatedTime.
 *     Config follows backend reality (canonical-compact rule from Task 5.2).
 *   - Read-only mode: no create/update/delete. The page uses TCrudPage with
 *     `:show-create="false"` and supplies stub mutate handlers that reject —
 *     useCrudPage requires the trio in its options. Bridge already rejects
 *     workflowRuns.tail() — we expose no streaming UI here.
 *   - Filters (workflowDefinitionId, status) are wired via query.filters and
 *     the bridge passes them straight through to the backend execution query.
 */
export const workflowRunColumns: ColumnDef[] = [
  { key: 'workflowDefinitionId', title: 'Workflow' },
  { key: 'executionId', title: 'Run ID' },
  { key: 'status', title: 'Status' },
  { key: 'completedStepCount', title: 'Completed Steps' },
  { key: 'awaitingApprovalCount', title: 'Awaiting Approval', visible: false },
  { key: 'creationTime', title: 'Started' },
  { key: 'completedTime', title: 'Completed' },
  { key: 'updatedTime', title: 'Last Updated', visible: false },
]

export const workflowRunStatusOptions: Array<{ label: string; value: number }> = [
  { label: 'Running', value: 0 },
  { label: 'Completed', value: 1 },
  { label: 'Failed', value: 2 },
  { label: 'Paused', value: 3 },
  { label: 'Awaiting Approval', value: 4 },
]

/** Filter form (rendered in #toolbarRight as a small inline panel). */
export const workflowRunFilterSchema: FormSchemaItem[] = [
  { key: 'workflowDefinitionId', label: 'Workflow ID', type: 'text', placeholder: 'Filter by workflow id' },
  { key: 'status', label: 'Status', type: 'select', options: workflowRunStatusOptions },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.workflowRun.*):
 *
 * Page meta:
 *   modules.ai.workflowRun.pageTitle
 *
 * Columns:
 *   modules.ai.workflowRun.columns.workflowDefinitionId
 *   modules.ai.workflowRun.columns.executionId
 *   modules.ai.workflowRun.columns.status
 *   modules.ai.workflowRun.columns.completedStepCount
 *   modules.ai.workflowRun.columns.awaitingApprovalCount
 *   modules.ai.workflowRun.columns.creationTime
 *   modules.ai.workflowRun.columns.completedTime
 *   modules.ai.workflowRun.columns.updatedTime
 *
 * Filter fields:
 *   modules.ai.workflowRun.filters.workflowDefinitionId
 *   modules.ai.workflowRun.filters.status
 */
