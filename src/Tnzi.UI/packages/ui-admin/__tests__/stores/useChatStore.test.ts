import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { MessageContentType } from '@tnzi/core/services/chat'
import { useChatStore } from '../../src/stores/useChatStore'

// Enum DTO fields are now PascalCase member-name strings; SignalR NewMessagePayload
// keeps a numeric contentType (backend `(int)` cast), so applyIncomingMessage
// payloads below intentionally stay numeric.

function mockBridge() {
  return {
    listConversations: vi.fn(async () => [
      { id: 'c1', type: 'Direct', title: 'Alice', unreadCount: 2, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-02T00:00:00Z' },
      { id: 'c2', type: 'Direct', title: 'Bob', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-03T00:00:00Z' },
    ]),
    getMessages: vi.fn(async () => ({ messages: [{ id: 'm1', conversationId: 'c1', contentType: MessageContentType.Text, content: 'hi', sentAt: '2026-01-01T00:00:00Z' }], hasMore: false })),
    sendMessage: vi.fn(async (id: string, data: { contentType: MessageContentType; content: string }) => ({ id: 'm9', conversationId: id, contentType: data.contentType, content: data.content, sentAt: '2026-01-04T00:00:00Z' })),
    markRead: vi.fn(async () => {}),
    getUnreadCount: vi.fn(async () => 2),
  }
}

describe('useChatStore', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('fetchConversations populates + sorts by lastMessageAt desc', async () => {
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations()
    expect(store.conversations).toHaveLength(2)
    expect(store.sortedConversations[0].id).toBe('c2') // newer
    expect(store.totalUnread).toBe(2)
  })

  it('openConversation loads messages + marks read (clears that unread)', async () => {
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations()
    await store.openConversation('c1')
    expect(store.activeId).toBe('c1')
    expect(store.messagesByConv['c1']).toHaveLength(1)
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(0)
    expect(store.totalUnread).toBe(0)
  })

  it('sendText appends the returned message to the active thread', async () => {
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations(); await store.openConversation('c1')
    await store.sendText('c1', 'yo')
    const thread = store.messagesByConv['c1']
    expect(thread[thread.length - 1].content).toBe('yo')
  })

  it('sendText keeps a failed placeholder in the thread when the send is rejected (403)', async () => {
    // A mid-session chat.use revocation makes the send 403. Instead of a toast,
    // the attempted message stays inline flagged failed so the window can render
    // the WeChat-style retry marker + reason.
    const bridge = mockBridge()
    bridge.sendMessage = vi.fn(async () => { throw new Error('You no longer have chat access') })
    const store = useChatStore(); store.init(bridge as never)
    await store.fetchConversations(); await store.openConversation('c1')

    await expect(store.sendText('c1', 'blocked?')).rejects.toThrow('chat access')

    const thread = store.messagesByConv['c1']
    const last = thread[thread.length - 1]
    expect(last.content).toBe('blocked?')
    expect(last.failed).toBe(true)
    expect(last.failReason).toBe('You no longer have chat access')
    expect(last.clientId).toBeTypeOf('number')
    // The failed send must NOT become the conversation's last-message preview.
    expect(store.conversations.find(c => c.id === 'c1')!.lastMessagePreview).toBeUndefined()
  })

  it('resendMessage drops the failed placeholder and re-sends; a success clears the failed state', async () => {
    let attempt = 0
    const bridge = mockBridge()
    bridge.sendMessage = vi.fn(async (id: string, data: { contentType: MessageContentType; content: string }) => {
      attempt += 1
      if (attempt === 1) throw new Error('403')
      return { id: 'm9', conversationId: id, contentType: data.contentType, content: data.content, sentAt: '2026-01-05T00:00:00Z' }
    })
    const store = useChatStore(); store.init(bridge as never)
    await store.fetchConversations(); await store.openConversation('c1')

    await expect(store.sendText('c1', 'retry me')).rejects.toThrow('403')
    const failed = store.messagesByConv['c1'].find(m => m.failed)!
    expect(failed).toBeTruthy()

    await store.resendMessage('c1', failed.clientId!)

    const thread = store.messagesByConv['c1']
    // No failed placeholder remains; the sent message is the real one.
    expect(thread.some(m => m.failed)).toBe(false)
    expect(thread[thread.length - 1].id).toBe('m9')
    expect(thread[thread.length - 1].content).toBe('retry me')
  })

  it('applyIncomingMessage bumps unread when not the active conversation', async () => {
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations() // c1 unread=2, none active
    store.applyIncomingMessage({ conversationId: 'c1', messageId: 'mX', senderId: 'other', contentType: 1, preview: 'ping' }, 'me')
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(3)
    expect(store.conversations.find(c => c.id === 'c1')!.lastMessagePreview).toBe('ping')
  })

  it('applyIncomingMessage treats system/broadcast (senderId=null) as from-other → bumps unread', async () => {
    // Broadcast/system messages carry senderId=null; they must still raise the badge.
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations() // c1 unread=2, none active
    store.applyIncomingMessage({ conversationId: 'c1', messageId: 'mB', senderId: null, contentType: 4, preview: '[System] notice' }, 'me')
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(3)
  })

  it('applyIncomingMessage bumps unread for the active conversation when the window is closed', async () => {
    // Regression: activeId survives closing the chat window. A message for that
    // conversation must still raise the badge while the window is not visible.
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations()
    await store.openConversation('c1') // activeId=c1, unread cleared to 0
    expect(store.windowVisible).toBe(false) // window never marked visible
    store.applyIncomingMessage({ conversationId: 'c1', messageId: 'mY', senderId: 'other', contentType: 1, preview: 'hey' }, 'me')
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(1)
  })

  it('applyIncomingMessage does NOT bump unread for the active conversation while the window is visible', async () => {
    const store = useChatStore(); store.init(mockBridge() as never)
    await store.fetchConversations()
    await store.openConversation('c1')
    store.setWindowVisible(true)
    store.applyIncomingMessage({ conversationId: 'c1', messageId: 'mZ', senderId: 'other', contentType: 1, preview: 'hey' }, 'me')
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(0)
  })
})

describe('useChatStore — new actions (U6)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  function makeBridgeU6() {
    return {
      listConversations: vi.fn(async () => [
        { id: 'c1', type: 'Direct', title: 'Alice', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '' },
      ]),
      getMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
      sendMessage: vi.fn(async (_id: string, data: { contentType: number; fileId?: string; fileName?: string; fileSize?: number }) => ({
        id: 'm1',
        conversationId: 'c-direct',
        contentType: data.contentType,
        content: '',
        fileId: data.fileId,
        fileName: data.fileName,
        sentAt: '2026-01-01T00:00:00Z',
      })),
      markRead: vi.fn(async () => {}),
      getUnreadCount: vi.fn(async () => 0),
      searchContacts: vi.fn(async () => [{ userId: 'u1', name: 'Alice' }]),
      getOrCreateDirect: vi.fn(async () => ({ id: 'c-direct', type: 'Direct', title: 'Alice', memberCount: 2, members: [] })),
      createGroup: vi.fn(async () => ({ id: 'c-group', type: 'Group', title: 'My Group', memberCount: 3, members: [] })),
      getConversation: vi.fn(async () => ({ id: 'g1', type: 'Group', title: 'Group', memberCount: 2, members: [] })),
      addMembers: vi.fn(async () => {}),
      removeMember: vi.fn(async () => {}),
      renameGroup: vi.fn(async () => {}),
      dissolveGroup: vi.fn(async () => {}),
      leaveGroup: vi.fn(async () => {}),
      mute: vi.fn(async () => {}),
      deleteMessage: vi.fn(async () => {}),
    }
  }

  it('searchContacts delegates to bridge', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    const result = await store.searchContacts('Alice')
    expect(bridge.searchContacts).toHaveBeenCalledWith('Alice')
    expect(result).toHaveLength(1)
    expect(result[0].userId).toBe('u1')
  })

  it('startDirect calls getOrCreateDirect, fetchConversations, openConversation', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    const id = await store.startDirect('u1')
    expect(bridge.getOrCreateDirect).toHaveBeenCalledWith('u1')
    expect(bridge.listConversations).toHaveBeenCalled()
    expect(store.activeId).toBe('c-direct')
    expect(id).toBe('c-direct')
  })

  it('createGroup calls bridge.createGroup, fetchConversations, openConversation', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    const id = await store.createGroup('Team Chat', ['u1', 'u2'])
    expect(bridge.createGroup).toHaveBeenCalledWith({ title: 'Team Chat', memberIds: ['u1', 'u2'] })
    expect(bridge.listConversations).toHaveBeenCalled()
    expect(store.activeId).toBe('c-group')
    expect(id).toBe('c-group')
  })

  it('sendMedia calls sendMessage with Image contentType and appends message', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    // Initialize conversation message list
    store.messagesByConv['c1'] = []
    await store.sendMedia('c1', { contentType: MessageContentType.Image, fileId: 'f1', fileName: 'photo.png', fileSize: 1024 })
    expect(bridge.sendMessage).toHaveBeenCalledWith('c1', {
      contentType: MessageContentType.Image,
      fileId: 'f1',
      fileName: 'photo.png',
      fileSize: 1024,
    })
    expect(store.messagesByConv['c1']).toHaveLength(1)
  })

  it('dissolveGroup clears activeId when it matches', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    await store.fetchConversations()
    await store.openConversation('c1')
    expect(store.activeId).toBe('c1')
    bridge.listConversations.mockResolvedValue([])
    await store.dissolveGroup('c1')
    expect(bridge.dissolveGroup).toHaveBeenCalledWith('c1')
    expect(store.activeId).toBeNull()
  })

  it('leaveGroup clears activeId when it matches', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    await store.fetchConversations()
    await store.openConversation('c1')
    bridge.listConversations.mockResolvedValue([])
    await store.leaveGroup('c1')
    expect(bridge.leaveGroup).toHaveBeenCalledWith('c1')
    expect(store.activeId).toBeNull()
  })

  it('getConversationDetail delegates to bridge.getConversation', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    const detail = await store.getConversationDetail('g1')
    expect(bridge.getConversation).toHaveBeenCalledWith('g1')
    expect(detail.id).toBe('g1')
  })

  it('addMembers calls bridge.addMembers and refreshes conversations', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    await store.addMembers('g1', ['u2', 'u3'])
    expect(bridge.addMembers).toHaveBeenCalledWith('g1', ['u2', 'u3'])
    expect(bridge.listConversations).toHaveBeenCalled()
  })

  it('removeMember calls bridge.removeMember and refreshes conversations', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    await store.removeMember('g1', 'u2')
    expect(bridge.removeMember).toHaveBeenCalledWith('g1', 'u2')
    expect(bridge.listConversations).toHaveBeenCalled()
  })

  it('renameGroup calls bridge.renameGroup and refreshes conversations', async () => {
    const bridge = makeBridgeU6()
    const store = useChatStore()
    store.init(bridge as never)
    await store.renameGroup('g1', 'New Name')
    expect(bridge.renameGroup).toHaveBeenCalledWith('g1', 'New Name')
    expect(bridge.listConversations).toHaveBeenCalled()
  })
})

