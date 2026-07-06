import { h } from 'vue'
import { formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import { translatePageKey } from '../../_shared/translate'
import type { UserQuotaDto } from '@tnzi/core/services/ai'

// QuotaWarningLevel enum values (None=0 / Warning=1 / Critical=2). Inlined as
// literals because pages/** may only `import type` from @tnzi/core/services/*
// (the no-restricted-imports bridge guard), and the bridge does not re-export
// this enum. Kept in sync with @tnzi/core/services/ai QuotaWarningLevel.
const WARNING_LEVEL = { None: 0, Warning: 1, Critical: 2 } as const

/**
 * Quotas page config — budget cost dashboard + per-user quota CRUD.
 *
 * Shape derives from @tnzi/core/services/ai UserQuotaDto / SetQuotaDto /
 * UserQuotaQueryDto. The paged list is served by POST /admin/quotas/query and
 * surfaced through the bridge as `quota.fetch()`. The budget dashboard above
 * the table is fed by `quota.getBudgetSummary()` (GET /admin/quotas/budget/summary).
 *
 * Notes:
 *   - The DTO splits the warning slider into `warningThreshold` and
 *     `criticalThreshold` (both 0-1 ratios on SetQuotaDto). The displayed
 *     `warningLevel` enum (None/Warning/Critical) is computed server-side and
 *     shown as a read-only colour-coded badge.
 *   - SetQuotaDto has no `isEnabled` field, so the form cannot toggle it; it is
 *     surfaced as a read-only column. The query filter still supports it.
 *   - Bridge `quota.delete` rejects (notImplemented) — the page omits
 *     `deleteData`, so the delete affordance is hidden automatically.
 */

/** Render a 0-1 ratio as a percentage string (e.g. 0.05 → "5%"). */
export function formatPercent(ratio: unknown): string {
  const n = typeof ratio === 'number' ? ratio : Number(ratio)
  if (!Number.isFinite(n)) return '—'
  return `${Math.round(n * 1000) / 10}%`
}

/** Clamp a 0-1 ratio to a 0-100 integer for progress bars. */
export function percentValue(ratio: unknown): number {
  const n = typeof ratio === 'number' ? ratio : Number(ratio)
  if (!Number.isFinite(n)) return 0
  return Math.max(0, Math.min(100, Math.round(n * 1000) / 10))
}

/** Render a token count compactly (e.g. 2000000 → "2,000,000"). */
export function formatTokens(value: unknown): string {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n)) return '—'
  return n.toLocaleString('en-US')
}

/**
 * "No limit" sentinel guard. The backend models an unlimited quota as a huge
 * long (≈ long.MaxValue / 2 ≈ 4.6e18); rendering that as a token count shows
 * an astronomic number. Anything above 1e15 tokens is not a real limit.
 */
const UNLIMITED_THRESHOLD = 1e15

/** Render a daily/monthly limit; the unlimited sentinel shows a label. */
export function formatLimit(value: unknown): string {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n)) return '—'
  if (n > UNLIMITED_THRESHOLD) return translatePageKey('ai.quota', 'unlimited')
  return formatTokens(n)
}

/**
 * Badge mappings shared by the (removed) table column and the card list, so the
 * warning-level / enabled chips read identically wherever a rule is surfaced.
 * Keys must be absolute (`admin.modules.ai.quota.*` / `admin.shared.*`) because
 * the admin TStatusBadge wrapper resolves labelKeys with an EMPTY page namespace.
 */
export const warningLevelBadgeMapping = {
  [WARNING_LEVEL.None]: { type: 'success', labelKey: 'admin.modules.ai.quota.warningLevel.none' },
  [WARNING_LEVEL.Warning]: { type: 'warning', labelKey: 'admin.modules.ai.quota.warningLevel.warning' },
  [WARNING_LEVEL.Critical]: { type: 'error', labelKey: 'admin.modules.ai.quota.warningLevel.critical' },
} as const

export const enabledBadgeMapping = {
  true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
  false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
} as const

