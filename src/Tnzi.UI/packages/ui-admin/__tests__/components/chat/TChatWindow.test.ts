import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TChatWindow from '../../../src/components/chat/TChatWindow.vue'
import { useChatStore } from '../../../src/stores/useChatStore'
import { ConversationType } from '@tnzi/core/services/chat'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'

// --- Fake data ---
const fakeConversations: ConversationListItemDto[] = [
  {
    id: 'conv-1', type: ConversationType.Direct, title: 'Alice',
    avatarFileId: null, lastMessagePreview: 'Hi', lastMessageAt: '2024-01-01T00:00:00Z',
    unreadCount: 0, isMuted: false, memberCount: 2,
  },
  {
    id: 'conv-2', type: ConversationType.Group, title: 'Team',
    avatarFileId: null, lastMessagePreview: 'Hey', lastMessageAt: '2024-01-01T00:01:00Z',
    unreadCount: 1, isMuted: false, memberCount: 4,
  },
]

// --- Fake bridge ---
const fakeBridge = {
  listConversations: vi.fn().mockResolvedValue(fakeConversations),
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

// --- Mocks ---
// TChatWindow no longer constructs a bridge — mock the module so imports resolve
vi.mock('../../../src/services/bridges/chat-im-bridge', () => ({
  createChatImBridge: () => fakeBridge,
}))

vi.mock('../../../src/headless/useChatRealtime', () => ({
  useChatRealtime: () => ({ start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined) }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => undefined,
  TNZI_ADMIN_CLIENT_KEY: Symbol('tnzi-admin-client'),
}))

vi.mock('../../../src/headless/useNotificationSound', () => ({
  useNotificationSound: () => ({ play: vi.fn(), setEnabled: vi.fn(), enabled: { value: true } }),
}))

vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({ isSm: { value: false } }),
  __resetTouchProbeForTests: vi.fn(),
}))

// Stub pinia-plugin-persistedstate
vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

// --- Global stubs ---
const globalConfig = {
  stubs: {
    NModal: { template: '<div class="n-modal-stub" v-if="show"><slot/></div>', props: ['show', 'bordered', 'preset', 'style'] },
    NScrollbar: { template: '<div><slot/></div>' },
    NInput: { template: '<input />', props: ['value', 'placeholder', 'size', 'clearable'] },
    NBadge: { template: '<div><slot/></div>', props: ['value', 'show', 'max'] },
    Icon: true,
  },
  plugins: [] as ReturnType<typeof createPinia>[],
}

describe('TChatWindow (pure display — orchestration moved to TChatHost)', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    fakeBridge.listConversations.mockResolvedValue(fakeConversations)
    fakeBridge.getMessages.mockResolvedValue({ messages: [], hasMore: false })
    fakeBridge.markRead.mockResolvedValue(undefined)
  })

  it('does NOT call listConversations on show=true (host is responsible)', async () => {
    // Seed store directly (as TChatHost would have done)
    const store = useChatStore()
    store.init(fakeBridge as any)
    await store.fetchConversations()

    const wrapper = mount(TChatWindow, {
      props: { show: true },
      global: { ...globalConfig, plugins: [pinia] },
    })
    // Window itself does NOT call listConversations — the host does
    fakeBridge.listConversations.mockClear()
    await wrapper.vm.$nextTick()
    expect(fakeBridge.listConversations).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('renders conversation list from pre-seeded store', async () => {
    const store = useChatStore()
    store.init(fakeBridge as any)
    await store.fetchConversations()

    const wrapper = mount(TChatWindow, {
      props: { show: true },
      global: { ...globalConfig, plugins: [pinia] },
      attachTo: document.body,
    })
    await wrapper.vm.$nextTick()
    // NModal uses teleport — content renders into document.body
    expect(document.body.innerHTML).toContain('t-conv-list')
    wrapper.unmount()
  })

  it('selecting a conversation calls store.openConversation', async () => {
    const store = useChatStore()
    store.init(fakeBridge as any)
    await store.fetchConversations()

    const wrapper = mount(TChatWindow, {
      props: { show: true },
      global: { ...globalConfig, plugins: [pinia] },
    })
    fakeBridge.getMessages.mockClear()

    await store.openConversation('conv-1')
    expect(fakeBridge.getMessages).toHaveBeenCalledWith('conv-1', expect.any(Object))
    wrapper.unmount()
  })

  it('accepts show prop and can be unmounted cleanly', () => {
    const wrapper = mount(TChatWindow, {
      props: { show: true },
      global: { ...globalConfig, plugins: [pinia] },
    })
    expect(wrapper.props('show')).toBe(true)
    wrapper.unmount()
  })

  it('re-emits update:show when the modal requests close', async () => {
    const NModalStub = {
      name: 'NModalStub',
      props: ['show', 'bordered', 'preset', 'style'],
      emits: ['update:show'],
      template: '<div class="n-modal-stub" v-if="show"><slot/></div>',
    }
    const wrapper = mount(TChatWindow, {
      props: { show: true },
      global: {
        ...globalConfig,
        plugins: [pinia],
        stubs: { ...globalConfig.stubs, NModal: NModalStub },
      },
    })
    await wrapper.vm.$nextTick()
    const modalStub = wrapper.findComponent(NModalStub)
    if (modalStub.exists()) {
      await modalStub.vm.$emit('update:show', false)
      expect(wrapper.emitted('update:show')?.[0]).toEqual([false])
    }
    wrapper.unmount()
  })
})
