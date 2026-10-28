/**
 * A file goes to the conversation it was dropped into.
 *
 * The upload runs for as long as the file takes to stream, and the conversation
 * list stays usable meanwhile. Reading the active conversation again once the
 * upload finished sent the file to whichever thread the user had moved on to,
 * which can have different members.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ConversationType, MessageContentType } from '@tnzi/core/services/chat'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'
import TChatWindow from '../../../src/components/chat/TChatWindow.vue'
import { useChatStore } from '../../../src/stores/useChatStore'

const conversations: ConversationListItemDto[] = [
  { id: 'conv-a', type: ConversationType.Direct, title: 'Alice', avatarFileId: null, lastMessagePreview: '', lastMessageAt: '2024-01-01T00:00:00Z', unreadCount: 0, isMuted: false, memberCount: 2 },
  { id: 'conv-b', type: ConversationType.Direct, title: 'Bob', avatarFileId: null, lastMessagePreview: '', lastMessageAt: '2024-01-01T00:00:00Z', unreadCount: 0, isMuted: false, memberCount: 2 },
]

const fakeBridge = {
  listConversations: vi.fn().mockResolvedValue(conversations),
  getMessages: vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
  markRead: vi.fn().mockResolvedValue(undefined),
  sendMessage: vi.fn(),
}

// The upload resolves when the test says so, standing in for a large file.
let finishUpload: (value: unknown) => void = () => undefined
vi.mock('@tnzi/core/services/storage', () => ({
  useStorageApi: () => ({
    upload: () => new Promise((resolve) => { finishUpload = resolve }),
  }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({}),
  TNZI_ADMIN_CLIENT_KEY: Symbol('tnzi-admin-client'),
}))

vi.mock('../../../src/headless/useChatSound', () => ({
  useChatSound: () => ({ configure: vi.fn(), playNotification: vi.fn(), playMessage: vi.fn(), preview: vi.fn() }),
}))

// A real ref: the template reads `isSm` unwrapped to decide whether the pane renders.
vi.mock('../../../src/headless/useBreakpoint', async () => {
  const { ref } = await import('vue')
  return { useBreakpoint: () => ({ isSm: ref(false) }), __resetTouchProbeForTests: vi.fn() }
})

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

const PaneStub = { name: 'TConversationPane', template: '<div />', emits: ['drop-file'] }

describe('TChatWindow - an upload is sent to the conversation it started in', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    fakeBridge.sendMessage.mockImplementation(async (id: string) => ({
      id: `m-${id}`, conversationId: id, senderId: 'me', contentType: MessageContentType.File, content: '', sentAt: '2024-01-01T00:00:00Z',
    }))
  })

  it('posts the file to the dropped-into conversation after the user switched threads', async () => {
    const store = useChatStore()
    store.init(fakeBridge as never)
    store.config = { ...store.config, enabled: true, enableFileMessages: true }
    await store.fetchConversations()
    await store.openConversation('conv-a')

    const wrapper = mount(TChatWindow, {
      props: { show: true, embedded: true },
      global: {
        stubs: { TConversationList: true, TConversationPane: PaneStub, TNewChatDialog: true, Icon: true },
      },
    })

    const file = new File(['abc'], 'contract.pdf', { type: 'application/pdf' })
    wrapper.findComponent(PaneStub).vm.$emit('drop-file', file)
    await Promise.resolve()

    // The user moves to another thread while the bar is still running.
    await store.openConversation('conv-b')
    finishUpload({ succeeded: true, data: { id: 'file-1', originalName: 'contract.pdf', fileName: 'contract.pdf', size: 3 } })
    await vi.waitFor(() => expect(fakeBridge.sendMessage).toHaveBeenCalledTimes(1))

    expect(fakeBridge.sendMessage).toHaveBeenCalledWith('conv-a', expect.objectContaining({ fileId: 'file-1' }))
    wrapper.unmount()
  })
})
