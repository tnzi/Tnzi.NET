import { describe, it, expect, vi } from 'vitest'
import { createPromotionBridge } from '../../../src/services/bridges/promotion-bridge'

/**
 * `GET /admin/promotions` filters on one status question: `activeOnly` =
 * enabled AND inside the promotion's start/end window. `false` is "no filter",
 * not "inactive", so the bridge must never present it as one.
 */
function fakeClient() {
  return {
    get: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 } })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  }
}

function sentParams(client: ReturnType<typeof fakeClient>): Record<string, unknown> {
  const [, options] = client.get.mock.calls[0] as unknown as [string, { params?: Record<string, unknown> } | undefined]
  return options?.params ?? {}
}

describe('promotion-bridge getList', () => {
  it('asks for the promotions valid now when the page filters on them', async () => {
    const client = fakeClient()
    await createPromotionBridge({ client: client as never }).getList({ pageIndex: 1, pageSize: 20, validNow: true })
    expect(sentParams(client).activeOnly).toBe(true)
  })

  it('asks for every promotion without the filter', async () => {
    const client = fakeClient()
    await createPromotionBridge({ client: client as never }).getList({ pageIndex: 1, pageSize: 20 })
    expect(sentParams(client).activeOnly).toBe(false)
  })
})
