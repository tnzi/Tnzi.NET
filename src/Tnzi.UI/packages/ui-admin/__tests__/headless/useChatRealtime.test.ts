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
  it('start() connects and subscribes to the five Chat events', async () => {
    const store = useChatStore()
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me' })
    await rt.start()
    expect(fakeClient.start).toHaveBeenCalled()
    expect(Object.keys(handlers).sort()).toEqual(['Chat.ConversationChanged', 'Chat.MessageRead', 'Chat.MessageRecalled', 'Chat.NewMessage', 'Chat.PresenceChanged'])
  })

  it('incoming Chat.NewMessage is applied to the store + fires onNewMessage', async () => {
    const store = useChatStore()
    store.conversations = [{ id: 'c1', type: 'Direct', title: 'A', unreadCount: 0, isMuted: false, memberCount: 2 } as never]
    const onNew = vi.fn()
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me', onNewMessage: onNew })
    await rt.start()
    handlers['Chat.NewMessage']({ conversationId: 'c1', messageId: 'm1', senderId: 'other', contentType: 1, preview: 'ping' })
    expect(store.conversations[0].unreadCount).toBe(1)
    expect(onNew).toHaveBeenCalled()
  })

  it('Chat.MessageRecalled drops the message from the open thread and takes the new preview', async () => {
    const store = useChatStore()
    store.conversations = [{ id: 'c1', type: 'Group', title: 'G', unreadCount: 0, isMuted: false, memberCount: 3, lastMessagePreview: 'my password is hunter2' } as never]
    store.messagesByConv = {
      c1: [
        { id: 'm1', conversationId: 'c1', content: 'hello' } as never,
        { id: 'm2', conversationId: 'c1', content: 'my password is hunter2' } as never,
      ],
    }
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me' })
    await rt.start()
    handlers['Chat.MessageRecalled']({ conversationId: 'c1', messageId: 'm2', preview: '[Message recalled]' })
    expect(store.messagesByConv.c1.map(m => m.id)).toEqual(['m1'])
    expect(store.conversations[0].lastMessagePreview).toBe('[Message recalled]')
  })

  it('Chat.MessageRecalled for an older message keeps the preview', async () => {
    const store = useChatStore()
    store.conversations = [{ id: 'c1', type: 'Group', title: 'G', unreadCount: 0, isMuted: false, memberCount: 3, lastMessagePreview: 'latest' } as never]
    store.messagesByConv = { c1: [{ id: 'm1', conversationId: 'c1', content: 'older' } as never, { id: 'm2', conversationId: 'c1', content: 'latest' } as never] }
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me' })
    await rt.start()
    handlers['Chat.MessageRecalled']({ conversationId: 'c1', messageId: 'm1', preview: null })
    expect(store.messagesByConv.c1.map(m => m.id)).toEqual(['m2'])
    expect(store.conversations[0].lastMessagePreview).toBe('latest')
  })

  it('system/broadcast Chat.NewMessage (senderId=null) bumps unread + fires onNewMessage', async () => {
    const store = useChatStore()
    store.conversations = [{ id: 'sys', type: 'System', title: 'System', unreadCount: 0, isMuted: false, memberCount: 1 } as never]
    const onNew = vi.fn()
    const rt = useChatRealtime({ client: {} as never, store, hubUrl: '/hubs/chat', getToken: () => 't', getUserId: () => 'me', onNewMessage: onNew })
    await rt.start()
    handlers['Chat.NewMessage']({ conversationId: 'sys', messageId: 'mB', senderId: null, contentType: 4, preview: '[System] broadcast' })
    expect(store.conversations[0].unreadCount).toBe(1)
    expect(onNew).toHaveBeenCalled()
  })
})
