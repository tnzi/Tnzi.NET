import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { billingShellView, useBillingStandingStore } from '../../../src/pages/billing/billing-standing'
import type { BillingStandingDto } from '@tnzi/core/services/payment'
import type { BillingBridge } from '../../../src/services/bridges/billing-bridge'

function standing(over: Partial<BillingStandingDto>): BillingStandingDto {
  return {
    standing: 'Good' as BillingStandingDto['standing'],
    canView: true,
    canManage: false,
    gateEnabled: true,
    isExempt: false,
    ...over,
  }
}

describe('billingShellView', () => {
  it('renders the normal shell for None / Good and before anything is known', () => {
    expect(billingShellView(null)).toBe('normal')
    expect(billingShellView(standing({ standing: 'None' as never, canManage: true }))).toBe('normal')
    expect(billingShellView(standing({ standing: 'Good' as never, canManage: true }))).toBe('normal')
  })

  it('shows the banner to a manager while a payment is overdue, and nothing to anyone else', () => {
    expect(billingShellView(standing({ standing: 'Warning' as never, canManage: true }))).toBe('banner')
    expect(billingShellView(standing({ standing: 'Warning' as never, canManage: false }))).toBe('normal')
  })

  it('suspends the shell for managers and members alike when the gate applies', () => {
    expect(billingShellView(standing({ standing: 'Suspended' as never, canManage: true }))).toBe('suspended')
    expect(billingShellView(standing({ standing: 'Suspended' as never, canManage: false }))).toBe('suspended')
  })

  it('never suspends a user the gate does not apply to', () => {
    expect(billingShellView(standing({ standing: 'Suspended' as never, isExempt: true, canManage: true }))).toBe('banner')
    expect(billingShellView(standing({ standing: 'Suspended' as never, isExempt: true, canManage: false }))).toBe('normal')
  })
})

describe('useBillingStandingStore', () => {
  beforeEach(() => setActivePinia(createPinia()))

  function bridgeWith(getStanding: BillingBridge['getStanding']): BillingBridge {
    return { getStanding } as unknown as BillingBridge
  }

  it('reads the standing for the configured product', async () => {
    const getStanding = vi.fn(async () => standing({ standing: 'Warning' as never, canManage: true }))
    const store = useBillingStandingStore()
    store.configure(bridgeWith(getStanding), 'crm')

    await store.refresh()

    expect(getStanding).toHaveBeenCalledWith('crm')
    expect(store.view).toBe('banner')
  })

  it('coalesces a burst of refreshes into the one in flight plus one more', async () => {
    let release: () => void = () => undefined
    const getStanding = vi.fn(
      () => new Promise<BillingStandingDto>((resolve) => {
        release = () => resolve(standing({}))
      }),
    )
    const store = useBillingStandingStore()
    store.configure(bridgeWith(getStanding))

    const first = store.refresh()
    void store.refresh()
    void store.refresh()
    release()
    await new Promise((r) => setTimeout(r, 0))
    release()
    await first

    expect(getStanding).toHaveBeenCalledTimes(2)
  })

  it('keeps the last known standing when a read fails', async () => {
    const getStanding = vi
      .fn<BillingBridge['getStanding']>()
      .mockResolvedValueOnce(standing({ standing: 'Suspended' as never }))
      .mockRejectedValueOnce(new Error('offline'))
    const store = useBillingStandingStore()
    store.configure(bridgeWith(getStanding))

    await store.refresh()
    await store.refresh()

    expect(store.view).toBe('suspended')
    expect(store.error).toBe('offline')
  })

  it('drops a read issued for the previous user after reset', async () => {
    let resolveRead: (v: BillingStandingDto) => void = () => undefined
    const getStanding = vi.fn(() => new Promise<BillingStandingDto>((r) => (resolveRead = r)))
    const store = useBillingStandingStore()
    store.configure(bridgeWith(getStanding))

    const pending = store.refresh()
    store.reset()
    resolveRead(standing({ standing: 'Suspended' as never }))
    await pending

    expect(store.standing).toBeNull()
    expect(store.view).toBe('normal')
  })
})
