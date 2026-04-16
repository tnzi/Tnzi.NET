import { describe, it, expect, vi } from 'vitest'
import { createChatBridge } from '../../../src/services/bridges/chat-bridge'

// ---------------------------------------------------------------------------
// Mock factory helpers
// ---------------------------------------------------------------------------

function mockChatSessionApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        {
          id: 's1',
          title: 'Launch announcements',
          status: 1,
          participants: ['u1', 'u2'],
          messageCount: 5,
          lastMessageAt: '2026-01-10T12:00:00Z',
          creationTime: '2026-01-01T00:00:00Z',
        },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    getById: vi.fn(async () => ({
      id: 's1',
      title: 'Launch announcements',
      description: null,
      status: 1,
      participants: ['u1', 'u2'],
      messageCount: 5,
      lastMessageAt: '2026-01-10T12:00:00Z',
      creationTime: '2026-01-01T00:00:00Z',
      lastModificationTime: null,
    })),
    create: vi.fn(async () => ({
      id: 's-new',
      title: 'New session',
      description: null,
      status: 1,
      participants: [],
      messageCount: 0,
      lastMessageAt: null,
      creationTime: '2026-04-14T00:00:00Z',
      lastModificationTime: null,
    })),
    update: vi.fn(async () => ({
      id: 's1',
      title: 'Renamed',
      description: null,
      status: 2,
      participants: ['u1'],
      messageCount: 5,
      lastMessageAt: '2026-01-10T12:00:00Z',
      creationTime: '2026-01-01T00:00:00Z',
      lastModificationTime: '2026-04-14T00:00:00Z',
    })),
    delete: vi.fn(async () => undefined),
    deleteBatch: vi.fn(async () => 2),
  }
}

