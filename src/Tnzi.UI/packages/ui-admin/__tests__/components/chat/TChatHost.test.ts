import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import TChatHost from '../../../src/components/chat/TChatHost.vue'
import { useAdminDesktopStore } from '../../../src/stores/useAdminDesktopStore'
import { useAdminThemeStore } from '../../../src/stores/useAdminThemeStore'

// --- Fake bridge ---
const fakeBridge = {
  // enabled:true = this user holds chat.use, so TChatHost proceeds past the
  // deny-by-default gate and fetches conversations. enablePresence:false keeps
  // the mount path off the presence branch.
  getConfig: vi.fn().mockResolvedValue({ enabled: true, enablePresence: false }),
  listConversations: vi.fn().mockResolvedValue([]),
  getUnreadCount: vi.fn().mockResolvedValue(0),
  getOrCreateDirect: vi.fn(),
  getConversation: vi.fn(),
  getMessages: vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
  sendMessage: vi.fn(),
  markRead: vi.fn().mockResolvedValue(undefined),
  mute: vi.fn(),
  deleteMessage: vi.fn(),
  createGroup: vi.fn(),
  addMembers: vi.fn(),
  removeMember: vi.fn(),
  renameGroup: vi.fn(),
  dissolveGroup: vi.fn(),
  leaveGroup: vi.fn(),
  searchContacts: vi.fn().mockResolvedValue([]),
}

const mockRealtimeStart = vi.fn().mockResolvedValue(undefined)
const mockRealtimeStop = vi.fn().mockResolvedValue(undefined)

vi.mock('../../../src/services/bridges/chat-im-bridge', () => ({
  createChatImBridge: () => fakeBridge,
}))

vi.mock('../../../src/headless/useChatRealtime', () => ({
  useChatRealtime: () => ({ start: mockRealtimeStart, stop: mockRealtimeStop }),
}))

vi.mock('../../../src/headless/useChatSound', () => ({
  useChatSound: () => ({ configure: vi.fn(), playNotification: vi.fn(), playMessage: vi.fn(), preview: vi.fn() }),
}))

vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({ isSm: { value: false } }),
}))

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

// Client mock - toggled per test
let mockClient: object | undefined = {}
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: (required?: boolean) => {
    if (required === false) return mockClient
    if (!mockClient) throw new Error('no client')
    return mockClient
  },
  TNZI_ADMIN_CLIENT_KEY: Symbol('tnzi-admin-client'),
}))

const globalStubs = {
  stubs: {
    TChatLauncher: { template: '<button class="t-chat-launcher-stub" />', props: ['unreadCount'], emits: ['open'] },
    TChatWindow: { template: '<div class="t-chat-window-stub" />', props: ['show'], emits: ['update:show'] },
    NModal: { template: '<div><slot/></div>', props: ['show'] },
  },
}

describe('TChatHost', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    mockClient = {}
    fakeBridge.listConversations.mockResolvedValue([])
    mockRealtimeStart.mockResolvedValue(undefined)
    mockRealtimeStop.mockResolvedValue(undefined)
  })

  it('with client: inits store, calls fetchConversations and realtime.start on mount', async () => {
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [createPinia()] } })
    await vi.waitFor(() => {
      expect(fakeBridge.listConversations).toHaveBeenCalled()
      expect(mockRealtimeStart).toHaveBeenCalled()
    })
    wrapper.unmount()
  })

  it('with client: renders TChatLauncher', () => {
    setActivePinia(createPinia())
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [] } })
    expect(wrapper.find('.t-chat-launcher-stub').exists()).toBe(true)
    wrapper.unmount()
  })

  it('without client: renders nothing and does NOT start realtime', () => {
    mockClient = undefined
    setActivePinia(createPinia())
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [] } })
    expect(wrapper.find('.t-chat-launcher-stub').exists()).toBe(false)
    expect(fakeBridge.listConversations).not.toHaveBeenCalled()
    expect(mockRealtimeStart).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('calls realtime.stop on unmount', async () => {
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [createPinia()] } })
    await vi.waitFor(() => expect(fakeBridge.listConversations).toHaveBeenCalled())
    wrapper.unmount()
    expect(mockRealtimeStop).toHaveBeenCalled()
  })
})

describe('TChatHost - chat is an ordinary window on the desktop', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    mockClient = {}
    fakeBridge.listConversations.mockResolvedValue([])
    mockRealtimeStart.mockResolvedValue(undefined)
    mockRealtimeStop.mockResolvedValue(undefined)
  })

  it('opens a desktop window instead of a floating modal', async () => {
    useAdminThemeStore().layoutMode = 'desktop'
    const desktop = useAdminDesktopStore()
    const wrapper = mount(TChatHost, { global: globalStubs })
    await flushPromises()

    wrapper.findComponent('.t-chat-launcher-stub').vm.$emit('open')
    await nextTick()

    // As a modal it sat permanently above every window and never appeared in
    // the taskbar - the taskbar renders exactly this list.
    expect(desktop.windows).toHaveLength(1)
    expect(desktop.windows[0].stack[0].panel).toBe('chat')
    // And the modal instance must NOT also be mounted: two live copies would
    // run every watcher twice for one visible surface.
    expect(wrapper.find('.t-chat-window-stub').exists()).toBe(false)
  })

  it('still uses the floating window in every other layout', async () => {
    useAdminThemeStore().layoutMode = 'vertical'
    const desktop = useAdminDesktopStore()
    const wrapper = mount(TChatHost, { global: globalStubs })
    await flushPromises()

    wrapper.findComponent('.t-chat-launcher-stub').vm.$emit('open')
    await nextTick()

    expect(desktop.windows).toHaveLength(0)
    expect(wrapper.find('.t-chat-window-stub').exists()).toBe(true)
  })
})
