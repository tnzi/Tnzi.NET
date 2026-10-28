/**
 * An order row shows what was collected once it was paid, and what was asked
 * for otherwise: a failed or expired payment collected nothing, and "CA$0.00"
 * hid the amount that was attempted.
 */
import { describe, it, expect } from 'vitest'
import { orderAmount } from '../../src/pages/payment/order-config'

describe('orderAmount', () => {
  it('is the collected amount once paid', () => {
    expect(orderAmount({ paidAmount: 113, payableAmount: 113 })).toBe(113)
  })

  it('is the attempted amount when nothing was collected', () => {
    expect(orderAmount({ paidAmount: 0, payableAmount: 113 })).toBe(113)
  })
})
