/**
 * The shell's billing standing, for a page outside the shell's billing host.
 *
 * Kept apart from `billing-context` (which loads the billing strings) so the
 * `@tnzi/ui-admin/billing` entry can export it statically without pulling the
 * billing UI along.
 */
import { computed, type ComputedRef } from 'vue'
import type { BillingStandingDto } from '@tnzi/core/services/payment'
import { createBillingBridge, type BillingBridge } from '../../services/bridges/billing-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminBillingConfig } from '../../plugin/billing-config'
import { useBillingStandingStore, type BillingShellView } from './billing-standing'

/**
 * The standing store, configured for wherever it is used: the shell's billing
 * host configures it for the app's product; a page mounted without that host
 * configures it here.
 */
export function useConfiguredStandingStore(bridge?: BillingBridge) {
  const store = useBillingStandingStore()
  store.configureIfUnset(bridge ?? createBillingBridge({ client: useAdminClient() }), useAdminBillingConfig()?.productCode)
  return store
}

export interface UseBillingStandingReturn {
  /** The last standing read (null before the first read, or with billing off). */
  standing: ComputedRef<BillingStandingDto | null>
  /** What the shell shows for it: `normal`, the overdue `banner`, or the `suspended` screen. */
  view: ComputedRef<BillingShellView>
  /**
   * Read the standing again. Call it after the page paid or changed the card: the shell's banner or suspended
   * screen follows the new standing without a reload. Calls during a read join it.
   */
  refresh: () => Promise<BillingStandingDto | null>
}

/** The shell's billing standing and its refresh, for an app's own billing page. Call in `setup`. */
export function useBillingStanding(): UseBillingStandingReturn {
  const store = useConfiguredStandingStore()
  return {
    standing: computed(() => store.standing),
    view: computed(() => store.view),
    refresh: () => store.refresh(),
  }
}
