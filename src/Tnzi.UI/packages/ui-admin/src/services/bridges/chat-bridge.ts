/**
 * Chat admin bridge — wraps `useChatBroadcastApi` + `useChatAdminApi` (core)
 * for the admin maintenance pages (Overview / Conversations / Presence /
 * Broadcast). The IM chat window uses the separate `chat-im-bridge`.
 *
 * Unwraps `ApiResult<T>` envelopes via `unwrapResult`. Paged listing maps the
 * `CrudPageQuery` to the backend `AdminConversationQueryDto` via the shared
 * `mapQueryToListRequest` helper.
 */
import {
  useChatBroadcastApi,
  useChatAdminApi,
  type BroadcastDto,
  type ChatStatisticsDto,
  type AdminConversationQueryDto,
  type AdminConversationListItemDto,
  type AdminConversationDetailDto,
  type MessageThreadDto,
  type PresenceOverviewDto,
  type PresenceOverviewQueryDto,
  type BroadcastLogDto,
} from '@tnzi/core/services/chat'
import { ensureOk, unwrapResult as unwrap, mapQueryToListRequest } from '../_mappers'
import type { CrudPageQuery, CrudPageResult } from '../types'

type HttpClient = Parameters<typeof useChatBroadcastApi>[0]

export interface ChatBridge {
  /** Send a broadcast (system notification) to all users or a targeted subset. */
  broadcast(dto: BroadcastDto): Promise<number>
  /** Global statistics overview. */
  statistics(): Promise<ChatStatisticsDto>
  conversations: {
    /** Paged listing of all conversations (filter by type / keyword / participant). */
    fetch(query: CrudPageQuery): Promise<CrudPageResult<AdminConversationListItemDto>>
    /** Conversation detail (members + metadata). */
    detail(id: string): Promise<AdminConversationDetailDto>
    /** Conversation messages (admin view, cursor paging). */
    messages(id: string, params: { before?: string; limit?: number }): Promise<MessageThreadDto>
    /** Delete (dissolve) one or more conversations. */
    delete(ids: string[]): Promise<void>
  }
  /** Force-recall any single message. */
  deleteMessage(messageId: string): Promise<void>
  /** Presence overview (effective online distribution + per-user detail). */
  presence(query?: PresenceOverviewQueryDto): Promise<PresenceOverviewDto>
  /** Broadcast history (paged, most recent first). */
  broadcasts(params?: { pageIndex?: number; pageSize?: number }): Promise<CrudPageResult<BroadcastLogDto>>
}

export interface ChatBridgeDeps {
  /** Production path: provide HttpClient; bridge builds the APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. */
  broadcastApi?: ReturnType<typeof useChatBroadcastApi>
  adminApi?: ReturnType<typeof useChatAdminApi>
}

export function createChatBridge(deps: ChatBridgeDeps = {}): ChatBridge {
  const broadcastApi = deps.broadcastApi ?? (deps.client ? useChatBroadcastApi(deps.client) : null)
  const adminApi = deps.adminApi ?? (deps.client ? useChatAdminApi(deps.client) : null)

  const noClient = (m: string) => () =>
    Promise.reject(new Error(`chat-bridge: ${m} — no HttpClient (deps.client) provided`))

  return {
    broadcast: broadcastApi
      ? async (dto) => unwrap<number>(await broadcastApi.broadcast(dto))
      : noClient('broadcast'),

    statistics: adminApi
      ? async () => unwrap(await adminApi.getStatistics())
      : noClient('statistics'),

    conversations: {
      fetch: adminApi
        ? async (query) =>
            unwrap(await adminApi.queryConversations(mapQueryToListRequest(query) as unknown as AdminConversationQueryDto))
        : noClient('conversations.fetch'),
      detail: adminApi
        ? async (id) => unwrap(await adminApi.getConversation(id))
        : noClient('conversations.detail'),
      messages: adminApi
        ? async (id, params) => unwrap(await adminApi.getConversationMessages(id, params))
        : noClient('conversations.messages'),
      delete: adminApi
        ? async (ids) => {
            const results = await Promise.all(ids.map((id) => adminApi.deleteConversation(id)))
            results.forEach((res) => ensureOk(res))
          }
        : noClient('conversations.delete'),
    },

    deleteMessage: adminApi
      ? async (messageId) => { ensureOk(await adminApi.deleteMessage(messageId)) }
      : noClient('deleteMessage'),

    presence: adminApi
      ? async (query) => unwrap(await adminApi.getPresenceOverview(query))
      : noClient('presence'),

    broadcasts: adminApi
      ? async (params) => unwrap(await adminApi.getBroadcasts(params))
      : noClient('broadcasts'),
  }
}
