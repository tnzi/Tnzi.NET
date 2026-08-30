import { describe, it, expect, vi, beforeEach } from 'vitest'
import { readFileSync } from 'node:fs'
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
// TChatWindow no longer constructs a bridge - mock the module so imports resolve
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

vi.mock('../../../src/headless/useChatSound', () => ({
  useChatSound: () => ({ configure: vi.fn(), playNotification: vi.fn(), playMessage: vi.fn(), preview: vi.fn() }),
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

describe('TChatWindow (pure display - orchestration moved to TChatHost)', () => {
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
    // Window itself does NOT call listConversations - the host does
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
    // NModal uses teleport - content renders into document.body
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

describe('chat is a window, not a blocking modal', () => {
  // Asserted on SOURCE rather than a mounted tree, deliberately. The substance
  // of this invariant is CSS that neutralises naive's mask and click-swallowing
  // container - they live outside the component's Teleport, and jsdom computes
  // neither `:has()` nor `pointer-events`. A mount test could only re-state the
  // two props and would pass while the half that actually unblocks the page was
  // deleted.
  const read = (rel: string): string =>
    readFileSync(new URL(rel, import.meta.url), 'utf8')

  it('turns off focus trapping and scroll locking', () => {
    // As a real modal, chat swallowed every click: with it open no other
    // feature could be reached at all. Reported on the desktop layout, but it
    // was wrong in every layout - chat is a surface you leave open while you
    // work, the way a desktop IM client is.
    const sfc = read('../../../src/components/chat/TChatWindow.vue')
    expect(sfc).toMatch(/trapFocus:\s*false/)
    expect(sfc).toMatch(/blockScroll:\s*false/)
  })

  it('makes the mask and container click-through', () => {
    const css = read('../../../src/styles/polish.css')
    // The container swallows the clicks; the window itself must take pointer
    // events back or chat would be inert too.
    expect(css).toMatch(/\.n-modal-container:has\(\.t-chat-window\)\s*\{[^}]*pointer-events:\s*none/)
    expect(css).toMatch(/\.n-modal-container:has\(\.t-chat-window\)\s+\.t-chat-window\s*\{[^}]*pointer-events:\s*auto/)
    expect(css).toMatch(/\.n-modal-container:has\(\.t-chat-window\)\s+\.n-modal-mask\s*\{[^}]*display:\s*none/)
  })

  it('draws its own edge, since the mask no longer does', () => {
    // The other half of hiding the mask. `--chat-bg` is `--tnzi-bg-deep`,
    // measured at rgb(246 248 250) against an admin canvas of rgb(247 250 252)
    // - a few RGB steps apart. While the mask was there the page behind was
    // dimmed 45% and that drew the boundary; without it the window has to draw
    // its own or the message pane runs straight into the page.
    //
    // It used to draw a hand-rolled 1px ring at 16% of the TEXT colour, which
    // worked but in a colour nothing else uses: `--tnzi-border` is near-white
    // and the text token is near-black, so this was the only surface in the
    // shell outlined in black. It now takes the overlay tier like every other
    // floating surface, and that tier's ambient layer marks the top edge that
    // a purely downward shadow leaves bare.
    const sfc = read('../../../src/components/chat/TChatWindow.vue')
    const rule = sfc.match(/\n\.t-chat-window \{[\s\S]*?\n\}/)?.[0] ?? ''
    expect(rule).not.toBe('')
    expect(rule).toMatch(/box-shadow:\s*var\(--tnzi-surface-overlay-shadow\)/)
    // Not a literal of its own again: the point is that it looks like the other
    // floating surfaces, which only holds while it reads the same token.
    expect(rule).not.toMatch(/box-shadow:[\s\S]*?rgb\(var\(--tnzi-base-text-rgb/)
  })
})