function mockChatAdminApi() {
  return {
    query: vi.fn(async () => ({
      items: [
        {
          id: 'm1',
          title: 'Hello team',
          messageType: 1,
          senderId: 'u1',
          senderName: 'Alice',
          canReply: true,
          creationTime: '2026-01-01T00:00:00Z',
          isRead: false,
          replyCount: 2,
          isImportant: false,
        },
        {
          id: 'm2',
          title: 'Announcement',
          messageType: 2,
          senderId: 'u2',
          senderName: 'Admin',
          canReply: false,
          creationTime: '2026-01-02T00:00:00Z',
          isRead: true,
          replyCount: 0,
          isImportant: true,
        },
      ],
      totalCount: 2,
      pageIndex: 1,
      pageSize: 20,
    })),
    delete: vi.fn(async () => undefined),
    batchDelete: vi.fn(async () => 2),
    getStatistics: vi.fn(async () => ({
      totalMessages: 10,
      sentMessages: 8,
      draftMessages: 2,
      publicMessages: 5,
      privateMessages: 5,
      totalReplies: 3,
      activeSenders: 2,
      importantMessages: 1,
    })),
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('chat-bridge', () => {
  it('exposes sessions / messages sub-contracts', () => {
    const bridge = createChatBridge({ chatAdminApi: mockChatAdminApi() as never })
    expect(typeof bridge.sessions.fetch).toBe('function')
    expect(typeof bridge.messages.fetch).toBe('function')
  })

  // ---- messages sub-contract ----

  it('messages.fetch calls chatAdminApi.query and returns paged items', async () => {
    const chatAdminApi = mockChatAdminApi()
    const bridge = createChatBridge({ chatAdminApi: chatAdminApi as never })
    const result = await bridge.messages.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(chatAdminApi.query).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('messages.fetch passes filters through including sessionId', async () => {
    const chatAdminApi = mockChatAdminApi()
    const bridge = createChatBridge({ chatAdminApi: chatAdminApi as never })
    await bridge.messages.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: { sessionId: 'sess1' } })
    expect(chatAdminApi.query).toHaveBeenCalledWith(
      expect.objectContaining({ sessionId: 'sess1' }),
    )
  })

  it('messages.delete calls chatAdminApi.batchDelete', async () => {
    const chatAdminApi = mockChatAdminApi()
    const bridge = createChatBridge({ chatAdminApi: chatAdminApi as never })
    await bridge.messages.delete(['m1', 'm2'])
    expect(chatAdminApi.batchDelete).toHaveBeenCalledWith(['m1', 'm2'])
  })

  it('messages.create is a backend-gap reject stub', async () => {
    const bridge = createChatBridge({ chatAdminApi: mockChatAdminApi() as never })
    await expect(bridge.messages.create({})).rejects.toThrow()
  })

  it('messages.update is a backend-gap reject stub', async () => {
    const bridge = createChatBridge({ chatAdminApi: mockChatAdminApi() as never })
    await expect(bridge.messages.update('id', {})).rejects.toThrow()
  })

  it('messages.fetchBySession calls query filtered by sessionId', async () => {
    const chatAdminApi = mockChatAdminApi()
    const bridge = createChatBridge({ chatAdminApi: chatAdminApi as never })
    await bridge.messages.fetchBySession('sess1', { pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(chatAdminApi.query).toHaveBeenCalledWith(
      expect.objectContaining({ sessionId: 'sess1' }),
    )
  })

  // ---- sessions sub-contract (real CRUD with chatSessionApi injected) ----

  it('sessions.fetch calls sessionApi.getList and returns paged items', async () => {
    const sessionApi = mockChatSessionApi()
    const bridge = createChatBridge({
      chatAdminApi: mockChatAdminApi() as never,
      chatSessionApi: sessionApi as never,
    })
    const result = await bridge.sessions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(sessionApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
    expect(result.items[0].id).toBe('s1')
    expect(result.totalCount).toBe(1)
  })

  it('sessions.fetch forwards status/keyword/participantId filters', async () => {
    const sessionApi = mockChatSessionApi()
    const bridge = createChatBridge({
      chatAdminApi: mockChatAdminApi() as never,
      chatSessionApi: sessionApi as never,
    })
    await bridge.sessions.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { status: 2, keyword: 'launch', participantId: 'u1' },
    })
    expect(sessionApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ status: 2, keyword: 'launch', participantId: 'u1' }),
    )
  })

  it('sessions.create calls sessionApi.create and returns row', async () => {
    const sessionApi = mockChatSessionApi()
    const bridge = createChatBridge({
      chatAdminApi: mockChatAdminApi() as never,
      chatSessionApi: sessionApi as never,
    })
    const row = await bridge.sessions.create({ title: 'New session', status: 1 })
    expect(sessionApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'New session', status: 1 }),
    )
    expect(row.id).toBe('s-new')
  })

  it('sessions.update calls sessionApi.update with id and payload', async () => {
    const sessionApi = mockChatSessionApi()
    const bridge = createChatBridge({
      chatAdminApi: mockChatAdminApi() as never,
      chatSessionApi: sessionApi as never,
    })
    await bridge.sessions.update('s1', { title: 'Renamed', status: 2 })
    expect(sessionApi.update).toHaveBeenCalledWith('s1', expect.objectContaining({ title: 'Renamed', status: 2 }))
  })

  it('sessions.delete single id calls sessionApi.delete, batch calls deleteBatch', async () => {
    const sessionApi = mockChatSessionApi()
    const bridge = createChatBridge({
      chatAdminApi: mockChatAdminApi() as never,
      chatSessionApi: sessionApi as never,
    })
    await bridge.sessions.delete(['s1'])
    expect(sessionApi.delete).toHaveBeenCalledWith('s1')
    await bridge.sessions.delete(['s1', 's2'])
    expect(sessionApi.deleteBatch).toHaveBeenCalledWith(['s1', 's2'])
  })

  it('sessions.fetch rejects when no HttpClient / sessionApi provided', async () => {
    // chatAdminApi injected but no sessionApi → falls through to the
    // no-HttpClient fallback branch inside the sessions contract.
    const bridge = createChatBridge({ chatAdminApi: mockChatAdminApi() as never })
    await expect(
      bridge.sessions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} }),
    ).rejects.toThrow()
  })

  // ---- no-deps fallback ----

  it('bridge with no deps returns stub contracts that reject on call', async () => {
    const bridge = createChatBridge()
    await expect(bridge.messages.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
    await expect(bridge.sessions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
  })
})
