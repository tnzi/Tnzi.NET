import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { ConversationListItemDto, ChatMessageDto, PresenceChangedPayload, ConversationMemberSettingsDto, ChatClientConfigDto } from '@tnzi/core/services/chat'
import type { ChatImBridge } from '../services/bridges/chat-im-bridge'
import type { NewMessagePayload, MessageReadPayload } from '@tnzi/core/services/chat'
import { MessageContentType, UserPresenceStatus, ChatSoundEffect, ChatNewMessageEffect } from '@tnzi/core/services/chat'
import { useAdminAuthStore } from './useAdminAuthStore'

/** Default FEATURE FLAGS (groups / sounds / presence / limits) used as the base
 *  every real `GET /chat/config` payload is spread over. `enabled: true` here is
 *  only the flag base — it is NOT a fail-open: `loadConfig()` forces `enabled`
 *  back to `false` whenever the config can't be confirmed (null/forbidden/empty
 *  result or a throw), so a user without `chat.use` is deny-by-default and never
 *  calls chat's 403-guarded endpoints. The pre-load `config` ref (below) likewise
 *  starts `enabled: false` so the launcher never flashes before the probe. */
export const DEFAULT_CHAT_CONFIG: ChatClientConfigDto = Object.freeze({
  enabled: true,
  enableGroups: true,
  maxGroupMembers: 0,
  groupAvatarMemberCount: 9,
  enablePresence: true,
  allowInvisible: true,
  enableMessageSound: true,
  notificationSound: ChatSoundEffect.Chime,
  messageSound: ChatSoundEffect.Pop,
  newMessageEffect: ChatNewMessageEffect.Shake,
  flashTitleOnMessage: true,
  enableFileMessages: true,
})

/**
 * A message as held in the local thread. Extends the server DTO with a few
 * client-only fields for an optimistic-on-failure send: when a send is rejected
 * (the sender lost `chat.use` mid-session → 403, or a network error) the
 * attempted message is kept in the thread and flagged `failed`, so the window
 * can show a WeChat-style red retry marker + reason INLINE instead of a
 * disappearing toast. Cleared once the message sends (the real DTO replaces it).
 */
export interface ChatMessageView extends ChatMessageDto {
  /** A send that failed — the bubble shows the red retry marker + reason. */
  failed?: boolean
  /** Failure reason surfaced in-window (the backend 403 message, etc.). */
  failReason?: string
  /** Stable client id used to retry / remove this local placeholder. */
  clientId?: number
}