describe('useChatStore — presence + sorting (U7)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  function makePresenceBridge() {
    return {
      listConversations: vi.fn(async () => [
        { id: 'd1', type: 'Direct', title: 'Alice', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-01T00:00:00Z', peerUserId: 'u-alice', peerStatus: undefined, isSticky: false },
        { id: 'd2', type: 'Direct', title: 'Bob',   unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-03T00:00:00Z', peerUserId: 'u-bob',   peerStatus: undefined, isSticky: false },
        { id: 'd3', type: 'Direct', title: 'Carol', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-02T00:00:00Z', peerUserId: 'u-carol', peerStatus: undefined, isSticky: true  },
      ]),
      getMessages:  vi.fn(async () => ({ messages: [], hasMore: false })),
      sendMessage:  vi.fn(),
      markRead:     vi.fn(async () => {}),
      getUnreadCount: vi.fn(async () => 0),
    }
  }

  it('applyPresenceChange updates presenceByUser[userId]', async () => {
    const { UserPresenceStatus } = await import('@tnzi/core/services/chat')
    const store = useChatStore()
    store.init(makePresenceBridge() as never)

    store.applyPresenceChange({ userId: 'u-alice', status: UserPresenceStatus.Away, lastSeenAt: null })

    expect(store.presenceByUser['u-alice']).toEqual({ status: UserPresenceStatus.Away, lastSeenAt: null })
  })

  it('applyPresenceChange syncs peerStatus on the matching direct conversation', async () => {
    const { UserPresenceStatus } = await import('@tnzi/core/services/chat')
    const store = useChatStore()
    store.init(makePresenceBridge() as never)
    await store.fetchConversations()

    store.applyPresenceChange({ userId: 'u-bob', status: UserPresenceStatus.Busy, lastSeenAt: null })

    const conv = store.conversations.find(c => c.id === 'd2')
    expect(conv?.peerStatus).toBe(UserPresenceStatus.Busy)
    // Other conversations are not affected
    const other = store.conversations.find(c => c.id === 'd1')
    expect(other?.peerStatus).toBeUndefined()
  })

  it('sortedConversations puts sticky conversations before non-sticky ones', async () => {
    const store = useChatStore()
    store.init(makePresenceBridge() as never)
    await store.fetchConversations()

    const sorted = store.sortedConversations
    expect(sorted[0].id).toBe('d3')  // Carol — isSticky = true
    // Among non-sticky: d2 (Jan 3) should precede d1 (Jan 1)
    expect(sorted[1].id).toBe('d2')
    expect(sorted[2].id).toBe('d1')
  })

  it('sortedConversations sorts two sticky conversations by lastMessageAt desc', async () => {
    const bridge = {
      listConversations: vi.fn(async () => [
        { id: 's1', type: 'Direct', title: 'S1', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-05T00:00:00Z', isSticky: true },
        { id: 's2', type: 'Direct', title: 'S2', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-07T00:00:00Z', isSticky: true },
        { id: 'n1', type: 'Direct', title: 'N1', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '2026-01-10T00:00:00Z', isSticky: false },
      ]),
      getMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
      sendMessage: vi.fn(),
      markRead: vi.fn(async () => {}),
      getUnreadCount: vi.fn(async () => 0),
    }
    const store = useChatStore()
    store.init(bridge as never)
    await store.fetchConversations()

    const sorted = store.sortedConversations
    expect(sorted[0].id).toBe('s2')  // sticky, newer
    expect(sorted[1].id).toBe('s1')  // sticky, older
    expect(sorted[2].id).toBe('n1')  // non-sticky (but newest overall — still last)
  })
})
