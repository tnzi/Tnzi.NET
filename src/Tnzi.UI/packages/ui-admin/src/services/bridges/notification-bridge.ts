/**
 * Notification bridge — full implementation (Phase 3 Task 3.24).
 *
 * Adapts the notification backend API to BridgeCrudContract shapes used by
 * TCrudPage-based notification pages.
 *
 * Sub-contracts:
 *   - messages      → useAdminNotificationApi (query, send, delete, retry)
 *   - templates     → /admin/notification-templates (paged CRUD, 2026-04-14
 *                     unstub). Module is pinned to "Notification" server-side;
 *                     the bridge just forwards CreateTemplateDto / UpdateTemplateDto
 *                     payloads and maps paged query shapes. Preview still
 *                     delegates to api.preview() with a synthesized request
 *                     because the dedicated preview route lives on the
 *                     generic template controller and takes content + model,
 *                     not a template id.
 *   - subscriptions → NotificationPreference-backed (2026-04-14 unstub).
 */
import {
  useAdminNotificationApi,
  useAdminNotificationPreferenceApi,
  NotificationType,
  type NotificationInfo,
  type QueryNotificationRequest,
  type NotificationPreferenceDto,
  type NotificationPreferenceQueryDto,
  type SetNotificationPreferenceDto,
} from '@tnzi/core/services/notification'
import type {
  TemplateInfoDto,
  TemplateEntityDto,
  CreateTemplateDto,
  UpdateTemplateDto,
  TemplateQueryDto,
} from '@tnzi/core/services/template'
import type { PagedList } from '@tnzi/core/types'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

/** Server-side pinned module name for notification templates. */
const NOTIFICATION_TEMPLATE_BASE = '/admin/notification-templates'

type HttpClient = Parameters<typeof useAdminNotificationApi>[0]

export interface NotificationBridgeDeps {
  /** Production path: provide HttpClient; bridge builds API internally. */
  client?: HttpClient
  /** Test path: inject mock API directly. */
  notificationApi?: ReturnType<typeof useAdminNotificationApi>
  preferenceApi?: ReturnType<typeof useAdminNotificationPreferenceApi>
}

/** messages sub-contract with extra send action */
export interface NotificationMessageContract extends BridgeCrudContract<NotificationInfo> {
  /** Resend (retry) a notification by id. */
  send(id: string): Promise<void>
}

/** Request shape for template preview / test-send actions. */
export interface NotificationTemplatePreviewRequest {
  templateName?: string | null
  /** Notification channel (defaults to Email). Serializes as the member-name string. */
  type?: NotificationType
  subject?: string | null
  content?: string | null
  variables?: Record<string, unknown> | null
  /** Used only by sendTest — leave empty for preview-only. */
  recipientAddress?: string | null
}

/** Rendered preview response (subject + content + isHtml flag). */
export interface NotificationTemplatePreviewResult {
  subject?: string | null
  content: string
  /** True when `content` is HTML and should be injected with v-html;
   *  false when it's plain text and must be escaped for safe display. */
  isHtml: boolean
}

/** templates sub-contract — full CRUD against /admin/notification-templates plus preview + test-send. */
export interface NotificationTemplateContract extends BridgeCrudContract<TemplateInfoDto> {
  /**
   * Preview notification rendering. Sends a CreateNotificationRequest to
   * /admin/notifications/preview with the template name + variables; the
   * backend renders the template via ITemplateRenderService and returns
   * the materialized subject + content without persisting anything.
   */
  preview(request: NotificationTemplatePreviewRequest): Promise<NotificationTemplatePreviewResult>
  /**
   * Send a test notification using the template. Creates and dispatches
   * a real notification (POST /admin/notifications/create-and-send) to
   * the supplied recipient — use with throwaway addresses only.
   */
  sendTest(request: NotificationTemplatePreviewRequest): Promise<void>
}

export interface NotificationBridge {
  messages: NotificationMessageContract
  templates: NotificationTemplateContract
  /**
   * NotificationPreference-backed "subscriptions" contract. Wired to
   * /admin/notification-preferences (2026-04-14 unstub). Create/update both
   * upsert via PUT /admin/notification-preferences/user/{userId}.
   */
  subscriptions: BridgeCrudContract<NotificationPreferenceDto>
}

const backendGapReject = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`notification-bridge: ${name} — backend gap, no endpoint available`))

