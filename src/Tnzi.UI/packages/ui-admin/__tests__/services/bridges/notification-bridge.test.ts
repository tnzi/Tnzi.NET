import { describe, it, expect, vi } from 'vitest'
import { createNotificationBridge } from '../../../src/services/bridges/notification-bridge'

// ---------------------------------------------------------------------------
// Mock factory helpers
// ---------------------------------------------------------------------------

function mockNotificationApi() {
  return {
    getById: vi.fn(async () => null),
    query: vi.fn(async () => ({
      items: [
        {
          id: 'n1',
          type: 'Email',
          subject: 'Welcome',
          content: 'Hello',
          isHtml: false,
          status: 'Sent',
          retryCount: 0,
          maxRetryCount: 3,
          totalRecipientCount: 1,
          successCount: 1,
          failureCount: 0,
          creationTime: '2026-01-01T00:00:00Z',
          priority: 'Normal',
          category: 'system',
          recipients: [],
          attachments: [],
        },
        {
          id: 'n2',
          type: 'Email',
          subject: 'Alert',
          content: 'Warning',
          isHtml: false,
          status: 'Failed',
          retryCount: 1,
          maxRetryCount: 3,
          totalRecipientCount: 1,
          successCount: 0,
          failureCount: 1,
          creationTime: '2026-01-01T00:01:00Z',
          priority: 'High',
          category: 'system',
          recipients: [],
          attachments: [],
        },
      ],
      totalCount: 2,
      pageIndex: 1,
      pageSize: 20,
    })),
    create: vi.fn(async (data) => ({ id: 'new', ...data })),
    createAndSend: vi.fn(async () => null),
    send: vi.fn(async () => undefined),
    retry: vi.fn(async () => undefined),
    retryFailed: vi.fn(async () => undefined),
    cancel: vi.fn(async () => undefined),
    delete: vi.fn(async () => undefined),
    getStatistics: vi.fn(async () => ({})),
    getStatisticsByChannel: vi.fn(async () => []),
    getStatisticsByStatus: vi.fn(async () => []),
    getFailed: vi.fn(async () => []),
    batchCancel: vi.fn(async () => 0),
    batchDelete: vi.fn(async () => 0),
    getScheduled: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    getStatisticsTrend: vi.fn(async () => ({})),
    preview: vi.fn(async () => ({ subject: 'Preview', content: '<p>Hello</p>', isHtml: true, category: 'system', recipientCount: 1 })),
    resendToFailed: vi.fn(async () => 0),
    getDeliveryReport: vi.fn(async () => ({})),
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('notification-bridge', () => {
  it('exposes messages / templates / subscriptions sub-contracts', () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    expect(typeof bridge.messages.fetch).toBe('function')
    expect(typeof bridge.templates.fetch).toBe('function')
    expect(typeof bridge.subscriptions.fetch).toBe('function')
  })

  // ---- messages sub-contract ----

  it('messages.fetch calls notificationApi.query and returns paged items', async () => {
    const notificationApi = mockNotificationApi()
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    const result = await bridge.messages.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(notificationApi.query).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('messages.send calls notificationApi.send with the id', async () => {
    const notificationApi = mockNotificationApi()
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await bridge.messages.send('n2')
    expect(notificationApi.send).toHaveBeenCalledWith('n2')
  })

  it('messages.delete calls notificationApi.batchDelete', async () => {
    const notificationApi = mockNotificationApi()
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await bridge.messages.delete(['n1', 'n2'])
    expect(notificationApi.batchDelete).toHaveBeenCalledWith(['n1', 'n2'])
  })

  it('messages.create is a read-only reject stub (messages are sent not CRUDed)', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.messages.create({})).rejects.toThrow()
  })

  it('messages.update is a read-only reject stub', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.messages.update('id', {})).rejects.toThrow()
  })

  // ---- templates sub-contract (backend gap — stubs reject) ----

  it('templates.fetch rejects with backend-gap error', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.templates.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
  })

  it('templates.create rejects with backend-gap error', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.templates.create({})).rejects.toThrow()
  })

  it('templates.preview calls notificationApi.preview and returns a rendered result', async () => {
    const notificationApi = mockNotificationApi()
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    const result = await bridge.templates.preview({ templateName: 'tpl1', variables: { name: 'World' } })
    expect(notificationApi.preview).toHaveBeenCalled()
    // Bridge contract returns a structured NotificationTemplatePreviewResult
    // ({ subject?, content: string, isHtml }), not a bare string.
    expect(typeof result.content).toBe('string')
    expect(typeof result.isHtml).toBe('boolean')
  })

  // ---- subscriptions sub-contract (backend gap — stubs reject) ----

  it('subscriptions.fetch rejects with backend-gap error', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.subscriptions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
  })

  it('subscriptions.create rejects with backend-gap error', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.subscriptions.create({})).rejects.toThrow()
  })

  // ---- no-deps fallback ----

  it('bridge with no deps returns stub contracts that reject on call', async () => {
    const bridge = createNotificationBridge()
    await expect(bridge.messages.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
  })
})
