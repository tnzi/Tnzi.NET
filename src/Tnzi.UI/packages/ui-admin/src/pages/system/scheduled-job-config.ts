/**
 * Scheduled Job config — wired to Tnzi.Hangfire admin API (2026-04-14).
 *
 * Backend: DefaultScheduledJobAdminController at /admin/scheduled-jobs.
 * Shape mirrors Tnzi.Hangfire.Dtos.ScheduledJobDto (Hangfire recurring job
 * projection). Columns below match those real fields — the previous Phase 3
 * stub used speculative field names (name/lastRun/nextRun/enabled) that do
 * not exist on Hangfire recurring jobs.
 */
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

interface ScheduledJobRow {
  id?: string
  cron?: string
  queue?: string
  lastExecution?: string
  nextExecution?: string
  lastJobId?: string
  lastJobState?: string
  error?: string
  removed?: boolean
}

function stateType(state?: string): 'success' | 'warning' | 'error' | 'info' | 'default' {
  switch (state?.toLowerCase()) {
    case 'succeeded': return 'success'
    case 'processing': return 'info'
    case 'failed': return 'error'
    case 'enqueued':
    case 'scheduled': return 'warning'
    default: return 'default'
  }
}

export const scheduledJobColumns: ColumnDef<ScheduledJobRow>[] = [
  { key: 'id', title: 'columns.id', minWidth: 160 },
  {
    key: 'cron',
    title: 'columns.cron',
    width: 140,
    render: (row) =>
      h(
        'code',
        { style: 'font-family: monospace; font-size: 12px; padding: 2px 6px; background: var(--tnzi-layout-bg); border-radius: 3px' },
        row.cron ?? '—',
      ),
  },
  { key: 'queue', title: 'columns.queue', minWidth: 100 },
  {
    key: 'lastJobState',
    title: 'columns.lastJobState',
    width: 120,
    render: (row) =>
      row.lastJobState
        ? h(TStatusBadge, {
            value: row.lastJobState,
            type: stateType(row.lastJobState),
            label: row.lastJobState,
          })
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  {
    key: 'lastExecution',
    title: 'columns.lastExecution',
    width: 140,
    render: (row) => h(TRelativeTime, { value: row.lastExecution }),
  },
  {
    key: 'nextExecution',
    title: 'columns.nextExecution',
    width: 140,
    render: (row) => h(TRelativeTime, { value: row.nextExecution }),
  },
  {
    key: 'removed',
    title: 'columns.removed',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.removed ?? false,
        mapping: {
          true: { type: 'default', label: 'Removed' },
          false: { type: 'success', labelKey: 'admin.shared.status.active' },
        },
      }),
  },
]

/**
 * Form schema only matters for the view dialog since create/update are not
 * supported by the Hangfire admin surface (recurring jobs are registered in
 * code via IBackgroundJobManager.CreateRecurring, not via admin UI).
 */
export const scheduledJobFormSchema: FormSchemaItem[] = [
  { key: 'id',            labelKey: 'form.id', label: 'Job ID',      type: 'text' },
  { key: 'cron',          labelKey: 'form.cron', label: 'Cron',        type: 'text' },
  { key: 'queue',         labelKey: 'form.queue', label: 'Queue',       type: 'text' },
  { key: 'lastExecution', labelKey: 'form.lastExecution', label: 'Last Run',    type: 'text' },
  { key: 'nextExecution', labelKey: 'form.nextExecution', label: 'Next Run',    type: 'text' },
  { key: 'lastJobId',     labelKey: 'form.lastJobId', label: 'Last Job ID', type: 'text' },
  { key: 'lastJobState',  labelKey: 'form.lastJobState', label: 'Last State',  type: 'text' },
  { key: 'error',         labelKey: 'form.error', label: 'Error',       type: 'textarea' },
]
