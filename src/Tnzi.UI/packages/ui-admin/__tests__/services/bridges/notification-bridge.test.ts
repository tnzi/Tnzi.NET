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

function mockTemplateApi() {
  return {
    getPagedList: vi.fn(async () => ({
      items: [{ id: 't1', name: 'welcome', module: 'Notification' }],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    getById: vi.fn(async () => ({ id: 't1', name: 'welcome' })),
    create: vi.fn(async (data) => ({ id: 't-new', ...data })),
    update: vi.fn(async (id, data) => ({ id, ...data })),
    delete: vi.fn(async () => undefined),
    batchDelete: vi.fn(async () => undefined),
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

  it('messages.delete rejects when the API resolves a failure envelope', async () => {
    const notificationApi = mockNotificationApi()
    notificationApi.batchDelete = vi.fn(async () => ({
      succeeded: false,
      success: false,
      message: 'Delete vetoed by policy',
    })) as never
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await expect(bridge.messages.delete(['n1'])).rejects.toThrow('Delete vetoed by policy')
  })

  it('messages.send rejects when the API resolves a failure envelope', async () => {
    const notificationApi = mockNotificationApi()
    notificationApi.send = vi.fn(async () => ({ succeeded: false, success: false })) as never
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await expect(bridge.messages.send('n1')).rejects.toThrow('Request failed')
  })

  it('messages.delete tolerates an empty success envelope (void endpoint)', async () => {
    const notificationApi = mockNotificationApi()
    notificationApi.batchDelete = vi.fn(async () => ({
      succeeded: true,
      success: true,
      data: null,
    })) as never
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await expect(bridge.messages.delete(['n1'])).resolves.toBeUndefined()
  })

  it('messages.create is a read-only reject stub (messages are sent not CRUDed)', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.messages.create({})).rejects.toThrow()
  })

  it('messages.update is a read-only reject stub', async () => {
    const bridge = createNotificationBridge({ notificationApi: mockNotificationApi() as never })
    await expect(bridge.messages.update('id', {})).rejects.toThrow()
  })

  it('messages.cancel calls notificationApi.cancel with the id', async () => {
    const notificationApi = mockNotificationApi()
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await bridge.messages.cancel('n1')
    expect(notificationApi.cancel).toHaveBeenCalledWith('n1')
  })

  it('messages.cancel rejects when the API resolves a failure envelope', async () => {
    const notificationApi = mockNotificationApi()
    notificationApi.cancel = vi.fn(async () => ({
      succeeded: false,
      success: false,
      message: 'Already sent',
    })) as never
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await expect(bridge.messages.cancel('n1')).rejects.toThrow('Already sent')
  })

  it('messages.batchCancel returns how many were actually stopped', async () => {
    const notificationApi = mockNotificationApi()
    notificationApi.batchCancel = vi.fn(async () => 2) as never
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    await expect(bridge.messages.batchCancel(['n1', 'n2', 'n3'])).resolves.toBe(2)
    expect(notificationApi.batchCancel).toHaveBeenCalledWith(['n1', 'n2', 'n3'])
  })

  it('messages.getDeliveryReport unwraps the report', async () => {
    const notificationApi = mockNotificationApi()
    notificationApi.getDeliveryReport = vi.fn(async () => ({
      messageId: 'n1',
      totalRecipients: 3,
      sentCount: 2,
      failedCount: 1,
      pendingCount: 0,
      readCount: 0,
      successRate: 66.7,
      recipients: [{ id: 'r1', address: 'a@example.com', status: 'Sent', isRead: false }],
    })) as never
    const bridge = createNotificationBridge({ notificationApi: notificationApi as never })
    const report = await bridge.messages.getDeliveryReport('n1')
    expect(notificationApi.getDeliveryReport).toHaveBeenCalledWith('n1')
    expect(report.recipients).toHaveLength(1)
    expect(report.failedCount).toBe(1)
  })

  // ---- templates sub-contract ----

  it('templates.fetch goes through the core template api factory, not a hand-built URL', async () => {
    const notificationApi = mockNotificationApi()
    const templateApi = mockTemplateApi()
    const bridge = createNotificationBridge({
      notificationApi: notificationApi as never,
      templateApi: templateApi as never,
    })

    const result = await bridge.templates.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })

    expect(templateApi.getPagedList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
  })

  it('templates.delete uses the single-id endpoint for one and batch for many', async () => {
    const templateApi = mockTemplateApi()
    const bridge = createNotificationBridge({
      notificationApi: mockNotificationApi() as never,
      templateApi: templateApi as never,
    })

    await bridge.templates.delete(['t1'])
    expect(templateApi.delete).toHaveBeenCalledWith('t1')
    expect(templateApi.batchDelete).not.toHaveBeenCalled()

    await bridge.templates.delete(['t1', 't2'])
    expect(templateApi.batchDelete).toHaveBeenCalledWith(['t1', 't2'])
  })

  // ---- templates sub-contract (no template api - stubs reject) ----

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

  // ---- subscriptions sub-contract (backend gap - stubs reject) ----

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
