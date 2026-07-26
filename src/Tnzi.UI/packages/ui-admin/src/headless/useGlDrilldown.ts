/**
 * `useGlDrilldown` - "every number can be verified".
 *
 * Stripe's dashboard lets you click any figure and walk down to the events
 * that produced it; that is the mechanism by which a financial UI earns
 * trust, because **users do not trust numbers they cannot check**. Our report
 * pages rendered dead text, so a balance that looked wrong had no path to the
 * rows behind it other than re-navigating to the general ledger by hand and
 * retyping the period.
 *
 * This composable owns the drill-down target (account + period + optional
 * source filter) and the fetched page, so any surface can open it from a money
 * cell: `<TMoney drilldown @drilldown="drill.open({ accountId, accountName })" />`.
 */
import { computed, ref } from 'vue'
import type {
  FinanceBridge,
  GeneralLedgerLineDto,
  GeneralLedgerReportDto,
} from '../services/bridges/finance-bridge'

export interface GlDrilldownTarget {
  accountId: string
  accountName?: string
  accountCode?: string
  /** Overrides the ambient period (e.g. a balance-sheet as-of column). */
  from?: string
  to?: string
  /** Pre-seed the filter, e.g. drilling into "Invoice" rows only. */
  sourceType?: string
}

export interface UseGlDrilldownOptions {
  bridge: FinanceBridge
  /** The ambient reporting period, usually from `useFinancePeriod`. */
  period: () => { from: string; to: string }
  pageSize?: number
}

export function useGlDrilldown(options: UseGlDrilldownOptions) {
  const { bridge, period, pageSize = 25 } = options

  const target = ref<GlDrilldownTarget | null>(null)
  const report = ref<GeneralLedgerReportDto | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const pageIndex = ref(1)
  const keyword = ref('')
  /** Newest-first, matching how people read a bank statement. */
  const descending = ref(true)

  const rows = computed<GeneralLedgerLineDto[]>(() => report.value?.lines.items ?? [])
  const total = computed(() => report.value?.lines.totalCount ?? 0)
  /**
   * The backend zeroes opening/closing/running balances whenever a filter is
   * active, and flags it. Surfacing the flag lets the table drop the balance
   * column entirely instead of showing zeroes that read as real balances.
   */
  const balancesApply = computed(() => report.value !== null && report.value.isFiltered !== true)

  /**
   * Request sequence token.
   *
   * Typing a keyword then immediately paging fires two loads; if the first is
   * slower it resolves last and paints the unfiltered first page while the
   * pager still reads "2" - rows and controls disagreeing is exactly the kind
   * of thing that makes a finance UI untrustworthy. Only the newest request is
   * allowed to write `report` / `error` / `loading`.
   */
  let seq = 0

  async function load() {
    const t = target.value
    if (!t) return
    const token = ++seq
    loading.value = true
    error.value = null
    try {
      const range = period()
      const next = await bridge.reports.generalLedger(
        t.accountId,
        t.from ?? range.from,
        t.to ?? range.to,
        pageIndex.value,
        pageSize,
        {
          keyword: keyword.value.trim() || undefined,
          sourceType: t.sourceType,
          descending: descending.value,
        },
      )
      if (token !== seq) return
      report.value = next
    } catch (e) {
      if (token !== seq) return
      error.value = e instanceof Error ? e.message : String(e)
      report.value = null
    } finally {
      if (token === seq) loading.value = false
    }
  }

  /**
   * Point the drill-down at an account and fetch its first page.
   *
   * Visibility is NOT owned here: the host opens its `useDetail` overlay. Two
   * sources of truth for "is it open" is exactly the split the detail-engine
   * consolidation removed.
   */
  async function openFor(next: GlDrilldownTarget) {
    target.value = next
    pageIndex.value = 1
    keyword.value = ''
    report.value = null
    await load()
  }

  async function goToPage(next: number) {
    pageIndex.value = next
    await load()
  }

  async function search(next: string) {
    keyword.value = next
    pageIndex.value = 1
    await load()
  }

  async function toggleOrder() {
    descending.value = !descending.value
    pageIndex.value = 1
    await load()
  }

  return {
    target,
    report,
    rows,
    total,
    loading,
    error,
    pageIndex,
    pageSize,
    keyword,
    descending,
    balancesApply,
    openFor,
    reload: load,
    goToPage,
    search,
    toggleOrder,
  }
}
