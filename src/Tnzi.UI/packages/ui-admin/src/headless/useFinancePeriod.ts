/**
 * `useFinancePeriod` - one date range, shared by every finance surface.
 *
 * Stripe's dashboard keeps a single period control and carries the selection
 * across pages; our finance pages each had their own bare `NDatePicker`, so
 * moving from P&L to the general ledger silently reset the range and the two
 * numbers on screen stopped being about the same period.
 *
 * State is module-level (not per-component) and mirrored to `localStorage`, so
 * the range survives navigation and reloads. The comparison period is derived,
 * never stored: "previous period" of a range that the user just widened must
 * follow it.
 */
import { computed, ref, watch } from 'vue'

export type FinancePeriodPreset =
  | 'this-month'
  | 'last-month'
  | 'this-quarter'
  | 'last-quarter'
  | 'year-to-date'
  | 'last-year'
  | 'custom'

export type FinanceComparison = 'none' | 'previous-period' | 'previous-year'

export interface FinancePeriodValue {
  /** `YYYY-MM-DD`, inclusive. */
  from: string
  /** `YYYY-MM-DD`, inclusive. */
  to: string
}

const STORAGE_KEY = 'tnzi-admin-finance-period'

function pad(n: number): string {
  return String(n).padStart(2, '0')
}

function iso(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function parse(value: string): Date {
  const [y, m, d] = value.split('-').map(Number)
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1)
}

/** Last day of the month containing `d` - `new Date(y, m + 1, 0)` idiom. */
function endOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0)
}

/** Inclusive day count, used to shift a range back by its own length. */
function dayspan(from: Date, to: Date): number {
  return Math.round((to.getTime() - from.getTime()) / 86_400_000) + 1
}

export function resolvePreset(preset: FinancePeriodPreset, today = new Date()): FinancePeriodValue | null {
  const y = today.getFullYear()
  const m = today.getMonth()
  switch (preset) {
    case 'this-month':
      return { from: iso(new Date(y, m, 1)), to: iso(endOfMonth(today)) }
    case 'last-month':
      return { from: iso(new Date(y, m - 1, 1)), to: iso(endOfMonth(new Date(y, m - 1, 1))) }
    case 'this-quarter': {
      const q = Math.floor(m / 3) * 3
      return { from: iso(new Date(y, q, 1)), to: iso(endOfMonth(new Date(y, q + 2, 1))) }
    }
    case 'last-quarter': {
      const q = Math.floor(m / 3) * 3 - 3
      return { from: iso(new Date(y, q, 1)), to: iso(endOfMonth(new Date(y, q + 2, 1))) }
    }
    case 'year-to-date':
      return { from: iso(new Date(y, 0, 1)), to: iso(today) }
    case 'last-year':
      return { from: iso(new Date(y - 1, 0, 1)), to: iso(new Date(y - 1, 11, 31)) }
    default:
      return null
  }
}

/**
 * The range to compare against.
 *
 * `previous-period` shifts back by the range's own length (a 7-day range
 * compares to the 7 days before it); `previous-year` shifts back exactly one
 * calendar year, which is what seasonal businesses actually want.
 */
export function resolveComparison(value: FinancePeriodValue, mode: FinanceComparison): FinancePeriodValue | null {
  if (mode === 'none') return null
  const from = parse(value.from)
  const to = parse(value.to)
  if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) return null

  if (mode === 'previous-year') {
    return {
      from: iso(new Date(from.getFullYear() - 1, from.getMonth(), from.getDate())),
      to: iso(new Date(to.getFullYear() - 1, to.getMonth(), to.getDate())),
    }
  }

  const span = dayspan(from, to)
  const prevTo = new Date(from)
  prevTo.setDate(prevTo.getDate() - 1)
  const prevFrom = new Date(prevTo)
  prevFrom.setDate(prevFrom.getDate() - span + 1)
  return { from: iso(prevFrom), to: iso(prevTo) }
}

function loadPersisted(): { value: FinancePeriodValue; preset: FinancePeriodPreset; comparison: FinanceComparison } | null {
  if (typeof localStorage === 'undefined') return null
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Record<string, unknown>
    const from = parsed.from
    const to = parsed.to
    if (typeof from !== 'string' || typeof to !== 'string') return null
    return {
      value: { from, to },
      preset: (parsed.preset as FinancePeriodPreset) ?? 'custom',
      comparison: (parsed.comparison as FinanceComparison) ?? 'none',
    }
  } catch {
    return null
  }
}

const initial = loadPersisted()
const fallback = resolvePreset('year-to-date')!

const periodValue = ref<FinancePeriodValue>(initial?.value ?? fallback)
const periodPreset = ref<FinancePeriodPreset>(initial?.preset ?? 'year-to-date')
const comparisonMode = ref<FinanceComparison>(initial?.comparison ?? 'none')

watch(
  [periodValue, periodPreset, comparisonMode],
  () => {
    if (typeof localStorage === 'undefined') return
    try {
      localStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({ ...periodValue.value, preset: periodPreset.value, comparison: comparisonMode.value }),
      )
    } catch {
      /* private mode / quota - the range still works for this session. */
    }
  },
  { deep: true },
)

export function useFinancePeriod() {
  const comparisonPeriod = computed(() => resolveComparison(periodValue.value, comparisonMode.value))

  function setPreset(preset: FinancePeriodPreset) {
    periodPreset.value = preset
    const resolved = resolvePreset(preset)
    if (resolved) periodValue.value = resolved
  }

  function setRange(next: FinancePeriodValue) {
    periodValue.value = next
    periodPreset.value = 'custom'
  }

  return {
    /** The active range. Mutate through `setRange` / `setPreset`. */
    period: periodValue,
    preset: periodPreset,
    comparison: comparisonMode,
    /** Derived from `period` + `comparison`; `null` when comparison is off. */
    comparisonPeriod,
    setPreset,
    setRange,
    /** `asOf` reporting date - balance-sheet style points-in-time. */
    asOf: computed(() => periodValue.value.to),
  }
}
