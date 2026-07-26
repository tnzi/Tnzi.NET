/**
 * Phase 5 Task 5.4 - AgentRunMonitor pure-data config.
 *
 * Column descriptors and status colour map for the master list / header.
 * Kept separate from the SFC so the i18n key sweep (Task 5.16) can pick
 * them up without parsing template syntax.
 */

export interface RunListColumn {
  key: string
  labelKey: string
}

export const runListColumns: RunListColumn[] = [
  { key: 'id', labelKey: 'monitor.col.id' },
  { key: 'status', labelKey: 'monitor.col.status' },
  { key: 'startedAt', labelKey: 'monitor.col.startedAt' },
  { key: 'durationMs', labelKey: 'monitor.col.duration' },
]

/** Coarse status -> CSS modifier suffix used by the page component. */
export const runStatusClass: Record<string, string> = {
  Running: 'is-running',
  Pending: 'is-pending',
  Succeeded: 'is-ok',
  Completed: 'is-ok',
  Failed: 'is-err',
  Cancelled: 'is-cancelled',
  Canceled: 'is-cancelled',
}

/** Trace event shape - backend has no canonical DTO yet (gap doc'd in
 * page comment). Kept loose so SSE messages can flow through unchanged. */
export interface RunTraceEvent {
  type?: string
  timestamp?: string
  content?: string
  raw?: unknown
}
