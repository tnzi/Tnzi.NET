import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatStore, DEFAULT_CHAT_CONFIG } from '../../src/stores/useChatStore'
import type { ChatImBridge } from '../../src/services/bridges/chat-im-bridge'

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

function makeBridge(overrides: Partial<ChatImBridge> = {}): ChatImBridge {
  return {
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
    updateMemberSettings: vi.fn(),
    clearHistory: vi.fn(),
    searchMessages: vi.fn(),
    updateNotice: vi.fn(),
    getContactProfile: vi.fn(),
    setStatus: vi.fn(),
    getMyStatus: vi.fn(),
    getPresence: vi.fn().mockResolvedValue([]),
    getConfig: vi.fn().mockResolvedValue({ ...DEFAULT_CHAT_CONFIG }),
    ...overrides,
  } as ChatImBridge
}

describe('useChatStore deployment config', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts chat disabled (deny-by-default) until loadConfig confirms the grant', () => {
    const store = useChatStore()
    // Pessimistic initial: the launcher stays hidden (no icon flash) until
    // GET /chat/config confirms this user holds chat.use.
    expect(store.config.enabled).toBe(false)
    // Every other feature still mirrors the all-enabled defaults.
    expect(store.config).toEqual({ ...DEFAULT_CHAT_CONFIG, enabled: false })
  })

  it('projects the per-user enabled flag from the server (deny-by-default gate)', async () => {
    const store = useChatStore()
    store.init(makeBridge({ getConfig: vi.fn().mockResolvedValue({ ...DEFAULT_CHAT_CONFIG, enabled: false }) }))
    await store.loadConfig()
    expect(store.config.enabled).toBe(false)
  })

  it('loadConfig applies the server projection', async () => {
    const store = useChatStore()
    store.init(makeBridge({
      getConfig: vi.fn().mockResolvedValue({
        enableGroups: false,
        maxGroupMembers: 50,
        groupAvatarMemberCount: 4,
        enablePresence: false,
        allowInvisible: false,
        enableMessageSound: false,
        enableFileMessages: false,
      }),
    }))

    await store.loadConfig()

    expect(store.config.enableGroups).toBe(false)
    expect(store.config.maxGroupMembers).toBe(50)
    expect(store.config.groupAvatarMemberCount).toBe(4)
    expect(store.config.enablePresence).toBe(false)
    expect(store.config.allowInvisible).toBe(false)
    expect(store.config.enableMessageSound).toBe(false)
    expect(store.config.enableFileMessages).toBe(false)
  })

  it('forces enabled:false (deny-by-default) when the endpoint throws', async () => {
    // A load failure must NOT fail-open into enabled:true - that would mount the
    // chat host for a user we cannot confirm holds chat.use, hitting the
    // 403-guarded /conversations + /presence endpoints. Feature flags still
    // mirror DEFAULT; only `enabled` is forced false.
    const store = useChatStore()
    store.init(makeBridge({ getConfig: vi.fn().mockRejectedValue(new Error('404')) }))

    await store.loadConfig()

    expect(store.config).toEqual({ ...DEFAULT_CHAT_CONFIG, enabled: false })
  })

  it('forces enabled:false when the bridge lacks getConfig entirely (stale fake/backend)', async () => {
    const store = useChatStore()
    const bridge = makeBridge()
    delete (bridge as Partial<ChatImBridge>).getConfig
    store.init(bridge)

    await store.loadConfig()

    expect(store.config).toEqual({ ...DEFAULT_CHAT_CONFIG, enabled: false })
  })

  it('forces enabled:false when getConfig resolves to null (forbidden/empty envelope unwrapped)', async () => {
    // The exact regression: a `{ succeeded, data: null }` envelope unwraps to
    // null; the old `{ ...DEFAULT, ...null }` spread fail-opened to enabled:true,
    // mounting TChatHost for a denied user → GET /conversations 403 + crash.
    const store = useChatStore()
    store.init(makeBridge({ getConfig: vi.fn().mockResolvedValue(null as never) }))

    await store.loadConfig()

    expect(store.config.enabled).toBe(false)
    expect(store.config).toEqual({ ...DEFAULT_CHAT_CONFIG, enabled: false })
  })

  it('openConversation skips peer presence loading when presence is disabled', async () => {
    const store = useChatStore()
    const getPresence = vi.fn().mockResolvedValue([])
    store.init(makeBridge({
      getConfig: vi.fn().mockResolvedValue({ ...DEFAULT_CHAT_CONFIG, enablePresence: false }),
      getPresence,
    }))
    await store.loadConfig()
    store.conversations = [
      { id: 'c1', type: 'Direct', title: 'Bob', unreadCount: 0, isMuted: false, memberCount: 2, isSticky: false, peerUserId: 'u2' } as never,
    ]

    await store.openConversation('c1')

    expect(getPresence).not.toHaveBeenCalled()
  })
})
