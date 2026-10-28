/**
 * Config for the external CLI runtime registry page.
 *
 * The form is intentionally tiny: everything else on a runtime (executable
 * path, CLI version, host info) is a **probe outcome**, not a setting - letting
 * an admin type a path here would just be overwritten by the next probe.
 */
import { h } from 'vue'
import { formatDateTime } from '@tnzi/core'
import type { FormSchemaItem } from '@tnzi/ui'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { CliRunStatus, CliRuntimeStatus, type CliRunDto } from '../../../services/bridges/cli-agent-bridge'

/** Runtime status → badge mapping (label keys are page-scoped i18n keys). */
export const statusBadgeMapping: Record<string, { label: string; type: string }> = {
  [CliRuntimeStatus.Online]: { label: 'status.online', type: 'success' },
  [CliRuntimeStatus.Offline]: { label: 'status.offline', type: 'warning' },
  [CliRuntimeStatus.Disabled]: { label: 'status.disabled', type: 'default' },
}

/**
 * Runtime edit form. `labelKey` + English `label` is the house shape: the key
 * renders through the page translator, the literal is the fallback.
 */
export const formSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'columns.name', label: 'Name', type: 'text', required: true },
  {
    key: 'status',
    labelKey: 'columns.status',
    label: 'Status',
    type: 'select',
    // Offline is deliberately absent: it is what a probe concluded, not a state
    // an admin can assert. Taking a runtime out of service is `Disabled`.
    options: [
      { labelKey: 'status.online', label: 'Online', value: CliRuntimeStatus.Online },
      { labelKey: 'status.disabled', label: 'Disabled', value: CliRuntimeStatus.Disabled },
    ],
  },
  { key: 'maxConcurrentRuns', labelKey: 'columns.maxConcurrentRuns', label: 'Max concurrent', type: 'number' },
]

/** Run status → badge mapping, shared by the run list and its detail drawer. */
export const runStatusBadgeMapping: Record<string, { label: string; type: string }> = {
  [CliRunStatus.Queued]: { label: 'runStatus.queued', type: 'default' },
  [CliRunStatus.Dispatched]: { label: 'runStatus.dispatched', type: 'info' },
  [CliRunStatus.Running]: { label: 'runStatus.running', type: 'info' },
  [CliRunStatus.Completed]: { label: 'runStatus.completed', type: 'success' },
  [CliRunStatus.Failed]: { label: 'runStatus.failed', type: 'error' },
  // Cancelled and TimedOut are warnings, not errors: neither means the agent
  // misbehaved, and colouring them red trains operators to ignore real failures.
  [CliRunStatus.Cancelled]: { label: 'runStatus.cancelled', type: 'warning' },
  [CliRunStatus.TimedOut]: { label: 'runStatus.timedOut', type: 'warning' },
}

/**
 * Run-list filters.
 *
 * Every field here is one the backend genuinely honours (`CliRunQueryDto` ->
 * `WhereIf`). Declaring one it does not would hand the operator a control that
 * appears to do nothing, which is worse than not offering it.
 *
 * The two questions this page gets opened for are "what failed" and "what ran in
 * this window", so status and the date range are what it exposes. `agentId`,
 * `cliRuntimeId` and `threadId` are also supported by the API but need an entity
 * picker to be usable as anything other than a pasted GUID; they stay API-only
 * until there is a reason to add one.
 */
export const runSearchFields: FormSchemaItem[] = [
  {
    key: 'status',
    labelKey: 'search.status',
    label: 'Status',
    type: 'select',
    // `labelKey` + English `label` is the house shape: the key is what renders,
    // the literal is the fallback when a locale has no entry for it.
    options: [
      { value: CliRunStatus.Queued, labelKey: 'runStatus.queued', label: 'Queued' },
      { value: CliRunStatus.Dispatched, labelKey: 'runStatus.dispatched', label: 'Dispatched' },
      { value: CliRunStatus.Running, labelKey: 'runStatus.running', label: 'Running' },
      { value: CliRunStatus.Completed, labelKey: 'runStatus.completed', label: 'Completed' },
      { value: CliRunStatus.Failed, labelKey: 'runStatus.failed', label: 'Failed' },
      { value: CliRunStatus.Cancelled, labelKey: 'runStatus.cancelled', label: 'Cancelled' },
      { value: CliRunStatus.TimedOut, labelKey: 'runStatus.timedOut', label: 'Timed out' },
    ],
    placeholderKey: 'search.statusPlaceholder',
  },
  { key: 'startTime', labelKey: 'search.startTime', label: 'From', type: 'date' },
  { key: 'endTime', labelKey: 'search.endTime', label: 'To', type: 'date' },
]

/** A run's duration as the list and the drawer show it. */
export function formatRunDuration(durationMs: number | null | undefined): string | null {
  return durationMs ? `${durationMs} ms` : null
}

/** A run's estimated cost as the list and the drawer show it. */
export function formatRunCost(costUsd: number | null | undefined): string | null {
  return costUsd != null ? `$${costUsd.toFixed(4)}` : null
}

/** Columns for the run list - a table, because these ARE compared across rows. */
export const runColumns: ColumnDef<CliRunDto>[] = [
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    minWidth: 160,
    render: (row) => formatDateTime(row.creationTime, { fallback: EMPTY_DASH }),
  },
  { key: 'providerKey', title: 'columns.provider', minWidth: 110 },
  {
    key: 'status',
    title: 'columns.status',
    minWidth: 110,
    // The badge resolves its label from the locale root, so the page-scoped
    // key is qualified here.
    render: (row) => {
      const badge = runStatusBadgeMapping[row.status]
      if (!badge) return row.status
      return h(TStatusBadge, {
        value: row.status,
        type: badge.type as never,
        labelKey: `admin.modules.ai.cliRuns.${badge.label}`,
      })
    },
  },
  { key: 'prompt', title: 'columns.prompt', minWidth: 260 },
  { key: 'durationMs', title: 'columns.duration', width: 110, render: (row) => formatRunDuration(row.durationMs) ?? EMPTY_DASH },
  { key: 'estimatedCostUsd', title: 'columns.cost', width: 110, render: (row) => formatRunCost(row.estimatedCostUsd) ?? EMPTY_DASH },
]
