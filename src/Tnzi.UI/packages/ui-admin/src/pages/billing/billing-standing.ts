/**
 * The signed-in user's billing standing, shared by the shell (banner,
 * suspended screen) and the billing sections (which refresh it after a
 * payment). Loaded lazily with the rest of the billing UI.
 *
 * What the shell renders is decided here and nowhere else:
 *
 * | standing  | gate applies (`!isExempt`) | `canManage` | shell      |
 * |-----------|----------------------------|-------------|------------|
 * | Suspended | yes                        | any         | suspended  |
 * | Suspended | no (exempt)                | yes         | banner     |
 * | Warning   | any                        | yes         | banner     |
 * | otherwise |                            |             | normal     |
 *
 * `canManage` comes from the server; it is never re-derived from permission
 * codes here (in User scope a user holds no code yet manages their own account).
 */
import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { BillingStanding, type BillingStandingDto } from '@tnzi/core/services/payment'
import type { BillingBridge } from '../../services/bridges/billing-bridge'

export type BillingShellView = 'normal' | 'banner' | 'suspended'

export function billingShellView(standing: BillingStandingDto | null | undefined): BillingShellView {
  if (!standing) return 'normal'
  if (standing.standing === BillingStanding.Suspended) {
    if (!standing.isExempt) return 'suspended'
    return standing.canManage ? 'banner' : 'normal'
  }
  if (standing.standing === BillingStanding.Warning && standing.canManage) return 'banner'
  return 'normal'
}

export const useBillingStandingStore = defineStore('tnzi-admin-billing-standing', () => {
  const standing = ref<BillingStandingDto | null>(null)
  const error = ref<string | null>(null)
  const loading = ref(false)

  let bridge: BillingBridge | null = null
  let productCode: string | undefined
  // Generation: bumped by reset() so a read issued for the previous user is dropped.
  let generation = 0
  let inFlight: Promise<BillingStandingDto | null> | null = null
  let again = false

  function configure(next: BillingBridge, product?: string): void {
    bridge = next
    productCode = product
  }

  /**
   * For a section hosted without the shell's billing host (a Settings Center
   * section, a consumer page): configure the store unless the host already did.
   */
  function configureIfUnset(next: BillingBridge, product?: string): void {
    if (!bridge) configure(next, product)
  }

  async function load(): Promise<BillingStandingDto | null> {
    if (!bridge) return null
    const mine = generation
    loading.value = true
    try {
      const result = await bridge.getStanding(productCode)
      if (mine !== generation) return null
      standing.value = result
      error.value = null
      return result
    } catch (err) {
      // Keep the last known standing: a failed read says nothing about the
      // account, and flipping a suspended shell back to normal on a network
      // blip would only bounce the user into refusals.
      if (mine === generation) error.value = err instanceof Error ? err.message : String(err)
      return null
    } finally {
      if (mine === generation) loading.value = false
    }
  }

  /**
   * Read the standing now. Calls made while a read is in flight join it and
   * schedule exactly one more read afterwards, so a burst of refused requests
   * costs two reads, not one per request.
   */
  function refresh(): Promise<BillingStandingDto | null> {
    if (inFlight) {
      again = true
      return inFlight
    }
    const run = async (): Promise<BillingStandingDto | null> => {
      let result = await load()
      while (again) {
        again = false
        result = await load()
      }
      return result
    }
    const pending: Promise<BillingStandingDto | null> = run().finally(() => {
      if (inFlight === pending) inFlight = null
    })
    inFlight = pending
    return pending
  }

  /** Forget the standing (sign-out, another user signed in). */
  function reset(): void {
    generation += 1
    again = false
    inFlight = null
    standing.value = null
    error.value = null
    loading.value = false
  }

  const view = computed<BillingShellView>(() => billingShellView(standing.value))

  return { standing, error, loading, view, configure, configureIfUnset, refresh, reset }
})
