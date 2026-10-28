import { describe, it, expect, vi } from 'vitest'
import { createPromotionBridge, DiscountType, PromotionType } from '../../src/services/bridges/promotion-bridge'
import { buildPromotionColumns, promotionFormSchema } from '../../src/pages/payment/promotion-config'
import type { PromotionDto } from '../../src/services/bridges/promotion-bridge'

/**
 * A fixed-amount promotion is bound to a currency: checkout rejects it for an
 * order in any other currency. The create whitelist dropped `currency`, so a
 * promotion took the server default whatever the operator meant, and the list
 * showed every fixed discount in US dollars whatever its currency.
 */

function fakeClient() {
  return {
    get: vi.fn(),
    put: vi.fn(async () => ({ succeeded: true, data: null })),
    post: vi.fn(async (_url: string, body: unknown) => ({ succeeded: true, data: { id: 'p1', ...(body as object) } })),
    delete: vi.fn(),
  }
}

const fixedFive = {
  promotionCode: 'SAVE5',
  name: 'Save 5',
  type: PromotionType.FixedAmountDiscount,
  discountType: DiscountType.Fixed,
  discountValue: 5,
}

describe('promotion-bridge create', () => {
  it('passes the currency of a fixed-amount promotion through', async () => {
    const client = fakeClient()
    const bridge = createPromotionBridge({ client: client as never })

    await bridge.create({ ...fixedFive, currency: 'CAD' })

    expect(client.post).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ currency: 'CAD' }))
  })

  // Left blank, the server binds the deployment's configured currency.
  it('leaves a blank currency out', async () => {
    const client = fakeClient()
    const bridge = createPromotionBridge({ client: client as never })

    await bridge.create({ ...fixedFive, currency: '  ' })

    const body = (client.post.mock.calls[0] as unknown[])[1] as Record<string, unknown>
    expect(body.currency).toBeUndefined()
  })
})

describe('promotion-config currency', () => {
  const discountCell = buildPromotionColumns((k) => k).find((c) => c.key === 'discountValue')!

  it('renders a fixed discount in the promotion\'s own currency', () => {
    const row = { discountValue: 5, discountType: DiscountType.Fixed, currency: 'CAD' } as unknown as PromotionDto
    const text = String(discountCell.render!(row as never, 0))
    expect(text).toContain('CA$')
    expect(text).toContain('5.00')
  })

  // A code the formatter does not know must not take the whole list down.
  it('falls back to the bare amount and code for a currency the formatter rejects', () => {
    const row = { discountValue: 5, discountType: DiscountType.Fixed, currency: 'X1' } as unknown as PromotionDto
    expect(() => discountCell.render!(row as never, 0)).not.toThrow()
    expect(String(discountCell.render!(row as never, 0))).toContain('X1')
  })

  it('offers a currency field on create for a fixed discount only', () => {
    const field = promotionFormSchema.find((f) => f.key === 'currency')
    expect(field).toBeDefined()
    expect(field!.visible!({ discountType: DiscountType.Fixed })).toBe(true)
    expect(field!.visible!({ discountType: DiscountType.Percentage })).toBe(false)
    // Immutable once created: `UpdatePromotionDto` has no currency.
    expect(field!.visible!({ id: 'p1', discountType: DiscountType.Fixed })).toBe(false)
  })
})
