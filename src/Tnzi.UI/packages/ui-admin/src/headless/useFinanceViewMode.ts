/**
 * `useFinanceViewMode` - how much of the finance area a user wants in front of
 * them, the way Xero splits its product.
 *
 * An owner wants invoices, bills and the bank; they do not want the journals,
 * the fiscal-year roll or exchange-rate maintenance in their sidebar. Owner
 * mode drops those pages from the menu. That is the whole of it.
 *
 * **It does not change any wording.** Terminology is decided by what a screen
 * shows, never by who is looking at it: a screen listing one funds account's
 * own side says money in / money out for everybody, and a screen showing a
 * whole voucher says debit / credit for everybody. A relabelling switch breaks
 * the thing an owner most needs it for - their accountant opening the same
 * screen has to see the same words in order to talk about it. QuickBooks is
 * currently dismantling its own two-view split for exactly that reason;
 * Xero's fixed-vocabulary model is the one worth copying. The per-view choice
 * lives in `moneyPairColumns` (components/finance).
 *
 * This is deliberately an **information-architecture** switch, not a
 * permission: both layers show the same tenant's same data, and a user can
 * flip freely. Access control stays with `finance.*` permission codes - a
 * bookkeeper without `finance.journalEntry.view` cannot reach the journals in
 * either layer. Because the routes stay registered, a deep link into a hidden
 * page still resolves and renders normally.
 */
import { computed, ref, watch } from 'vue'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'

export type FinanceViewMode = 'owner' | 'accountant'

const STORAGE_KEY = 'tnzi-admin-finance-view-mode'
const HIDDEN_OWNER_KEY = 'finance-view-mode'

/**
 * Routes that only make sense once you accept double-entry bookkeeping.
 *
 * Everything else in the Finance area (invoices, bills, expenses, payments,
 * customers, vendors, banking, reports) is meaningful to an owner and stays
 * visible in both layers.
 */
export const ACCOUNTANT_ONLY_ROUTES = [
  // The whole `finance.group.ledger` group - the store drops a directory whose
  // children were all filtered, so the "Ledger" heading collapses with them.
  'finance.accounts',
  'finance.journals',
  'finance.fiscalYears',
  'finance.revaluations',
  // Setup group: exchange-rate maintenance is a bookkeeping chore; Items and
  // Taxes stay because owners edit their own price list and tax codes.
  'finance.rates',
] as const

function loadPersisted(): FinanceViewMode {
  if (typeof localStorage === 'undefined') return 'accountant'
  const raw = localStorage.getItem(STORAGE_KEY)
  return raw === 'owner' || raw === 'accountant' ? raw : 'accountant'
}

/**
 * Module-level so every mounted finance surface agrees on the layer.
 *
 * Default is `accountant`: the framework ships the full ledger and an existing
 * deployment must not silently lose menu entries on upgrade. Consumer apps
 * aimed at owners call `setFinanceViewMode('owner')` at boot.
 */
const mode = ref<FinanceViewMode>(loadPersisted())

watch(mode, (next) => {
  if (typeof localStorage === 'undefined') return
  try {
    localStorage.setItem(STORAGE_KEY, next)
  } catch {
    /* private mode - the choice still holds for this session. */
  }
})

export function useFinanceViewMode() {
  const routeStore = useAdminRouteStore()

  // Re-publish on every consumer mount as well as on change: the store is
  // cleared on logout (`clearRoutes`), so a session restored into owner mode
  // must re-contribute its hidden routes rather than assume they survived.
  function publish(next: FinanceViewMode) {
    routeStore.setRuntimeHiddenRoutes(
      HIDDEN_OWNER_KEY,
      next === 'owner' ? [...ACCOUNTANT_ONLY_ROUTES] : [],
    )
  }
  publish(mode.value)
  watch(mode, publish)

  const isAccountant = computed(() => mode.value === 'accountant')
  const isOwner = computed(() => mode.value === 'owner')

  return {
    mode,
    isAccountant,
    isOwner,
    setMode: (next: FinanceViewMode) => {
      mode.value = next
    },
    toggle: () => {
      mode.value = mode.value === 'owner' ? 'accountant' : 'owner'
    },
    /** True when a surface should render raw double-entry detail. */
    showDoubleEntry: isAccountant,
  }
}

/** Set the layer outside a component (app boot, consumer config). */
export function setFinanceViewMode(next: FinanceViewMode): void {
  mode.value = next
}
