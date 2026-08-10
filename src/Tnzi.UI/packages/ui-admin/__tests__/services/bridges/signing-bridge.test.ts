import { describe, it, expect, vi } from 'vitest'
import { createSigningBridge } from '../../../src/services/bridges/signing-bridge'

const envelope = {
  id: 'e1',
  title: 'NDA',
  status: 'Sent',
  isSequential: true,
  expiresAt: '2026-09-01T00:00:00Z',
  recipientCount: 2,
  signedCount: 0,
  creationTime: '2026-08-01T00:00:00Z',
}

function ok<T>(data: T) {
  return { succeeded: true, code: 200, data }
}

function mockApi(overrides: Record<string, unknown> = {}) {
  return {
    getRequests: vi.fn(async () =>
      ok({ items: [envelope], totalCount: 1, pageIndex: 1, pageSize: 20 }),
    ),
    getRequest: vi.fn(async () => ok({ ...envelope, recipients: [] })),
    createRequest: vi.fn(async () => ok({ ...envelope, recipients: [] })),
    sendRequest: vi.fn(async () => ok([{ recipientId: 'r1', name: 'Dana', email: null, token: 'tok' }])),
    voidRequest: vi.fn(async () => ok(undefined)),
    getTemplates: vi.fn(async () => ok({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    getTemplate: vi.fn(async () => ok({ id: 't1', name: 'NDA', fields: [] })),
    createTemplate: vi.fn(async () => ok({ id: 't1', name: 'NDA', fields: [] })),
    updateTemplate: vi.fn(async () => ok({ id: 't1', name: 'NDA', fields: [] })),
    deleteTemplate: vi.fn(async () => ok(undefined)),
    ...overrides,
  }
}

const query = { pageIndex: 1, pageSize: 20, searchText: '', filters: {} }

describe('signing bridge', () => {
  it('maps the paged request response into the CRUD page shape', async () => {
    const api = mockApi()
    const bridge = createSigningBridge({ adminSigningApi: api as never })
    const page = await bridge.requests.fetch(query)
    expect(api.getRequests).toHaveBeenCalled()
    expect(page.items).toHaveLength(1)
    expect(page.totalCount).toBe(1)
  })

  it('returns the issued links from send', async () => {
    const api = mockApi()
    const bridge = createSigningBridge({ adminSigningApi: api as never })
    await expect(bridge.requests.send('e1')).resolves.toEqual([
      { recipientId: 'r1', name: 'Dana', email: null, token: 'tok' },
    ])
  })

  /**
   * ★ A failed void must NOT resolve. `ensureOk` is what turns a
   * `{ succeeded: false }` envelope into a rejection; without it the page
   * would report "Request voided" and refresh into an unchanged list.
   */
  it('throws when void comes back as a failed envelope', async () => {
    const api = mockApi({
      voidRequest: vi.fn(async () => ({ succeeded: false, code: 409, message: 'Already completed' })),
    })
    const bridge = createSigningBridge({ adminSigningApi: api as never })
    await expect(bridge.requests.void('e1')).rejects.toThrow(/Already completed/)
  })

  it('rejects update and delete on a request rather than silently doing nothing', async () => {
    // A dispatched request is evidence: the snapshot is frozen and voiding is
    // the supported way to call it off. Rejecting (rather than no-op) means a
    // caller wiring these up finds out immediately.
    const bridge = createSigningBridge({ adminSigningApi: mockApi() as never })
    await expect(bridge.requests.update('e1', {})).rejects.toThrow(/not supported/)
    await expect(bridge.requests.delete(['e1'])).rejects.toThrow(/not supported/)
  })

  it('deletes templates one id at a time - there is no batch endpoint', async () => {
    const api = mockApi()
    const bridge = createSigningBridge({ adminSigningApi: api as never })
    await bridge.templates.delete(['t1', 't2'])
    expect(api.deleteTemplate).toHaveBeenCalledTimes(2)
    expect(api.deleteTemplate).toHaveBeenNthCalledWith(1, 't1')
    expect(api.deleteTemplate).toHaveBeenNthCalledWith(2, 't2')
  })

  it('stops the template delete loop at the first rejection', async () => {
    // 409 on the first id (referenced by a request) must not silently proceed
    // to delete the rest.
    const api = mockApi({
      deleteTemplate: vi
        .fn()
        .mockResolvedValueOnce({ succeeded: false, code: 409, message: 'In use' })
        .mockResolvedValue(ok(undefined)),
    })
    const bridge = createSigningBridge({ adminSigningApi: api as never })
    await expect(bridge.templates.delete(['t1', 't2'])).rejects.toThrow(/In use/)
    expect(api.deleteTemplate).toHaveBeenCalledTimes(1)
  })

  it('rejects every call when constructed without deps instead of returning undefined', async () => {
    const bridge = createSigningBridge()
    await expect(bridge.requests.fetch(query)).rejects.toThrow(/no deps provided/)
    await expect(bridge.templates.getById('t1')).rejects.toThrow(/no deps provided/)
  })
})
