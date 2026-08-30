import { describe, it, expect, vi } from 'vitest'
import { createDualControlBridge } from '../../../src/services/bridges/dual-control-bridge'

function api() {
  return {
    query: vi.fn(async () => ({
      data: { items: [{ id: 'd1' }], totalCount: 1, pageIndex: 1, pageSize: 20 },
      succeeded: true,
    })),
    get: vi.fn(async () => ({ data: { id: 'd1' }, succeeded: true })),
    approve: vi.fn(async () => ({ data: { id: 'd1', status: 'Approved' }, succeeded: true })),
    reject: vi.fn(async () => ({ data: { id: 'd1', status: 'Rejected' }, succeeded: true })),
    cancel: vi.fn(async () => ({ succeeded: true })),
  }
}

const query = { pageIndex: 1, pageSize: 20 }

describe('dual-control bridge', () => {
  it('rejects every call when no deps are supplied', async () => {
    const bridge = createDualControlBridge()
    await expect(bridge.requests.getById('d1')).rejects.toThrow(/no deps provided/)
    await expect(bridge.requests.approve('d1')).rejects.toThrow(/no deps provided/)
  })

  it('pages through requests', async () => {
    const a = api()
    const bridge = createDualControlBridge({ adminDualControlApi: a as never })

    const page = await bridge.requests.fetch(query)

    expect(a.query).toHaveBeenCalled()
    expect(page.items).toHaveLength(1)
    expect(page.totalCount).toBe(1)
  })

  it('omits an empty comment rather than sending a blank string', async () => {
    const a = api()
    const bridge = createDualControlBridge({ adminDualControlApi: a as never })

    await bridge.requests.approve('d1')
    await bridge.requests.approve('d1', '   ')
    await bridge.requests.reject('d1', '')

    // The backend treats `Comment` as optional; posting `""` would record an
    // empty note where the operator left none.
    expect(a.approve).toHaveBeenNthCalledWith(1, 'd1', undefined)
    expect(a.approve).toHaveBeenNthCalledWith(2, 'd1', undefined)
    expect(a.reject).toHaveBeenCalledWith('d1', undefined)
  })

  it('trims a comment it does send', async () => {
    const a = api()
    const bridge = createDualControlBridge({ adminDualControlApi: a as never })

    await bridge.requests.approve('d1', '  checked  ')

    expect(a.approve).toHaveBeenCalledWith('d1', { comment: 'checked' })
  })

  it('surfaces a failed withdraw instead of resolving quietly', async () => {
    const a = api()
    a.cancel = vi.fn(async () => ({ succeeded: false, message: 'Only the requester can cancel' }))
    const bridge = createDualControlBridge({ adminDualControlApi: a as never })

    await expect(bridge.requests.cancel('d1')).rejects.toThrow(/Only the requester/)
  })
})
