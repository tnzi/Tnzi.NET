import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { ConversationListItemDto, ChatMessageDto, PresenceChangedPayload, ConversationMemberSettingsDto } from '@tnzi/core/services/chat'
import type { ChatImBridge } from '../services/bridges/chat-im-bridge'
import type { NewMessagePayload, MessageReadPayload } from '@tnzi/core/services/chat'
import { MessageContentType, UserPresenceStatus } from '@tnzi/core/services/chat'
import { useAdminAuthStore } from './useAdminAuthStore'

export const useChatStore = defineStore('admin-chat', () => {
  let bridge: ChatImBridge | null = null

  const conversations = ref<ConversationListItemDto[]>([])
  const activeId = ref<string | null>(null)
  const messagesByConv = ref<Record<string, ChatMessageDto[]>>({})
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
    try { conversations.value = await requireBridge().listConversations() }
    finally { loading.value = false }
  }

  async function openConversation(id: string) {
    activeId.value = id
    const thread = await requireBridge().getMessages(id, { limit: 30 })
    messagesByConv.value = { ...messagesByConv.value, [id]: thread.messages }
    await markRead(id)
    const conv = conversations.value.find(c => c.id === id)
    if (conv?.peerUserId) loadPresence([conv.peerUserId]).catch(() => undefined)
  }

  async function sendText(id: string, content: string) {
    const msg = await requireBridge().sendMessage(id, { contentType: MessageContentType.Text, content })
    appendMessage(id, msg)
  }

  async function markRead(id: string) {
    await requireBridge().markRead(id)
    const conv = conversations.value.find(c => c.id === id)
    if (conv) conv.unreadCount = 0
  }

  function appendMessage(id: string, msg: ChatMessageDto) {
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

  function applyIncomingMessage(p: NewMessagePayload, myId?: string) {
    const isActive = activeId.value === p.conversationId
    const fromOther = p.senderId && p.senderId !== (myId ?? useAdminAuthStore().userInfo?.id)
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
    presenceByUser.value = { ...presenceByUser.value, [p.userId]: { status: p.status as UserPresenceStatus, lastSeenAt: p.lastSeenAt } }
    const conv = conversations.value.find(c => c.peerUserId === p.userId)
    if (conv) conversations.value = conversations.value.map(c => c.peerUserId === p.userId ? { ...c, peerStatus: p.status as UserPresenceStatus } : c)
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

  async function setNotice(id: string, notice: string | null) { await requireBridge().updateNotice(id, notice) }

  async function searchMessages(id: string, keyword: string) { return requireBridge().searchMessages(id, { keyword, limit: 50 }) }

  async function getContactProfile(userId: string) { return requireBridge().getContactProfile(userId) }

  // Part 3: media send
  async function sendMedia(id: string, payload: { contentType: MessageContentType; fileId: string; fileName: string; fileSize: number }) {
    const msg = await requireBridge().sendMessage(id, {
      contentType: payload.contentType,
      fileId: payload.fileId,
      fileName: payload.fileName,
      fileSize: payload.fileSize,
    })
    appendMessage(id, msg)
  }

  return {
    conversations, activeId, messagesByConv, loading,
    presenceByUser, myStatus,
    totalUnread, activeConversation, sortedConversations,
    init, fetchConversations, openConversation, sendText, markRead,
    appendMessage, applyIncomingMessage, applyRead, refreshUnread,
    searchContacts, startDirect, createGroup,
    getConversationDetail, addMembers, removeMember, renameGroup,
    dissolveGroup, leaveGroup, sendMedia,
    applyPresenceChange, loadPresence, loadMyStatus, setMyStatus,
    setMemberSettings, clearHistory, setNotice, searchMessages, getContactProfile,
  }
})
