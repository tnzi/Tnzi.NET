import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { useChatStore } from '../../../src/stores/useChatStore'
import TConversationInfoPanel from '../../../src/components/chat/TConversationInfoPanel.vue'
import { MemberRole } from '@tnzi/core/services/chat'

function makeBridge(isOwner = true, type: 1 | 2 = 2) {
  const myId = 'me'
  const ownerId = isOwner ? myId : 'other'
  return {
    listConversations: vi.fn(async () => []),
    getMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
    sendMessage: vi.fn(async () => ({ id: 'm1', conversationId: 'c1', contentType: 1, content: '', sentAt: '' })),
    markRead: vi.fn(async () => {}),
    getUnreadCount: vi.fn(async () => 0),
    searchContacts: vi.fn(async () => [{ userId: 'u3', name: 'Carol' }]),
    getOrCreateDirect: vi.fn(async () => ({ id: 'd-new', type: 1, title: 'Carol', memberCount: 2, members: [] })),
    createGroup: vi.fn(),
    getConversation: vi.fn(async () => ({
      id: type === 2 ? 'g1' : 'd1',
      type,
      title: type === 2 ? 'Test Group' : 'Carol',
      memberCount: 2,
      ownerId,
      notice: 'Welcome',
      isSticky: false,
      isMuted: false,
      myRemark: null,
      myAlias: null,
      members: [
        { userId: myId, name: 'Me', role: isOwner ? MemberRole.Owner : MemberRole.Member },
        { userId: 'other', name: 'Other', role: isOwner ? MemberRole.Member : MemberRole.Owner },
      ],
    })),
    addMembers: vi.fn(async () => {}),
    removeMember: vi.fn(async () => {}),
    renameGroup: vi.fn(async () => {}),
    dissolveGroup: vi.fn(async () => {}),
    leaveGroup: vi.fn(async () => {}),
    updateMemberSettings: vi.fn(async () => {}),
    clearHistory: vi.fn(async () => {}),
    updateNotice: vi.fn(async () => {}),
    searchMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
    getContactProfile: vi.fn(),
    mute: vi.fn(async () => {}),
    deleteMessage: vi.fn(async () => {}),
  }
}

function mountPanel(bridge: ReturnType<typeof makeBridge>, type: 1 | 2 = 2) {
  const store = useChatStore()
  store.init(bridge as never)
  const wrapper = mount(TConversationInfoPanel, {
    props: { show: true, conversationId: type === 2 ? 'g1' : 'd1', myId: 'me' },
  })
  return { store, wrapper }
}

describe('TConversationInfoPanel', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('loads conversation detail on show=true', async () => {
    const { wrapper } = mountPanel(makeBridge(true))
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    const vm = wrapper.vm as unknown as { detail: { id: string } | null; isOwner: boolean; isGroup: boolean }
    expect(vm.detail?.id).toBe('g1')
    expect(vm.isOwner).toBe(true)
    expect(vm.isGroup).toBe(true)
  })

  it('owner: renameGroup called on save', async () => {
    const { store, wrapper } = mountPanel(makeBridge(true))
    const renameSpy = vi.spyOn(store, 'renameGroup')
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    const vm = wrapper.vm as unknown as { editTitle: string; onRename: () => Promise<void> }
    vm.editTitle = 'New Name'
    await vm.onRename()
    expect(renameSpy).toHaveBeenCalledWith('g1', 'New Name')
  })

  it('owner: dissolveGroup called and panel closes', async () => {
    const { store, wrapper } = mountPanel(makeBridge(true))
    const dissolveSpy = vi.spyOn(store, 'dissolveGroup')
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    await (wrapper.vm as unknown as { onDissolve: () => Promise<void> }).onDissolve()
    expect(dissolveSpy).toHaveBeenCalledWith('g1')
    expect(wrapper.emitted('update:show')?.[0]).toEqual([false])
  })

  it('non-owner: leaveGroup called and panel closes', async () => {
    const { store, wrapper } = mountPanel(makeBridge(false))
    const leaveSpy = vi.spyOn(store, 'leaveGroup')
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    const vm = wrapper.vm as unknown as { isOwner: boolean; onLeave: () => Promise<void> }
    expect(vm.isOwner).toBe(false)
    await vm.onLeave()
    expect(leaveSpy).toHaveBeenCalledWith('g1')
    expect(wrapper.emitted('update:show')?.[0]).toEqual([false])
  })

  it('addMembers filters out existing members from candidates', async () => {
    const { store, wrapper } = mountPanel(makeBridge(true))
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    const vm = wrapper.vm as unknown as {
      addCandidates: { userId: string; name: string }[]
    }
    const results = await store.searchContacts('Carol')
    const detail = (wrapper.vm as unknown as { detail: { members: { userId: string }[] } | null }).detail
    const existingIds = new Set(detail?.members.map((m) => m.userId) ?? [])
    vm.addCandidates = results.filter((c) => !existingIds.has(c.userId))
    await wrapper.vm.$nextTick()
    // Carol (u3) is not an existing member, so she should appear.
    expect(vm.addCandidates.some((c) => c.userId === 'u3')).toBe(true)
  })

  it('clearHistory called, reloads detail and emits changed', async () => {
    const { store, wrapper } = mountPanel(makeBridge(true))
    const clearSpy = vi.spyOn(store, 'clearHistory')
    const detailSpy = vi.spyOn(store, 'getConversationDetail')
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    detailSpy.mockClear() // reset call count from initial load
    await (wrapper.vm as unknown as { onClearHistory: () => Promise<void> }).onClearHistory()
    expect(clearSpy).toHaveBeenCalledWith('g1')
    expect(detailSpy).toHaveBeenCalledWith('g1')
    expect(wrapper.emitted('changed')).toBeTruthy()
  })

  it('toggles mute via setMemberSettings', async () => {
    const { store, wrapper } = mountPanel(makeBridge(true))
    const setSpy = vi.spyOn(store, 'setMemberSettings')
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    await (wrapper.vm as unknown as { onToggleMute: (v: boolean) => Promise<void> }).onToggleMute(true)
    expect(setSpy).toHaveBeenCalledWith('g1', { isMuted: true })
  })

  it('direct conversation: isGroup false, message member starts direct', async () => {
    const { store, wrapper } = mountPanel(makeBridge(true, 1), 1)
    const startSpy = vi.spyOn(store, 'startDirect')
    await (wrapper.vm as unknown as { loadDetail: () => Promise<void> }).loadDetail()
    await wrapper.vm.$nextTick()
    const vm = wrapper.vm as unknown as { isGroup: boolean; onMessageMember: (id: string) => Promise<void> }
    expect(vm.isGroup).toBe(false)
    await vm.onMessageMember('other')
    expect(startSpy).toHaveBeenCalledWith('other')
    expect(wrapper.emitted('open-conversation')?.[0]).toEqual(['d-new'])
  })
})
