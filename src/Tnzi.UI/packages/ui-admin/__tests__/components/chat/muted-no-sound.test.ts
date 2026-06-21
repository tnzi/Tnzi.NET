/**
 * Muted-no-sound integration test
 *
 * Verifies that when a new message arrives for a MUTED conversation, the
 * notification sound is NOT played, while it IS played for an unmuted one.
 *
 * The sound guard lives in TChatHost's `onNewMessage` callback (see
 * src/components/chat/TChatHost.vue). We mount TChatHost, capture the
 * `onNewMessage` handler that was passed to useChatRealtime, and call it
 * directly with muted/unmuted payloads — this is the real execution path.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { NewMessagePayload } from '@tnzi/core/services/chat'

// ── Shared mutable captures ────────────────────────────────────────────────
let capturedOnNewMessage: ((p: NewMessagePayload) => void) | undefined
const mockSoundPlay = vi.fn()

// ── Fake bridge (two conversations: one muted, one not) ─────────────────────
const fakeBridge = {
  listConversations: vi.fn().mockResolvedValue([
    { id: 'c-muted',   type: 1, title: 'Alice', unreadCount: 0, isMuted: true,  memberCount: 2, lastMessageAt: '' },
    { id: 'c-unmuted', type: 1, title: 'Bob',   unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '' },
  ]),
  getUnreadCount:  vi.fn().mockResolvedValue(0),
  getMessages:     vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
  markRead:        vi.fn().mockResolvedValue(undefined),
  sendMessage:     vi.fn(),
  getOrCreateDirect: vi.fn(),
  getConversation: vi.fn(),
  createGroup:     vi.fn(),
  addMembers:      vi.fn(),
  removeMember:    vi.fn(),
  renameGroup:     vi.fn(),
  dissolveGroup:   vi.fn(),
  leaveGroup:      vi.fn(),
  searchContacts:  vi.fn().mockResolvedValue([]),
  mute:            vi.fn(),
  deleteMessage:   vi.fn(),
  setStatus:       vi.fn(),
  getMyStatus:     vi.fn().mockResolvedValue(1),
  getPresence:     vi.fn().mockResolvedValue([]),
}

// ── Module mocks ───────────────────────────────────────────────────────────
vi.mock('../../../src/services/bridges/chat-im-bridge', () => ({
  createChatImBridge: () => fakeBridge,
}))

vi.mock('../../../src/headless/useChatRealtime', () => ({
  useChatRealtime: (opts: { onNewMessage?: (p: NewMessagePayload) => void }) => {
    // Capture the callback TChatHost provides so tests can invoke it directly.
    capturedOnNewMessage = opts.onNewMessage
    return {
      start: vi.fn().mockResolvedValue(undefined),
      stop:  vi.fn().mockResolvedValue(undefined),
    }
  },
}))

vi.mock('../../../src/headless/useNotificationSound', () => ({
  useNotificationSound: () => ({ play: mockSoundPlay, setEnabled: vi.fn(), enabled: { value: true } }),
}))

vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({ isSm: { value: false } }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: (required?: boolean) => {
    if (required === false) return {}
    return {}
  },
  TNZI_ADMIN_CLIENT_KEY: Symbol('tnzi-admin-client'),
}))

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

// ── Stubs ──────────────────────────────────────────────────────────────────
const globalConfig = {
  stubs: {
    TChatLauncher: { template: '<button />', props: ['unreadCount'], emits: ['open'] },
    TChatWindow:   { template: '<div />', props: ['show'], emits: ['update:show'] },
  },
}

// ── Tests ──────────────────────────────────────────────────────────────────
describe('TChatHost — muted conversations do not play sound', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    capturedOnNewMessage = undefined
    fakeBridge.listConversations.mockResolvedValue([
      { id: 'c-muted',   type: 1, title: 'Alice', unreadCount: 0, isMuted: true,  memberCount: 2, lastMessageAt: '' },
      { id: 'c-unmuted', type: 1, title: 'Bob',   unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '' },
    ])
  })

  async function mountAndWaitForInit() {
    const TChatHost = (await import('../../../src/components/chat/TChatHost.vue')).default
    const wrapper = mount(TChatHost, {
      global: { ...globalConfig, plugins: [createPinia()] },
    })
    // Wait for onMounted (fetchConversations + realtime.start)
    await vi.waitFor(() => expect(fakeBridge.listConversations).toHaveBeenCalled())
    return wrapper
  }

  it('does NOT play sound for a message in a muted conversation', async () => {
    const wrapper = await mountAndWaitForInit()
    expect(capturedOnNewMessage).toBeDefined()

    const payload: NewMessagePayload = {
      conversationId: 'c-muted',
      messageId: 'm1',
      senderId: 'other-user',
      contentType: 1,
      preview: 'hello',
    }
    capturedOnNewMessage!(payload)
    await wrapper.vm.$nextTick()

    expect(mockSoundPlay).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('DOES play sound for a message in an unmuted conversation', async () => {
    const wrapper = await mountAndWaitForInit()
    expect(capturedOnNewMessage).toBeDefined()

    const payload: NewMessagePayload = {
      conversationId: 'c-unmuted',
      messageId: 'm2',
      senderId: 'other-user',
      contentType: 1,
      preview: 'ping',
    }
    capturedOnNewMessage!(payload)
    await wrapper.vm.$nextTick()

    expect(mockSoundPlay).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
})