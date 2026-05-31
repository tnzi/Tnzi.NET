/**
 * Chat bridge — full implementation (Phase 3 Task 3.28 / 2026-04-14 sessions
 * unstub).
 *
 * Adapts the chat backend API to BridgeCrudContract shapes used by
 * TCrudPage-based chat pages.
 *
 * Sub-contracts:
 *   - messages  → useChatAdminApi (query, delete, batchDelete)
 *   - sessions  → useAdminChatSessionApi (full CRUD against
 *                 /admin/chat-sessions; admin-curated session groupings).
 *
 * Special contract:
 *   messages.fetchBySession(sessionId, query) — filters admin query by senderId
 *   or a sessionId filter field. The backend AdminMessageQueryDto supports
 *   arbitrary filter passthrough; sessionId is forwarded as a query parameter.
 *
 * Read-only admin surface:
 *   messages.create/update remain rejected — admin view is read-only; messages
 *   are created by users, not admins.
 */
import {
  useChatAdminApi,
  useAdminChatSessionApi,
  type MessageListItemDto,
  type AdminMessageQueryDto,
  type ChatSessionListItemDto,
  type ChatSessionDto,
  type CreateChatSessionDto,
  type UpdateChatSessionDto,
  type ChatSessionQueryDto,
  ChatSessionStatus,
} from '@tnzi/core/services/chat'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useChatAdminApi>[0]

export interface ChatBridgeDeps {
  /** Production path: provide HttpClient; bridge builds API internally. */
  client?: HttpClient
  /** Test path: inject mock API directly. */
  chatAdminApi?: ReturnType<typeof useChatAdminApi>
  /** Test path: inject mock session API directly. */
  chatSessionApi?: ReturnType<typeof useAdminChatSessionApi>
}

/** messages sub-contract with optional fetchBySession */
export interface ChatMessageContract extends BridgeCrudContract<MessageListItemDto> {
  /**
   * Fetch messages scoped to a specific session (admin detail panel).
   * Forwards sessionId as an extra filter on the admin query.
   */
  fetchBySession(sessionId: string, query: CrudPageQuery): Promise<CrudPageResult<MessageListItemDto>>
}

export interface ChatBridge {
  /** Admin-curated chat session groupings. /admin/chat-sessions CRUD. */
  sessions: BridgeCrudContract<ChatSessionListItemDto>
  messages: ChatMessageContract
}

const backendGapReject = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`chat-bridge: ${name} — backend gap, no endpoint available`))

export function createChatBridge(deps: ChatBridgeDeps = {}): ChatBridge {
  const chatAdminApi = deps.chatAdminApi ?? (deps.client ? useChatAdminApi(deps.client) : null)
  const sessionApi = deps.chatSessionApi ?? (deps.client ? useAdminChatSessionApi(deps.client) : null)

  if (!chatAdminApi) {
    const noFetch = backendGapReject('no deps provided')
    return {
      sessions: {
        fetch: noFetch as never,
        create: backendGapReject('sessions.create'),
        update: backendGapReject('sessions.update'),
        delete: backendGapReject('sessions.delete'),
      },
      messages: {
        fetch: noFetch as never,
        create: backendGapReject('messages.create'),
        update: backendGapReject('messages.update'),
        delete: backendGapReject('messages.delete'),
        fetchBySession: backendGapReject('messages.fetchBySession'),
      },
    }
  }

  // Narrowed reference for closures
  const api = chatAdminApi

  async function fetchMessages(
    query: CrudPageQuery,
    extraFilters?: Record<string, unknown>,
  ): Promise<CrudPageResult<MessageListItemDto>> {
    const base = mapQueryToListRequest(query)
    const params = { ...base, ...extraFilters } as unknown as AdminMessageQueryDto
    const result = unwrap<{ items: MessageListItemDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await api.query(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const messages: ChatMessageContract = {
    fetch: (query) => fetchMessages(query),
    // Admin view is read-only; messages are created/updated by users only.
    create: backendGapReject('messages.create — admin view is read-only; use user-facing chat API'),
    update: backendGapReject('messages.update — admin view is read-only; messages are immutable for admin'),
    delete: (ids: string[]) => api.batchDelete(ids).then(() => undefined),
    fetchBySession: (sessionId, query) => fetchMessages(query, { sessionId }),
  }

  const sessions: BridgeCrudContract<ChatSessionListItemDto> = sessionApi
    ? {
        fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ChatSessionListItemDto>> => {
          const base = mapQueryToListRequest(query)
          const filters = (query.filters ?? {}) as Record<string, unknown>
          const params: ChatSessionQueryDto = {
            ...(base as unknown as ChatSessionQueryDto),
            status: typeof filters.status === 'number' ? (filters.status as ChatSessionStatus) : undefined,
            keyword: typeof filters.keyword === 'string' ? (filters.keyword as string) : undefined,
            participantId: typeof filters.participantId === 'string' ? (filters.participantId as string) : undefined,
          }
          const result = unwrap<{ items: ChatSessionListItemDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
            await sessionApi.getList(params),
          )
          return pagedResult({
            items: result.items ?? [],
            totalCount: result.totalCount ?? 0,
            pageIndex: result.pageIndex ?? query.pageIndex,
            pageSize: result.pageSize ?? query.pageSize,
          })
        },
        create: async (data) => {
          const payload = data as unknown as CreateChatSessionDto
          const result = unwrap<ChatSessionDto>(await sessionApi.create(payload))
          // ChatSessionDto → ChatSessionListItemDto is structurally compatible for
          // the fields the list view consumes.
          return result as unknown as ChatSessionListItemDto
        },
        update: async (id, data) => {
          const payload = data as unknown as UpdateChatSessionDto
          const result = unwrap<ChatSessionDto>(
            await sessionApi.update(String(id), payload),
          )
          return result as unknown as ChatSessionListItemDto
        },
        delete: async (ids) => {
          if (ids.length === 1) {
            await sessionApi.delete(String(ids[0]))
            return
          }
          await sessionApi.deleteBatch(ids.map(String))
        },
      }
    : {
        fetch: backendGapReject('sessions.fetch — no HttpClient (deps.client) provided') as never,
        create: backendGapReject('sessions.create — no HttpClient (deps.client) provided'),
        update: backendGapReject('sessions.update — no HttpClient (deps.client) provided'),
        delete: backendGapReject('sessions.delete — no HttpClient (deps.client) provided'),
      }

  return { sessions, messages }
}
