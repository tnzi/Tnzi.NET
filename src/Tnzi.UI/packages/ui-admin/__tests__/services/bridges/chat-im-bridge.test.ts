import { describe, it, expect, vi } from 'vitest'
import { createChatImBridge } from '../../../src/services/bridges/chat-im-bridge'

function mockApi() {
  return {
    getConversations: vi.fn(async () => ({ success: true, code: 200, data: [{ id: 'c1', type: 1, title: 'Alice', unreadCount: 2, isMuted: false, memberCount: 2 }] })),
    getMessages: vi.fn(async () => ({ success: true, code: 200, data: { messages: [{ id: 'm1', conversationId: 'c1', contentType: 1, content: 'hi', sentAt: '2026-01-01T00:00:00Z' }], hasMore: false } })),
    sendMessage: vi.fn(async () => ({ success: true, code: 200, data: { id: 'm2', conversationId: 'c1', contentType: 1, content: 'yo', sentAt: '2026-01-01T00:00:01Z' } })),
    getUnreadCount: vi.fn(async () => ({ success: true, code: 200, data: 5 })),
    markRead: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('chat-im-bridge', () => {
  it('listConversations unwraps data', async () => {
    const api = mockApi(); const b = createChatImBridge({ api: api as never })
    const list = await b.listConversations()
    expect(api.getConversations).toHaveBeenCalled()
    expect(list).toHaveLength(1); expect(list[0].id).toBe('c1')
  })
  it('getMessages unwraps thread', async () => {
    const api = mockApi(); const b = createChatImBridge({ api: api as never })
    const t = await b.getMessages('c1', {})
    expect(t.messages[0].content).toBe('hi'); expect(t.hasMore).toBe(false)
  })
  it('getUnreadCount unwraps number', async () => {
    const api = mockApi(); const b = createChatImBridge({ api: api as never })
    expect(await b.getUnreadCount()).toBe(5)
  })
})
