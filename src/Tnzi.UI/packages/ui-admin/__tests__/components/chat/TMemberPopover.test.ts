import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import type { ChatContactProfileDto } from '@tnzi/core/services/chat'
import TMemberPopover from '../../../src/components/chat/TMemberPopover.vue'
import { useChatStore } from '../../../src/stores/useChatStore'

// Minimal bridge so the store initialises without a real HTTP client
function minimalBridge() {
  return {
    listConversations: vi.fn(async () => []),
    getMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
    sendMessage: vi.fn(),
    markRead: vi.fn(async () => {}),
    getUnreadCount: vi.fn(async () => 0),
    getOrCreateDirect: vi.fn(),
    getConversation: vi.fn(),
    createGroup: vi.fn(),
    addMembers: vi.fn(),
    removeMember: vi.fn(),
    renameGroup: vi.fn(),
    dissolveGroup: vi.fn(),
    leaveGroup: vi.fn(),
    searchContacts: vi.fn(async () => []),
    updateMemberSettings: vi.fn(async () => {}),
    clearHistory: vi.fn(async () => {}),
    updateNotice: vi.fn(async () => {}),
    searchMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
    getContactProfile: vi.fn(),
    mute: vi.fn(async () => {}),
    deleteMessage: vi.fn(async () => {}),
    setStatus: vi.fn(async () => {}),
    getMyStatus: vi.fn(async () => UserPresenceStatus.Online),
    getPresence: vi.fn(async () => []),
  }
}

const fakeProfile: ChatContactProfileDto = {
  userId: 'u1',
  name: 'Alice',
  avatarFileId: null,
  status: UserPresenceStatus.Online,
  lastSeenAt: null,
}

const globalConfig = {
  stubs: {
    NPopover: { template: '<div class="n-popover-stub"><slot name="trigger"/><slot/></div>', props: ['show', 'trigger'], emits: ['update:show'] },
    NButton: { template: '<button class="n-button-stub" @click="$emit(\'click\')"><slot/></button>', emits: ['click'] },
    NSpin: true,
    TChatAvatar: true,
  },
}

describe('TMemberPopover', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('opening the popover calls store.getContactProfile once', async () => {
    const bridge = minimalBridge()
    bridge.getContactProfile.mockResolvedValue(fakeProfile)
    const store = useChatStore()
    store.init(bridge as never)
    const profileSpy = vi.spyOn(store, 'getContactProfile').mockResolvedValue(fakeProfile)

    const wrapper = mount(TMemberPopover, {
      props: { userId: 'u1', name: 'Alice' },
      global: globalConfig,
    })

    // Simulate opening the popover
    const vm = wrapper.vm as unknown as { onUpdateShow: (show: boolean) => void }
    vm.onUpdateShow(true)
    await wrapper.vm.$nextTick()

    expect(profileSpy).toHaveBeenCalledTimes(1)
    expect(profileSpy).toHaveBeenCalledWith('u1')
  })

  it('opening the popover a second time does NOT refetch (lazy once-per-user)', async () => {
    const bridge = minimalBridge()
    bridge.getContactProfile.mockResolvedValue(fakeProfile)
    const store = useChatStore()
    store.init(bridge as never)
    const profileSpy = vi.spyOn(store, 'getContactProfile').mockResolvedValue(fakeProfile)

    const wrapper = mount(TMemberPopover, {
      props: { userId: 'u1', name: 'Alice' },
      global: globalConfig,
    })

    const vm = wrapper.vm as unknown as { onUpdateShow: (show: boolean) => void }
    vm.onUpdateShow(true)
    await wrapper.vm.$nextTick()
    vm.onUpdateShow(false)
    vm.onUpdateShow(true)
    await wrapper.vm.$nextTick()

    expect(profileSpy).toHaveBeenCalledTimes(1)
  })

  it('onSendMessage emits message(userId)', async () => {
    const bridge = minimalBridge()
    const store = useChatStore()
    store.init(bridge as never)
    vi.spyOn(store, 'getContactProfile').mockResolvedValue(fakeProfile)

    const wrapper = mount(TMemberPopover, {
      props: { userId: 'u1', name: 'Alice' },
      global: globalConfig,
    })

    const vm = wrapper.vm as unknown as { onSendMessage: () => void }
    vm.onSendMessage()

    const emitted = wrapper.emitted('message')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['u1'])
  })
})
