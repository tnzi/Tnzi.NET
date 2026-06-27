import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

const handlers: Record<string, (p: unknown) => void> = {}
const fakeClient = {
  start: vi.fn(async () => {}), stop: vi.fn(async () => {}), isConnected: () => true,
  on: vi.fn((e: string, h: (p: unknown) => void) => { handlers[e] = h }),
  off: vi.fn(),
}
vi.mock('@tnzi/core/services/chat', async (orig) => ({ ...(await orig() as object), createChatSignalRClient: () => fakeClient }))

import { useChatRealtime } from '../../src/headless/useChatRealtime'
import { useChatStore } from '../../src/stores/useChatStore'

beforeEach(() => { setActivePinia(createPinia()); for (const k in handlers) delete handlers[k]; fakeClient.on.mockClear() })

describe('useChatRealtime', () => {
  it('start() connects and subscribes to the four Chat events', async () => {
    const store = useChatStore()
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me' })
    await rt.start()
    expect(fakeClient.start).toHaveBeenCalled()
    expect(Object.keys(handlers).sort()).toEqual(['Chat.ConversationChanged', 'Chat.MessageRead', 'Chat.NewMessage', 'Chat.PresenceChanged'])
  })

  it('incoming Chat.NewMessage is applied to the store + fires onNewMessage', async () => {
    const store = useChatStore()
    store.conversations = [{ id: 'c1', type: 1, title: 'A', unreadCount: 0, isMuted: false, memberCount: 2 } as never]
    const onNew = vi.fn()
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me', onNewMessage: onNew })
    await rt.start()
    handlers['Chat.NewMessage']({ conversationId: 'c1', messageId: 'm1', senderId: 'other', contentType: 1, preview: 'ping' })
    expect(store.conversations[0].unreadCount).toBe(1)
    expect(onNew).toHaveBeenCalled()
  })

  it('system/broadcast Chat.NewMessage (senderId=null) bumps unread + fires onNewMessage', async () => {
    const store = useChatStore()
    store.conversations = [{ id: 'sys', type: 3, title: 'System', unreadCount: 0, isMuted: false, memberCount: 1 } as never]
    const onNew = vi.fn()
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me', onNewMessage: onNew })
    await rt.start()
    handlers['Chat.NewMessage']({ conversationId: 'sys', messageId: 'mB', senderId: null, contentType: 4, preview: '[System] broadcast' })
    expect(store.conversations[0].unreadCount).toBe(1)
    expect(onNew).toHaveBeenCalled()
  })
})