/**
 * Normalise a UserQuotaDto warning level to its enum number (None fallback).
 * The backend serialises the enum as the PascalCase member NAME
 * (JsonStringEnumConverter: None / Warning / Critical); a numeric ordinal is
 * still accepted for backward compatibility. Returning a number keeps the
 * numeric-keyed `warningLevelBadgeMapping` lookup working.
 */
export function warningLevelValue(row: UserQuotaDto): number {
  const raw = (row as unknown as { warningLevel?: number | string }).warningLevel
  const map: Record<string, number> = {
    '0': WARNING_LEVEL.None,
    '1': WARNING_LEVEL.Warning,
    '2': WARNING_LEVEL.Critical,
    None: WARNING_LEVEL.None,
    Warning: WARNING_LEVEL.Warning,
    Critical: WARNING_LEVEL.Critical,
  }
  return map[String(raw ?? WARNING_LEVEL.None)] ?? WARNING_LEVEL.None
}

/** Render a GUID truncated to its first 8 chars (full value in `title`). */
function renderShortId(value: unknown) {
  const full = typeof value === 'string' ? value : value == null ? '' : String(value)
  if (!full) return '—'
  const short = full.length > 8 ? `${full.slice(0, 8)}…` : full
  return h('span', { title: full, class: 'font-mono' }, short)
}

export const quotaColumns: ColumnDef[] = [
  { key: 'userId', title: 'columns.userId', primary: true, render: (row) => renderShortId(row.userId) },
  { key: 'dailyTokenLimit', title: 'columns.dailyTokenLimit', render: (row) => formatLimit(row.dailyTokenLimit) },
  { key: 'monthlyTokenLimit', title: 'columns.monthlyTokenLimit', render: (row) => formatLimit(row.monthlyTokenLimit) },
  { key: 'currentDailyUsage', title: 'columns.currentDailyUsage', render: (row) => formatTokens(row.currentDailyUsage) },
  { key: 'currentMonthlyUsage', title: 'columns.currentMonthlyUsage', render: (row) => formatTokens(row.currentMonthlyUsage) },
  {
    key: 'dailyUsagePercentage',
    title: 'columns.dailyUsagePercentage',
    render: (row) => formatPercent(row.dailyUsagePercentage),
  },
  {
    key: 'monthlyUsagePercentage',
    title: 'columns.monthlyUsagePercentage',
    render: (row) => formatPercent(row.monthlyUsagePercentage),
  },
  {
    key: 'warningLevel',
    title: 'columns.warningLevel',
    width: 130,
    render: (row) =>
      h(TStatusBadge, {
        value: warningLevelValue(row as unknown as UserQuotaDto),
        mapping: warningLevelBadgeMapping,
      }),
  },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isEnabled),
        mapping: enabledBadgeMapping,
      }),
  },
  {
    key: 'lastModificationTime',
    title: 'columns.lastModificationTime',
    render: (row) =>
      formatDateTime(
        (row as { lastModificationTime?: string | null }).lastModificationTime,
        { fallback: '—' },
      ),
  },
]

export const quotaFormSchema: FormSchemaItem[] = [
  { key: 'userId', labelKey: 'form.userId', label: 'User ID', type: 'text', required: true, placeholder: 'Target user UUID' },
  { key: 'dailyTokenLimit', labelKey: 'form.dailyTokenLimit', label: 'Daily Token Limit', type: 'number', min: 0, required: true },
  { key: 'monthlyTokenLimit', labelKey: 'form.monthlyTokenLimit', label: 'Monthly Token Limit', type: 'number', min: 0, required: true },
  {
    key: 'warningThreshold',
    labelKey: 'form.warningThreshold',
    label: 'Warning Threshold (0-1)',
    type: 'number',
    min: 0,
    max: 1,
    placeholderKey: 'form.thresholdHint',
  },
  {
    key: 'criticalThreshold',
    labelKey: 'form.criticalThreshold',
    label: 'Critical Threshold (0-1)',
    type: 'number',
    min: 0,
    max: 1,
    placeholderKey: 'form.thresholdHint',
  },
]

