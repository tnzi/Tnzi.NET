/**
 * Closed-window notification regression test
 *
 * Bug: activeId survives closing the chat window, and the "is the user viewing
 * this thread" checks only compared conversation ids. So after opening a
 * conversation once and closing the window, its incoming messages were treated
 * as "on screen": silently marked read on the server, no badge, no sound.
 *
 * The fix gates both checks on store.windowVisible (mirrored from TChatHost's
 * `show` ref). This test drives the real open -> close -> message flow through
 * TChatHost's captured onNewMessage callback.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { NewMessagePayload } from '@tnzi/core/services/chat'
import { useChatStore } from '../../../src/stores/useChatStore'
import { useAdminDesktopStore } from '../../../src/stores/useAdminDesktopStore'
import { useAdminThemeStore } from '../../../src/stores/useAdminThemeStore'
import TChatHost from '../../../src/components/chat/TChatHost.vue'

// TChatHost is imported statically on purpose: the module graph behind it is
// large, and a dynamic import inside a test paid its whole transform under that
// test's timeout, which a machine busy with a parallel build could exceed.
// Collection has no such timeout. The mounted host settles in a few
// microtasks; the generous wait only absorbs a starved event loop.
const MOUNT_SETTLE_TIMEOUT_MS = 10_000

// ── Shared mutable captures ────────────────────────────────────────────────
let capturedOnNewMessage: ((p: NewMessagePayload) => void) | undefined
const mockPlayNotification = vi.fn()
const mockPlayMessage = vi.fn()

// ── Fake bridge (one unmuted conversation) ─────────────────────────────────
const fakeBridge = {
  // enabled:true = this user holds chat.use, so TChatHost proceeds past the
  // deny-by-default gate and fetches conversations / starts realtime.
  getConfig: vi.fn().mockResolvedValue({ enabled: true, enablePresence: false }),
  listConversations: vi.fn().mockResolvedValue([
    { id: 'c1', type: 'Direct', title: 'Alice', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '' },
  ]),
  getUnreadCount:  vi.fn().mockResolvedValue(0),
  getMessages:     vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
  markRead:        vi.fn().mockResolvedValue(undefined),
  sendMessage:     vi.fn(),
  searchContacts:  vi.fn().mockResolvedValue([]),
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
    capturedOnNewMessage = opts.onNewMessage
    return {
      start: vi.fn().mockResolvedValue(undefined),
      stop:  vi.fn().mockResolvedValue(undefined),
    }
  },
}))

vi.mock('../../../src/headless/useChatSound', () => ({
  useChatSound: () => ({
    configure: vi.fn(),
    playNotification: mockPlayNotification,
    playMessage: mockPlayMessage,
    preview: vi.fn(),
  }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({}),
  TNZI_ADMIN_CLIENT_KEY: Symbol('tnzi-admin-client'),
}))

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

// ── Named stubs (so tests can emit open / update:show on them) ─────────────
const LauncherStub = { name: 'TChatLauncher', template: '<button />', props: ['unreadCount', 'effect', 'attention'], emits: ['open'] }
const WindowStub = { name: 'TChatWindow', template: '<div />', props: ['show'], emits: ['update:show'] }

const globalStubs = { TChatLauncher: LauncherStub, TChatWindow: WindowStub }

const payload = (id: string): NewMessagePayload => ({
  conversationId: 'c1',
  messageId: id,
  senderId: 'other-user',
  contentType: 1,
  preview: 'hello',
})

// ── Tests ──────────────────────────────────────────────────────────────────
describe('TChatHost - closed window still notifies for the last active conversation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    capturedOnNewMessage = undefined
    fakeBridge.listConversations.mockResolvedValue([
      { id: 'c1', type: 'Direct', title: 'Alice', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '' },
    ])
  })

  async function mountHost() {
    const pinia = createPinia()
    setActivePinia(pinia)
    const wrapper = mount(TChatHost, { global: { stubs: globalStubs, plugins: [pinia] } })
    await vi.waitFor(() => expect(fakeBridge.listConversations).toHaveBeenCalled(), { timeout: MOUNT_SETTLE_TIMEOUT_MS })
    const store = useChatStore()
    return { wrapper, store }
  }

  it('plays the notification tone (and does NOT markRead) after the window was opened once and closed', async () => {
    const { wrapper, store } = await mountHost()
    expect(capturedOnNewMessage).toBeDefined()

    // Open the window, open a conversation, then close the window.
    wrapper.findComponent(LauncherStub).vm.$emit('open')
    await wrapper.vm.$nextTick()
    expect(store.windowVisible).toBe(true)
    await store.openConversation('c1')
    wrapper.findComponent(WindowStub).vm.$emit('update:show', false)
    await wrapper.vm.$nextTick()
    expect(store.windowVisible).toBe(false)

    const readCallsBefore = fakeBridge.markRead.mock.calls.length
    capturedOnNewMessage!(payload('m1'))
    await wrapper.vm.$nextTick()

    // Closed window → the attention tone, not the in-conversation one.
    expect(mockPlayNotification).toHaveBeenCalledTimes(1)
    expect(mockPlayMessage).not.toHaveBeenCalled()
    expect(fakeBridge.markRead.mock.calls.length).toBe(readCallsBefore)
    // ...and the launcher icon gets an attention bump (window is closed).
    expect(wrapper.findComponent(LauncherStub).props('attention')).toBe(1)
    wrapper.unmount()
  })

  it('plays the gentle in-conversation tone (and marks read) while the window is open on the active conversation', async () => {
    const { wrapper, store } = await mountHost()

    wrapper.findComponent(LauncherStub).vm.$emit('open')
    await wrapper.vm.$nextTick()
    await store.openConversation('c1')

    const readCallsBefore = fakeBridge.markRead.mock.calls.length
    capturedOnNewMessage!(payload('m2'))
    await wrapper.vm.$nextTick()

    // Active thread on screen → the subtle message tone, and it still marks read.
    expect(mockPlayMessage).toHaveBeenCalledTimes(1)
    expect(mockPlayNotification).not.toHaveBeenCalled()
    expect(fakeBridge.markRead.mock.calls.length).toBe(readCallsBefore + 1)
    // Window is open on this thread → no launcher attention bump.
    expect(wrapper.findComponent(LauncherStub).props('attention')).toBe(0)
    wrapper.unmount()
  })

  it('clears the active conversation unread when the window reopens', async () => {
    const { wrapper, store } = await mountHost()

    wrapper.findComponent(LauncherStub).vm.$emit('open')
    await wrapper.vm.$nextTick()
    await store.openConversation('c1')
    wrapper.findComponent(WindowStub).vm.$emit('update:show', false)
    await wrapper.vm.$nextTick()

    // A message arrives while closed: badge goes up (store path).
    store.applyIncomingMessage(payload('m3'), 'me')
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(1)

    const readCallsBefore = fakeBridge.markRead.mock.calls.length
    wrapper.findComponent(LauncherStub).vm.$emit('open')
    await wrapper.vm.$nextTick()

    expect(fakeBridge.markRead.mock.calls.length).toBe(readCallsBefore + 1)
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(0)
    wrapper.unmount()
  })
})

// ── Desktop layout: the chat window is a desktop window, not the modal ──────
describe('TChatHost - desktop layout: the chat window on screen counts as visible', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    capturedOnNewMessage = undefined
    fakeBridge.listConversations.mockResolvedValue([
      { id: 'c1', type: 'Direct', title: 'Alice', unreadCount: 0, isMuted: false, memberCount: 2, lastMessageAt: '' },
    ])
  })

  async function mountDesktopHost() {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAdminThemeStore().layoutMode = 'desktop'
    const wrapper = mount(TChatHost, { global: { stubs: globalStubs, plugins: [pinia] } })
    await vi.waitFor(() => expect(fakeBridge.listConversations).toHaveBeenCalled(), { timeout: MOUNT_SETTLE_TIMEOUT_MS })
    return { wrapper, store: useChatStore(), desktop: useAdminDesktopStore() }
  }

  function chatWindowId(desktop: ReturnType<typeof useAdminDesktopStore>): string {
    const win = desktop.windows.find(w => w.stack[w.stackIndex]?.panel === 'chat')
    expect(win).toBeDefined()
    return win!.id
  }

  it('marks the open thread read and plays the in-thread tone while its window is on screen', async () => {
    const { wrapper, store, desktop } = await mountDesktopHost()

    wrapper.findComponent(LauncherStub).vm.$emit('open')
    await wrapper.vm.$nextTick()
    chatWindowId(desktop)
    expect(store.windowVisible).toBe(true)
    await store.openConversation('c1')

    const readCallsBefore = fakeBridge.markRead.mock.calls.length
    store.applyIncomingMessage(payload('d1'), 'me')
    capturedOnNewMessage!(payload('d1'))
    await wrapper.vm.$nextTick()

    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(0)
    expect(fakeBridge.markRead.mock.calls.length).toBe(readCallsBefore + 1)
    expect(mockPlayMessage).toHaveBeenCalledTimes(1)
    expect(mockPlayNotification).not.toHaveBeenCalled()
    expect(wrapper.findComponent(LauncherStub).props('attention')).toBe(0)
    wrapper.unmount()
  })

  it('treats a minimised or closed chat window as not visible, and catches up on restore', async () => {
    const { wrapper, store, desktop } = await mountDesktopHost()

    wrapper.findComponent(LauncherStub).vm.$emit('open')
    await wrapper.vm.$nextTick()
    await store.openConversation('c1')
    const id = chatWindowId(desktop)

    desktop.minimize(id)
    await wrapper.vm.$nextTick()
    expect(store.windowVisible).toBe(false)

    const readCallsBefore = fakeBridge.markRead.mock.calls.length
    store.applyIncomingMessage(payload('d2'), 'me')
    capturedOnNewMessage!(payload('d2'))
    await wrapper.vm.$nextTick()
    expect(store.conversations.find(c => c.id === 'c1')!.unreadCount).toBe(1)
    expect(fakeBridge.markRead.mock.calls.length).toBe(readCallsBefore)
    expect(mockPlayNotification).toHaveBeenCalledTimes(1)
    expect(wrapper.findComponent(LauncherStub).props('attention')).toBe(1)

    // Bringing the window back lands on the thread with the message rendered,
    // so its unread clears right away (same as reopening the modal).
    desktop.focus(id)
    await wrapper.vm.$nextTick()
    expect(store.windowVisible).toBe(true)
    expect(fakeBridge.markRead.mock.calls.length).toBe(readCallsBefore + 1)

    desktop.close(id)
    await wrapper.vm.$nextTick()
    expect(store.windowVisible).toBe(false)
    wrapper.unmount()
  })

  it('counts a chat window restored with the desktop as on screen from the first render', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAdminThemeStore().layoutMode = 'desktop'
    // The desktop is persisted: after a reload the chat window is already open
    // before the host mounts, so there is no open transition to observe.
    useAdminDesktopStore().open({ panel: 'chat', name: 'chat-panel', path: '', title: 'Chat' })
    const wrapper = mount(TChatHost, { global: { stubs: globalStubs, plugins: [pinia] } })
    await vi.waitFor(() => expect(fakeBridge.listConversations).toHaveBeenCalled(), { timeout: MOUNT_SETTLE_TIMEOUT_MS })

    expect(useChatStore().windowVisible).toBe(true)
    wrapper.unmount()
  })
})
