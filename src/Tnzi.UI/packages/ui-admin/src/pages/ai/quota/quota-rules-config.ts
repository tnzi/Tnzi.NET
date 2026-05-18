import { h } from 'vue'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'

/**
 * Phase 5 Task 5.12 — QuotaRules page config.
 *
 * Shape derives from @tnzi/core/services/ai UserQuotaDto / SetQuotaDto /
 * UserQuotaQueryDto. Stage 1 backend prereq added the paged list endpoint
 * (POST /admin/quotas/query), which the bridge exposes as quota.fetch().
 *
 * Plan deviations vs the task sketch:
 *   - The plan listed a `warningLevel` slider 0-1; the real DTO splits it
 *     into `warningThreshold` and `criticalThreshold` (both number, both on
 *     SetQuotaDto). The displayed enum `warningLevel` (None/Warning/Critical)
 *     is computed server-side; we surface it as a read-only column.
 *   - There is NO `isEnabled` field on SetQuotaDto, so the form cannot toggle
 *     it. The DTO has `isEnabled` for display purposes only — kept as a
 *     column. The query filter still supports `isEnabled`.
 *   - Bridge `quota.delete` rejects (notImplemented) — a per-row delete is
 *     not exposed; batch delete in TCrudPage will surface the rejection if
 *     used. The page leaves it on by default since we have no `:hideDelete`
 *     prop, but documents the gap here.
 */
export const quotaColumns: ColumnDef[] = [
  { key: 'userId', title: 'columns.userId' },
  { key: 'dailyTokenLimit', title: 'columns.dailyTokenLimit' },
  { key: 'monthlyTokenLimit', title: 'columns.monthlyTokenLimit' },
  { key: 'currentDailyUsage', title: 'columns.currentDailyUsage' },
  { key: 'currentMonthlyUsage', title: 'columns.currentMonthlyUsage' },
  { key: 'dailyUsagePercentage', title: 'columns.dailyUsagePercentage', visible: false },
  { key: 'monthlyUsagePercentage', title: 'columns.monthlyUsagePercentage', visible: false },
  { key: 'warningLevel', title: 'columns.warningLevel' },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isEnabled),
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
  { key: 'lastModificationTime', title: 'columns.lastModificationTime' },
]

export const quotaFormSchema: FormSchemaItem[] = [
  { key: 'userId', label: 'User ID', type: 'text', required: true, placeholder: 'Target user UUID' },
  { key: 'dailyTokenLimit', label: 'Daily Token Limit', type: 'number', min: 0, required: true },
  { key: 'monthlyTokenLimit', label: 'Monthly Token Limit', type: 'number', min: 0, required: true },
  { key: 'warningThreshold', label: 'Warning Threshold (0-1)', type: 'number', min: 0, max: 1 },
  { key: 'criticalThreshold', label: 'Critical Threshold (0-1)', type: 'number', min: 0, max: 1 },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.quota.*):
 *
 * Page meta:
 *   modules.ai.quota.pageTitle
 *
 * Columns:
 *   modules.ai.quota.columns.userId
 *   modules.ai.quota.columns.dailyTokenLimit
 *   modules.ai.quota.columns.monthlyTokenLimit
 *   modules.ai.quota.columns.currentDailyUsage
 *   modules.ai.quota.columns.currentMonthlyUsage
 *   modules.ai.quota.columns.dailyUsagePercentage
 *   modules.ai.quota.columns.monthlyUsagePercentage
 *   modules.ai.quota.columns.warningLevel
 *   modules.ai.quota.columns.isEnabled
 *   modules.ai.quota.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.quota.form.userId
 *   modules.ai.quota.form.dailyTokenLimit
 *   modules.ai.quota.form.monthlyTokenLimit
 *   modules.ai.quota.form.warningThreshold
 *   modules.ai.quota.form.criticalThreshold
 */
