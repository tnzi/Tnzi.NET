/**
 * Scheduled Job config — wired to Tnzi.Hangfire admin API (2026-04-14).
 *
 * Backend: DefaultScheduledJobAdminController at /admin/scheduled-jobs.
 * Shape mirrors Tnzi.Hangfire.Dtos.ScheduledJobDto (Hangfire recurring job
 * projection). Columns below match those real fields — the previous Phase 3
 * stub used speculative field names (name/lastRun/nextRun/enabled) that do
 * not exist on Hangfire recurring jobs.
 */
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const scheduledJobColumns: ColumnDef[] = [
  { key: 'id',            title: 'Job ID' },
  { key: 'cron',          title: 'Cron' },
  { key: 'queue',         title: 'Queue' },
  { key: 'lastExecution', title: 'Last Run' },
  { key: 'nextExecution', title: 'Next Run' },
  { key: 'lastJobState',  title: 'Last State' },
  { key: 'removed',       title: 'Removed' },
]

/**
 * Form schema only matters for the view dialog since create/update are not
 * supported by the Hangfire admin surface (recurring jobs are registered in
 * code via IBackgroundJobManager.CreateRecurring, not via admin UI).
 */
export const scheduledJobFormSchema: FormSchemaItem[] = [
  { key: 'id',            label: 'Job ID',      type: 'text' },
  { key: 'cron',          label: 'Cron',        type: 'text' },
  { key: 'queue',         label: 'Queue',       type: 'text' },
  { key: 'lastExecution', label: 'Last Run',    type: 'text' },
  { key: 'nextExecution', label: 'Next Run',    type: 'text' },
  { key: 'lastJobId',     label: 'Last Job ID', type: 'text' },
  { key: 'lastJobState',  label: 'Last State',  type: 'text' },
  { key: 'error',         label: 'Error',       type: 'textarea' },
]