export const useChatStore = defineStore('admin-chat', () => {
  // Monotonic id for local (failed) message placeholders — unique per store.
  let clientSeq = 0
  let bridge: ChatImBridge | null = null

  const conversations = ref<ConversationListItemDto[]>([])
  // Deployment-level feature config (server ChatOptions projection) + this
  // user's `chat.use` grant. Starts `enabled: false` (pessimistic): chat is
  // deny-by-default, so the launcher stays hidden until GET /chat/config
  // confirms the grant — no icon flash for a disabled user. loadConfig() flips
  // it from the server value, or fails open to DEFAULT_CHAT_CONFIG.
  const config = ref<ChatClientConfigDto>({ ...DEFAULT_CHAT_CONFIG, enabled: false })
  const activeId = ref<string | null>(null)
  // Whether the chat window (NModal) is currently open. "User is viewing a
  // conversation" = window visible AND conversation active: activeId alone
  // survives closing the window (so reopening lands on the same thread), and
  // must therefore never suppress the unread badge / notification sound.
  const windowVisible = ref(false)
  const messagesByConv = ref<Record<string, ChatMessageView[]>>({})
  const loading = ref(false)
  const presenceByUser = ref<Record<string, { status: UserPresenceStatus; lastSeenAt?: string | null }>>({})
  const myStatus = ref<UserPresenceStatus>(UserPresenceStatus.Online)

  const totalUnread = computed(() => conversations.value.reduce((s, c) => s + (c.unreadCount || 0), 0))
  const activeConversation = computed(() => conversations.value.find(c => c.id === activeId.value) ?? null)
  const sortedConversations = computed(() =>
    [...conversations.value].sort((a, b) => {
      if (a.isSticky !== b.isSticky) return a.isSticky ? -1 : 1
      return new Date(b.lastMessageAt ?? 0).getTime() - new Date(a.lastMessageAt ?? 0).getTime()
    }))

  function init(b: ChatImBridge) { bridge = b }
  function requireBridge(): ChatImBridge { if (!bridge) throw new Error('useChatStore: call init(bridge) first'); return bridge }

  async function fetchConversations() {
    loading.value = true
    // `?? []` keeps the `conversations` ref invariant (never undefined) even if a
    // bridge/backend hands back null — every getter (totalUnread/sorted/map) reads it.
    try { conversations.value = (await requireBridge().listConversations()) ?? [] }
    finally { loading.value = false }
  }

  /** Load the deployment feature config; keeps the all-enabled defaults on any
   *  failure so the chat window still works against older backends. */
  async function loadConfig() {
    try {
      const cfg = await requireBridge().getConfig()
      // DENY-BY-DEFAULT: enable chat ONLY from a real config payload (the backend
      // projects `enabled` from the user's `chat.use` grant). A null / forbidden /
      // empty result — e.g. a `{ succeeded, data: null }` envelope unwrapped to
      // null, or a 401 during the login→redirect token transition — must NOT
      // fail-open into `enabled: true`. Spreading such a null over DEFAULT
      // (`{ ...DEFAULT, ...null }` → enabled:true) mounted TChatHost for a user
      // WITHOUT chat.use, which then hit the 403-guarded /conversations +
      // /presence endpoints and crashed on the null result. Feature flags still
      // come from DEFAULT; only `enabled` is forced false when unconfirmed.
      config.value =
        cfg && typeof cfg === 'object'
          ? { ...DEFAULT_CHAT_CONFIG, ...cfg }
          : { ...DEFAULT_CHAT_CONFIG, enabled: false }
    } catch {
      // Genuine load failure (network / no endpoint): stay deny-by-default so a
      // user we can't confirm never calls chat's permission-guarded endpoints.
      config.value = { ...DEFAULT_CHAT_CONFIG, enabled: false }
    }
  }

  async function openConversation(id: string) {
    activeId.value = id
    const thread = await requireBridge().getMessages(id, { limit: 30 })
    messagesByConv.value = { ...messagesByConv.value, [id]: thread.messages }
    await markRead(id)
    const conv = conversations.value.find(c => c.id === id)
    if (conv?.peerUserId && config.value.enablePresence) loadPresence([conv.peerUserId]).catch(() => undefined)
  }

  async function sendText(id: string, content: string) {
    try {
      const msg = await requireBridge().sendMessage(id, { contentType: MessageContentType.Text, content })
      appendMessage(id, msg)
    } catch (e) {
      // Keep the attempted message in the thread, flagged failed, so the window
      // shows a WeChat-style retry marker + reason inline (no toast). Re-throw so
      // the caller skips the "sent" sound.
      pushFailed(id, { contentType: MessageContentType.Text, content }, e)
      throw e
    }
  }

  /** Append a failed local placeholder for a send that the server rejected. It
   *  deliberately does NOT touch the conversation's preview/last-message (the
   *  message never sent), only the open thread. */
  function pushFailed(
    id: string,
    partial: { contentType: MessageContentType; content?: string; fileId?: string; fileName?: string; fileSize?: number },
    e: unknown,
  ) {
    const me = useAdminAuthStore().userInfo
    const cid = ++clientSeq
    const failed: ChatMessageView = {
      id: `local-fail-${cid}`,
      conversationId: id,
      senderId: me?.id ?? '',
      senderName: me?.shortName || me?.displayName || '',
      senderAvatarFileId: null,
      contentType: partial.contentType,
      content: partial.content ?? '',
      fileId: partial.fileId ?? null,
      fileName: partial.fileName ?? null,
      fileSize: partial.fileSize ?? null,
      sentAt: new Date().toISOString(),
      failed: true,
      failReason: e instanceof Error ? e.message : '',
      clientId: cid,
    }
    const cur = messagesByConv.value[id] ?? []
    messagesByConv.value = { ...messagesByConv.value, [id]: [...cur, failed] }
  }

  /** Retry a failed local message: drop the placeholder, then re-send. A still-
   *  revoked grant just fails again (a fresh placeholder); a re-granted one sends. */
  async function resendMessage(id: string, clientId: number) {
    const cur = messagesByConv.value[id] ?? []
    const failed = cur.find((m) => m.clientId === clientId)
    if (!failed) return
    messagesByConv.value = { ...messagesByConv.value, [id]: cur.filter((m) => m.clientId !== clientId) }
    if (failed.contentType === MessageContentType.Text) {
      await sendText(id, failed.content)
    } else if (failed.fileId) {
      await sendMedia(id, {
        contentType: failed.contentType,
        fileId: failed.fileId,
        fileName: failed.fileName ?? '',
        fileSize: failed.fileSize ?? 0,
      })
    }
  }

  async function markRead(id: string) {
    await requireBridge().markRead(id)
    const conv = conversations.value.find(c => c.id === id)
    if (conv) conv.unreadCount = 0
  }

  function appendMessage(id: string, msg: ChatMessageView) {
    const cur = messagesByConv.value[id] ?? []
    if (cur.some(m => m.id === msg.id)) return
    messagesByConv.value = { ...messagesByConv.value, [id]: [...cur, msg] }
    const conv = conversations.value.find(c => c.id === id)
    if (conv) { conv.lastMessageAt = msg.sentAt; conv.lastMessagePreview = previewOf(msg) }
  }

  function previewOf(m: ChatMessageDto): string {
    if (m.contentType === MessageContentType.Image) return '[Image]'
    if (m.contentType === MessageContentType.File) return '[File]'
    return m.content
  }

  function setWindowVisible(v: boolean) { windowVisible.value = v }

  function applyIncomingMessage(p: NewMessagePayload, myId?: string) {
    const isActive = windowVisible.value && activeId.value === p.conversationId
    // senderId=null → system/broadcast message; "!== my id" treats it as from-other
    // so it bumps the unread count (the realtime layer plays the sound in parallel).
    const fromOther = p.senderId !== (myId ?? useAdminAuthStore().userInfo?.id)
    // Incremental append: the backend pushes the full message body so an already
    // open thread updates live without a full re-fetch. appendMessage dedupes by
    // id (our own optimistic send is ignored) and refreshes the conversation's
    // preview/lastMessageAt from the real body.
    if (p.message && messagesByConv.value[p.conversationId]) {
      appendMessage(p.conversationId, p.message)
    }
    const conv = conversations.value.find(c => c.id === p.conversationId)
    if (conv) {
      conv.lastMessagePreview = p.preview
      if (!p.message) conv.lastMessageAt = new Date().toISOString()
      if (!isActive && fromOther) conv.unreadCount = (conv.unreadCount || 0) + 1
    }
    // If the conversation isn't in the list yet (new direct/group/system), caller should refetch.
  }

  function applyRead(_p: MessageReadPayload) { /* read-receipt UI handled in Plan 3 */ }

  async function refreshUnread() {
    const total = await requireBridge().getUnreadCount()
    // total is authoritative; conversations carry per-conv counts from list fetch.
    return total
  }

  // Part 1: contact search + conversation creation
  async function searchContacts(keyword: string) {
    return requireBridge().searchContacts(keyword)
  }

  async function startDirect(userId: string) {
    const c = await requireBridge().getOrCreateDirect(userId)
    await fetchConversations()
    await openConversation(c.id)
    return c.id
  }

  async function createGroup(title: string, memberIds: string[]) {
    const c = await requireBridge().createGroup({ title, memberIds })
    await fetchConversations()
    await openConversation(c.id)
    return c.id
  }

  // Part 2: group management
  async function getConversationDetail(id: string) {
    return requireBridge().getConversation(id)
  }

  async function addMembers(id: string, userIds: string[]) {
    await requireBridge().addMembers(id, userIds)
    await fetchConversations()
  }

  async function removeMember(id: string, userId: string) {
    await requireBridge().removeMember(id, userId)
    await fetchConversations()
  }

  async function renameGroup(id: string, title: string) {
    await requireBridge().renameGroup(id, title)
    await fetchConversations()
  }

  async function dissolveGroup(id: string) {
    await requireBridge().dissolveGroup(id)
    await fetchConversations()
    if (activeId.value === id) activeId.value = null
  }

  async function leaveGroup(id: string) {
    await requireBridge().leaveGroup(id)
    await fetchConversations()
    if (activeId.value === id) activeId.value = null
  }

  // Presence
  function applyPresenceChange(p: PresenceChangedPayload) {
    presenceByUser.value = { ...presenceByUser.value, [p.userId]: { status: p.status, lastSeenAt: p.lastSeenAt } }
    const conv = conversations.value.find(c => c.peerUserId === p.userId)
    if (conv) conversations.value = conversations.value.map(c => c.peerUserId === p.userId ? { ...c, peerStatus: p.status } : c)
  }

  async function loadPresence(userIds: string[]) {
    const ids = userIds.filter(Boolean)
    if (!ids.length) return
    const list = await requireBridge().getPresence(ids)
    const next = { ...presenceByUser.value }
    for (const p of list) next[p.userId] = { status: p.status, lastSeenAt: p.lastSeenAt }
    presenceByUser.value = next
  }

  async function loadMyStatus() { myStatus.value = await requireBridge().getMyStatus() }

  async function setMyStatus(status: UserPresenceStatus) { await requireBridge().setStatus(status); myStatus.value = status }

  // Member settings
  async function setMemberSettings(id: string, settings: ConversationMemberSettingsDto) {
    await requireBridge().updateMemberSettings(id, settings)
    await fetchConversations()
  }

  async function clearHistory(id: string) {
    await requireBridge().clearHistory(id)
    messagesByConv.value = { ...messagesByConv.value, [id]: [] }
  }

  /** Hide from my list (server re-surfaces it automatically on a new message). */
  async function hideConversation(id: string) {
    await requireBridge().updateMemberSettings(id, { isHidden: true })
    if (activeId.value === id) activeId.value = null
    await fetchConversations()
  }

  /** Per-user delete: wipes my history view + hides from my list. */
  async function deleteConversation(id: string) {
    await requireBridge().deleteForMe(id)
    const { [id]: _dropped, ...rest } = messagesByConv.value
    messagesByConv.value = rest
    if (activeId.value === id) activeId.value = null
    await fetchConversations()
  }

  async function setNotice(id: string, notice: string | null) { await requireBridge().updateNotice(id, notice) }

  async function searchMessages(id: string, keyword: string) { return requireBridge().searchMessages(id, { keyword, limit: 50 }) }

  async function getContactProfile(userId: string) { return requireBridge().getContactProfile(userId) }

  // Part 3: media send
  async function sendMedia(id: string, payload: { contentType: MessageContentType; fileId: string; fileName: string; fileSize: number }) {
    try {
      const msg = await requireBridge().sendMessage(id, {
        contentType: payload.contentType,
        fileId: payload.fileId,
        fileName: payload.fileName,
        fileSize: payload.fileSize,
      })
      appendMessage(id, msg)
    } catch (e) {
      // Same inline-failure treatment as text: keep the media message flagged
      // failed with a retry marker instead of a toast (the upload succeeded, only
      // the send was rejected). Re-throw so the caller skips the "sent" sound.
      pushFailed(id, { contentType: payload.contentType, fileId: payload.fileId, fileName: payload.fileName, fileSize: payload.fileSize }, e)
      throw e
    }
  }

  return {
    conversations, activeId, windowVisible, messagesByConv, loading,
    presenceByUser, myStatus, config,
    totalUnread, activeConversation, sortedConversations,
    init, setWindowVisible, fetchConversations, loadConfig, openConversation, sendText, markRead,
    appendMessage, applyIncomingMessage, applyRead, refreshUnread,
    searchContacts, startDirect, createGroup,
    getConversationDetail, addMembers, removeMember, renameGroup,
    dissolveGroup, leaveGroup, sendMedia, resendMessage,
    applyPresenceChange, loadPresence, loadMyStatus, setMyStatus,
    setMemberSettings, clearHistory, hideConversation, deleteConversation,
    setNotice, searchMessages, getContactProfile,
  }
})
