import { describe, it, expect, vi } from 'vitest'
import { createBillingBridge, isSubscriptionSuspendedError } from '../../../src/services/bridges/billing-bridge'

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data })
const refused = { succeeded: false, success: false, code: 403, message: 'Forbidden' }

function fakeClient() {
  return {
    get: vi.fn(async (_url: string, _opts?: unknown) => ok<unknown>(null)),
    post: vi.fn(async (_url: string, _body?: unknown) => ok<unknown>(null)),
    put: vi.fn(async () => ok<unknown>(null)),
    delete: vi.fn(async () => ok<unknown>(null)),
    download: vi.fn(async () => ok(new Blob(['%PDF']))),
  }
}

describe('createBillingBridge', () => {
  it('reads the standing for a product from subscriptions/standing', async () => {
    const client = fakeClient()
    client.get.mockResolvedValueOnce(ok({ standing: 'Good' }))
    const standing = await createBillingBridge({ client: client as never }).getStanding('crm')
    expect(client.get).toHaveBeenCalledWith('/subscriptions/standing', { params: { productCode: 'crm' } })
    expect(standing).toEqual({ standing: 'Good' })
  })

  it('downloads the invoice PDF through client.download, never as a link', async () => {
    const client = fakeClient()
    const blob = await createBillingBridge({ client: client as never }).downloadInvoicePdf('inv-1')
    expect(client.download).toHaveBeenCalledWith('/invoices/inv-1/pdf')
    expect(blob).toBeInstanceOf(Blob)
  })

  it('pays now, binds a card as default, attaches it and syncs the payment on the documented paths', async () => {
    const client = fakeClient()
    client.post.mockResolvedValue(ok({}))
    const bridge = createBillingBridge({ client: client as never })

    await bridge.payNow('sub-1')
    await bridge.bindPaymentMethod('pm_1', 'Stripe', true)
    await bridge.attachPaymentMethod('sub-1', 'stored-1')
    await bridge.syncPayment('T-1')
    await bridge.createSetupSession()

    expect(client.post).toHaveBeenCalledWith('/subscriptions/sub-1/pay-now')
    expect(client.post).toHaveBeenCalledWith('/payment-methods', { paymentMethodToken: 'pm_1', channelCode: 'Stripe', setAsDefault: true })
    expect(client.post).toHaveBeenCalledWith('/subscriptions/sub-1/payment-method', { paymentMethodId: 'stored-1' })
    expect(client.post).toHaveBeenCalledWith('/payments/T-1/sync')
    expect(client.post).toHaveBeenCalledWith('/payment-methods/setup', {})
  })

  it('lists active plans for a product, starts a subscription and ends a sign-up immediately', async () => {
    const client = fakeClient()
    client.get.mockResolvedValueOnce(ok([{ id: 'plan-1' }]))
    client.post.mockResolvedValue(ok({ subscription: { id: 'sub-1' } }))
    const bridge = createBillingBridge({ client: client as never })

    expect(await bridge.listPlans('firm')).toEqual([{ id: 'plan-1' }])
    await bridge.createSubscription('plan-1')
    await bridge.cancelSubscription('sub-1', 'Replaced')

    expect(client.get).toHaveBeenCalledWith('/subscriptions/plans', { params: { activeOnly: true, productCode: 'firm' } })
    expect(client.post).toHaveBeenCalledWith('/subscriptions', { planId: 'plan-1' })
    expect(client.post).toHaveBeenCalledWith('/subscriptions/sub-1/cancel', { reason: 'Replaced', immediate: true })
  })

  it('rejects refused reads and writes instead of resolving them as empty or done', async () => {
    const client = fakeClient()
    client.get.mockResolvedValue(refused as never)
    client.post.mockResolvedValue(refused as never)
    client.delete.mockResolvedValue(refused as never)
    const bridge = createBillingBridge({ client: client as never })

    await expect(bridge.getStanding()).rejects.toThrow('Forbidden')
    await expect(bridge.listSubscriptions()).rejects.toThrow('Forbidden')
    await expect(bridge.listPaymentMethods()).rejects.toThrow('Forbidden')
    await expect(bridge.listInvoices(1, 20)).rejects.toThrow('Forbidden')
    await expect(bridge.payNow('sub-1')).rejects.toThrow('Forbidden')
    await expect(bridge.listPlans()).rejects.toThrow('Forbidden')
    await expect(bridge.createSubscription('plan-1')).rejects.toThrow('Forbidden')
    await expect(bridge.cancelSubscription('sub-1', 'x')).rejects.toThrow('Forbidden')
    await expect(bridge.setDefaultPaymentMethod('pm')).rejects.toThrow('Forbidden')
    await expect(bridge.removePaymentMethod('pm')).rejects.toThrow('Forbidden')
  })

  it('re-exports the gate refusal check', () => {
    expect(isSubscriptionSuspendedError({ succeeded: false, code: 402, errorCode: 'SUBSCRIPTION_SUSPENDED' })).toBe(true)
  })
})
