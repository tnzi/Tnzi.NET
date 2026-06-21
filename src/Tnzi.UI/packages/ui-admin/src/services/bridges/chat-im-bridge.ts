/**
 * Chat IM bridge — wraps `useChatImApi` (core) for the store layer.
 *
 * Unwraps `ApiResult<T>` envelopes via `unwrapResult` from `../_mappers`
 * so callers receive plain data types. Void-returning methods (markRead,
 * mute, deleteMessage, addMembers, removeMember, renameGroup, dissolveGroup,
 * leaveGroup) are called with a plain `await` — no unwrap needed.
 */
import { useChatImApi, usePresenceApi } from '@tnzi/core/services/chat'
import type {
  ConversationListItemDto,
  ConversationDto,
  ChatMessageDto,
  MessageThreadDto,
  SendMessageDto,
  CreateGroupDto,
  ChatContactDto,
  ConversationMemberSettingsDto,
  ChatContactProfileDto,
  UserPresenceDto,
  UserPresenceStatus,
} from '@tnzi/core/services/chat'
import { unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useChatImApi>[0]
type ChatImApi = ReturnType<typeof useChatImApi>

export interface ChatImBridge {
  listConversations(): Promise<ConversationListItemDto[]>
  getUnreadCount(): Promise<number>
  getOrCreateDirect(userId: string): Promise<ConversationDto>
  getConversation(id: string): Promise<ConversationDto>
  getMessages(id: string, params: { before?: string; limit?: number }): Promise<MessageThreadDto>
  sendMessage(id: string, data: SendMessageDto): Promise<ChatMessageDto>
  markRead(id: string): Promise<void>
  mute(id: string, muted: boolean): Promise<void>
  deleteMessage(messageId: string): Promise<void>
  createGroup(data: CreateGroupDto): Promise<ConversationDto>
  addMembers(id: string, userIds: string[]): Promise<void>
  removeMember(id: string, userId: string): Promise<void>
  renameGroup(id: string, title: string): Promise<void>
  dissolveGroup(id: string): Promise<void>
  leaveGroup(id: string): Promise<void>
  searchContacts(keyword: string): Promise<ChatContactDto[]>
  updateMemberSettings(id: string, settings: ConversationMemberSettingsDto): Promise<void>
  clearHistory(id: string): Promise<void>
  searchMessages(id: string, params: { keyword: string; before?: string; limit?: number }): Promise<MessageThreadDto>
  updateNotice(id: string, notice: string | null): Promise<void>
  getContactProfile(userId: string): Promise<ChatContactProfileDto>
  setStatus(status: UserPresenceStatus): Promise<void>
  getMyStatus(): Promise<UserPresenceStatus>
  getPresence(userIds: string[]): Promise<UserPresenceDto[]>
}

export function createChatImBridge(deps: { client?: HttpClient; api?: ChatImApi }): ChatImBridge {
  const api = deps.api ?? (deps.client ? useChatImApi(deps.client) : null)
  if (!api) throw new Error('chat-im-bridge: client or api required')
  const presence = deps.client ? usePresenceApi(deps.client) : null
  return {
    listConversations: async () => unwrap(await api.getConversations()),
    getUnreadCount: async () => unwrap(await api.getUnreadCount()),
    getOrCreateDirect: async (userId) => unwrap(await api.getOrCreateDirect(userId)),
    getConversation: async (id) => unwrap(await api.getConversation(id)),
    getMessages: async (id, params) => unwrap(await api.getMessages(id, params)),
    sendMessage: async (id, data) => unwrap(await api.sendMessage(id, data)),
    markRead: async (id) => { await api.markRead(id) },
    mute: async (id, muted) => { await api.mute(id, muted) },
    deleteMessage: async (messageId) => { await api.deleteMessage(messageId) },
    createGroup: async (data) => unwrap(await api.createGroup(data)),
    addMembers: async (id, userIds) => { await api.addMembers(id, userIds) },
    removeMember: async (id, userId) => { await api.removeMember(id, userId) },
    renameGroup: async (id, title) => { await api.renameGroup(id, title) },
    dissolveGroup: async (id) => { await api.dissolveGroup(id) },
    leaveGroup: async (id) => { await api.leaveGroup(id) },
    searchContacts: async (keyword) => unwrap(await api.searchContacts(keyword)),
    updateMemberSettings: async (id, s) => { await api.updateMemberSettings(id, s) },
    clearHistory: async (id) => { await api.clearHistory(id) },
    searchMessages: async (id, p) => unwrap(await api.searchMessages(id, p)),
    updateNotice: async (id, notice) => { await api.updateNotice(id, notice) },
    getContactProfile: async (userId) => unwrap(await api.getContactProfile(userId)),
    setStatus: async (status) => { await presence!.setStatus(status) },
    getMyStatus: async () => unwrap(await presence!.getMyStatus()),
    getPresence: async (userIds) => unwrap(await presence!.getPresence(userIds)),
  }
}