export function createNotificationBridge(deps: NotificationBridgeDeps = {}): NotificationBridge {
  const notificationApi = deps.notificationApi ?? (deps.client ? useAdminNotificationApi(deps.client) : null)
  const preferenceApi = deps.preferenceApi ?? (deps.client ? useAdminNotificationPreferenceApi(deps.client) : null)

  if (!notificationApi) {
    const noFetch = backendGapReject('no deps provided')
    return {
      messages: {
        fetch: noFetch as never,
        create: backendGapReject('messages.create'),
        update: backendGapReject('messages.update'),
        delete: backendGapReject('messages.delete'),
        send: backendGapReject('messages.send'),
      },
      templates: {
        fetch: backendGapReject('templates.fetch'),
        create: backendGapReject('templates.create'),
        update: backendGapReject('templates.update'),
        delete: backendGapReject('templates.delete'),
        preview: backendGapReject('templates.preview') as never,
        sendTest: backendGapReject('templates.sendTest'),
      },
      subscriptions: {
        fetch: backendGapReject('subscriptions.fetch'),
        create: backendGapReject('subscriptions.create'),
        update: backendGapReject('subscriptions.update'),
        delete: backendGapReject('subscriptions.delete'),
      },
    }
  }

  // Narrowed references for closures
  const api = notificationApi
  const prefApi = preferenceApi

  async function fetchMessages(query: CrudPageQuery): Promise<CrudPageResult<NotificationInfo>> {
    const params = mapQueryToListRequest(query) as unknown as QueryNotificationRequest
    const result = unwrap<{ items: NotificationInfo[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await api.query(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const messages: NotificationMessageContract = {
    fetch: fetchMessages,
    // Messages are sent notifications — create/update are not supported from admin.
    create: backendGapReject('messages.create — notifications are sent, not CRUDed; use send/createAndSend'),
    update: backendGapReject('messages.update — notifications are immutable once created'),
    delete: (ids: string[]) => api.batchDelete(ids).then((res) => ensureOk(res)),
    send: (id: string) => api.send(id).then((res) => ensureOk(res)),
  }

  // /admin/notification-templates CRUD (2026-04-14 unstub). Uses the shared
  // HttpClient directly because @tnzi/core does not yet expose a dedicated
  // useAdminNotificationTemplateApi factory — the endpoint is a thin
  // notification-scoped view over the generic template store and pins
  // Module="Notification" server-side, so the bridge only has to forward
  // the standard TemplateDto shapes.
  const client = deps.client
  const templates: NotificationTemplateContract = client
    ? {
        fetch: async (query: CrudPageQuery): Promise<CrudPageResult<TemplateInfoDto>> => {
          const params = { ...mapQueryToListRequest(query), includeFileSource: true } as unknown as TemplateQueryDto
          const result = unwrap<PagedList<TemplateInfoDto>>(
            await client.get<PagedList<TemplateInfoDto>>(NOTIFICATION_TEMPLATE_BASE, { params }),
          )
          return pagedResult({
            items: result.items ?? [],
            totalCount: result.totalCount ?? 0,
            pageIndex: result.pageIndex ?? query.pageIndex,
            pageSize: result.pageSize ?? query.pageSize,
          })
        },
        create: async (data) => {
          const payload = data as unknown as CreateTemplateDto
          const result = unwrap<TemplateEntityDto>(
            await client.post<TemplateEntityDto>(NOTIFICATION_TEMPLATE_BASE, payload),
          )
          // TemplateEntityDto → TemplateInfoDto widening is structural; the
          // list view only consumes Info fields, the form modal reads back
          // the full entity on edit.
          return result as unknown as TemplateInfoDto
        },
        update: async (id, data) => {
          const payload = data as unknown as UpdateTemplateDto
          const result = unwrap<TemplateEntityDto>(
            await client.put<TemplateEntityDto>(`${NOTIFICATION_TEMPLATE_BASE}/${encodeURIComponent(String(id))}`, payload),
          )
          return result as unknown as TemplateInfoDto
        },
        delete: async (ids) => {
          if (ids.length === 1) {
            ensureOk(await client.delete(`${NOTIFICATION_TEMPLATE_BASE}/${encodeURIComponent(String(ids[0]))}`))
            return
          }
          ensureOk(await client.delete(`${NOTIFICATION_TEMPLATE_BASE}/batch`, { body: ids.map(String) }))
        },
        // Preview hits POST /admin/notifications/preview which renders the
        // template via ITemplateRenderService without creating or sending a
        // notification. Page passes templateName + variables; the backend
        // looks up the template by name + module="Notification".
        preview: async (req: NotificationTemplatePreviewRequest): Promise<NotificationTemplatePreviewResult> => {
          const result = unwrap(
            await api.preview({
              type: req.type ?? NotificationType.Email,
              subject: req.subject ?? 'Preview',
              content: req.content ?? '',
              templateName: req.templateName ?? undefined,
              recipients: [],
              templateVariables: req.variables ?? undefined,
            }),
          )
          return {
            subject: (result as { subject?: string | null }).subject ?? null,
            content: (result as { content?: string }).content ?? '',
            isHtml: (result as { isHtml?: boolean }).isHtml ?? false,
          }
        },
        // Test-send creates and dispatches a real notification — use a
        // throwaway recipient address. Pin templateName + variables on the
        // CreateNotificationRequest so the backend renders + delivers
        // exactly the template the admin clicked.
        sendTest: async (req: NotificationTemplatePreviewRequest): Promise<void> => {
          if (!req.recipientAddress) {
            throw new Error('sendTest: recipientAddress is required.')
          }
          ensureOk(await api.createAndSend({
            type: req.type ?? NotificationType.Email,
            subject: req.subject ?? 'Test Send',
            content: req.content ?? '',
            templateName: req.templateName ?? undefined,
            templateVariables: req.variables ?? undefined,
            recipients: [{ address: req.recipientAddress }],
          }))
        },
      }
    : {
        fetch: backendGapReject('templates.fetch — no HttpClient (deps.client) provided') as never,
        create: backendGapReject('templates.create — no HttpClient (deps.client) provided'),
        update: backendGapReject('templates.update — no HttpClient (deps.client) provided'),
        delete: backendGapReject('templates.delete — no HttpClient (deps.client) provided'),
        preview: async (req: NotificationTemplatePreviewRequest): Promise<NotificationTemplatePreviewResult> => {
          const result = unwrap(
            await api.preview({
              type: req.type ?? NotificationType.Email,
              subject: req.subject ?? 'Preview',
              content: req.content ?? '',
              templateName: req.templateName ?? undefined,
              recipients: [],
              templateVariables: req.variables ?? undefined,
            }),
          )
          return {
            subject: (result as { subject?: string | null }).subject ?? null,
            content: (result as { content?: string }).content ?? '',
            isHtml: (result as { isHtml?: boolean }).isHtml ?? false,
          }
        },
        sendTest: async (req: NotificationTemplatePreviewRequest): Promise<void> => {
          if (!req.recipientAddress) {
            throw new Error('sendTest: recipientAddress is required.')
          }
          ensureOk(await api.createAndSend({
            type: req.type ?? NotificationType.Email,
            subject: req.subject ?? 'Test Send',
            content: req.content ?? '',
            templateName: req.templateName ?? undefined,
            templateVariables: req.variables ?? undefined,
            recipients: [{ address: req.recipientAddress }],
          }))
        },
      }

  // Subscriptions → NotificationPreference. Wired 2026-04-14 to the canonical
  // paged GET /admin/notification-preferences endpoint when `prefApi` is
  // available; otherwise falls through to backend-gap rejects so legacy
  // tests that only inject `notificationApi` still pass.
  //
  // Create/update both upsert via PUT /user/{userId} because the backend key
  // is (userId, channel, category) rather than a synthetic id — we still
  // expose a BridgeCrudContract shape so TCrudPage can consume it uniformly.
  const subscriptions: BridgeCrudContract<NotificationPreferenceDto> = prefApi
    ? {
        fetch: async (query: CrudPageQuery): Promise<CrudPageResult<NotificationPreferenceDto>> => {
          const filters = (query.filters ?? {}) as Record<string, unknown>
          const orderBy = query.sortField
            ? `${query.sortField}${query.sortOrder === 'desc' ? ' desc' : ''}`
            : undefined
          const params: NotificationPreferenceQueryDto = {
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            orderBy,
            userId: typeof filters.userId === 'string' ? filters.userId : undefined,
            channel: typeof filters.channel === 'string' ? filters.channel : undefined,
            category: typeof filters.category === 'string' ? filters.category : undefined,
            isEnabled: typeof filters.isEnabled === 'boolean' ? filters.isEnabled : undefined,
          }
          const result = unwrap<{ items: NotificationPreferenceDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
            await prefApi.getPagedList(params),
          )
          return pagedResult({
            items: result.items ?? [],
            totalCount: result.totalCount ?? 0,
            pageIndex: result.pageIndex ?? query.pageIndex,
            pageSize: result.pageSize ?? query.pageSize,
          })
        },
        create: async (data) => {
          const input = data as unknown as NotificationPreferenceDto & { userId: string }
          const upsert: SetNotificationPreferenceDto = {
            channel: input.channel,
            category: input.category,
            isEnabled: input.isEnabled ?? true,
            quietHoursStart: input.quietHoursStart,
            quietHoursEnd: input.quietHoursEnd,
            maxFrequencyPerHour: input.maxFrequencyPerHour,
          }
          return unwrap(await prefApi.setPreference(input.userId, upsert)) as NotificationPreferenceDto
        },
        update: async (_id, data) => {
          const input = data as unknown as NotificationPreferenceDto & { userId: string }
          const upsert: SetNotificationPreferenceDto = {
            channel: input.channel,
            category: input.category,
            isEnabled: input.isEnabled,
            quietHoursStart: input.quietHoursStart,
            quietHoursEnd: input.quietHoursEnd,
            maxFrequencyPerHour: input.maxFrequencyPerHour,
          }
          return unwrap(await prefApi.setPreference(input.userId, upsert)) as NotificationPreferenceDto
        },
        delete: async (ids) => {
          for (const id of ids) {
            ensureOk(await prefApi.delete(String(id)))
          }
        },
      }
    : {
        fetch: backendGapReject('subscriptions.fetch — no preferenceApi provided') as never,
        create: backendGapReject('subscriptions.create — no preferenceApi provided'),
        update: backendGapReject('subscriptions.update — no preferenceApi provided'),
        delete: backendGapReject('subscriptions.delete — no preferenceApi provided'),
      }

  return { messages, templates, subscriptions }
}
