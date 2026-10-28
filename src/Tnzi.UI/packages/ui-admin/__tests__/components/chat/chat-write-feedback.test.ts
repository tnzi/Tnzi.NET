import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { ConversationType, MemberRole, MessageContentType } from '@tnzi/core/services/chat'

/**
 * Chat writes the server refuses say why.
 *
 * The info panel and the new-chat dialog awaited the store with no catch: a
 * rename by a non-owner, an alias over the column width, a group past the
 * member cap, a group on a deployment with groups switched off. The spinner
 * stopped, the field quietly went back to its old value, and the reason only
 * reached the console as an unhandled rejection.
 */
const messageApi = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn() }

vi.mock('@tnzi/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/ui')>()),
  useSafeMessage: () => messageApi,
}))

const { useChatStore } = await import('../../../src/stores/useChatStore')
const TConversationInfoPanel = (await import('../../../src/components/chat/TConversationInfoPanel.vue')).default
const TNewChatDialog = (await import('../../../src/components/chat/TNewChatDialog.vue')).default
const TMemberGrid = (await import('../../../src/components/chat/TMemberGrid.vue')).default
const TInfoField = (await import('../../../src/components/chat/TInfoField.vue')).default

function makeBridge(convType: ConversationType = ConversationType.Group, isOwner = true) {
  const isGroup = convType === ConversationType.Group
  return {
    listConversations: vi.fn(async () => []),
    getMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
    sendMessage: vi.fn(async () => ({ id: 'm1', conversationId: 'c1', contentType: MessageContentType.Text, content: '', sentAt: '' })),
    markRead: vi.fn(async () => {}),
    getUnreadCount: vi.fn(async () => 0),
    searchContacts: vi.fn(async () => [{ userId: 'u3', name: 'Carol' }]),
    getOrCreateDirect: vi.fn(async () => ({ id: 'd-new', type: ConversationType.Direct, title: 'Carol', memberCount: 2, members: [] })),
    createGroup: vi.fn(async () => { throw new Error('Group size exceeds the maximum of 3 members') }),
    getConversation: vi.fn(async () => ({
      id: isGroup ? 'g1' : 'd1',
      type: convType,
      title: isGroup ? 'Test Group' : 'Other',
      memberCount: 2,
      notice: null,
      isSticky: false,
      isMuted: false,
      myRemark: null,
      myAlias: null,
      members: [
        { userId: 'me', name: 'Me', role: isOwner ? MemberRole.Owner : MemberRole.Member },
        { userId: 'other', name: 'Other', role: isOwner ? MemberRole.Member : MemberRole.Owner },
      ],
    })),
    addMembers: vi.fn(async () => {}),
    removeMember: vi.fn(async () => {}),
    renameGroup: vi.fn(async () => { throw new Error('Only the group owner can rename the group') }),
    dissolveGroup: vi.fn(async () => {}),
    leaveGroup: vi.fn(async () => {}),
    updateMemberSettings: vi.fn(async () => { throw new Error('Alias is too long (max 100 characters).') }),
    clearHistory: vi.fn(async () => {}),
    updateNotice: vi.fn(async () => {}),
    searchMessages: vi.fn(async () => ({ messages: [], hasMore: false })),
    getContactProfile: vi.fn(),
    mute: vi.fn(async () => {}),
    deleteMessage: vi.fn(async () => {}),
  }
}

type PanelVm = {
  loadDetail: () => Promise<void>
  onRename: (v: string) => Promise<void>
  onSaveAlias: (v: string) => Promise<void>
  onClearHistory: () => Promise<void>
}

async function mountPanel(bridge: ReturnType<typeof makeBridge>, convType = ConversationType.Group) {
  const store = useChatStore()
  store.init(bridge as never)
  const wrapper = mount(TConversationInfoPanel, {
    props: { show: false, conversationId: convType === ConversationType.Group ? 'g1' : 'd1', myId: 'me' },
  })
  await (wrapper.vm as unknown as PanelVm).loadDetail()
  await flushPromises()
  return { store, wrapper, vm: wrapper.vm as unknown as PanelVm }
}

describe('chat writes the server refuses', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('a refused rename shows the server reason and settles', async () => {
    const { vm } = await mountPanel(makeBridge())

    await expect(vm.onRename('New name')).resolves.toBeUndefined()

    expect(messageApi.error).toHaveBeenCalledWith('Only the group owner can rename the group')
  })

  it('a refused alias shows the server reason', async () => {
    const { vm } = await mountPanel(makeBridge())

    await expect(vm.onSaveAlias('x'.repeat(120))).resolves.toBeUndefined()

    expect(messageApi.error).toHaveBeenCalledWith('Alias is too long (max 100 characters).')
  })

  it('a failed clear-history shows the reason', async () => {
    const bridge = makeBridge()
    bridge.clearHistory.mockRejectedValueOnce(new Error('Conversation not found'))
    const { vm } = await mountPanel(bridge)

    await expect(vm.onClearHistory()).resolves.toBeUndefined()

    expect(messageApi.error).toHaveBeenCalledWith('Conversation not found')
  })

  it('caps the editable fields at the server column widths', async () => {
    const { wrapper } = await mountPanel(makeBridge())

    const limits = wrapper.findAllComponents(TInfoField).map((f) => f.props('maxlength'))
    // Group name, notice, my alias, remark.
    expect(limits).toEqual([200, 2000, 100, 100])
  })

  it('a direct chat offers "create group" only where groups are on', async () => {
    const bridge = makeBridge(ConversationType.Direct)
    const { store, wrapper } = await mountPanel(bridge, ConversationType.Direct)

    store.config = { ...store.config, enableGroups: true }
    await flushPromises()
    expect(wrapper.findComponent(TMemberGrid).props('canAdd')).toBe(true)

    store.config = { ...store.config, enableGroups: false }
    await flushPromises()
    expect(wrapper.findComponent(TMemberGrid).props('canAdd')).toBe(false)
  })

  it('a refused group creation in the new-chat dialog shows the reason and keeps the dialog', async () => {
    const store = useChatStore()
    store.init(makeBridge() as never)
    const wrapper = mount(TNewChatDialog, { props: { show: true } })
    const vm = wrapper.vm as unknown as {
      selected: { userId: string; name: string }[]
      groupName: string
      onCreateGroup: () => Promise<void>
    }
    vm.selected = [{ userId: 'u1', name: 'Alice' }, { userId: 'u2', name: 'Bob' }]
    vm.groupName = 'Team'

    await expect(vm.onCreateGroup()).resolves.toBeUndefined()

    expect(messageApi.error).toHaveBeenCalledWith('Group size exceeds the maximum of 3 members')
    expect(wrapper.emitted('update:show')).toBeUndefined()
  })
})
