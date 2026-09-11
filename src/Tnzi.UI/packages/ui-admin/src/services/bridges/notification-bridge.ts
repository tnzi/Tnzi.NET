/**
 * Notification bridge - full implementation (Phase 3 Task 3.24).
 *
 * Adapts the notification backend API to BridgeCrudContract shapes used by
 * TCrudPage-based notification pages.
 *
 * Sub-contracts:
 *   - messages      → useAdminNotificationApi (query, send, cancel, batch
 *                     cancel, delivery report, delete)
 *   - templates     → useAdminNotificationTemplateApi. Module is pinned to
 *                     "Notification" server-side; the bridge just forwards
 *                     CreateTemplateDto / UpdateTemplateDto payloads and maps
 *                     paged query shapes. Preview still delegates to
 *                     api.preview() with a synthesized request because the
 *                     dedicated preview route lives on the generic template
 *                     controller and takes content + model, not a template id.
 *   - subscriptions → NotificationPreference-backed (2026-04-14 unstub).
 */
import {
  useAdminNotificationApi,
  useAdminNotificationPreferenceApi,
  useAdminNotificationTemplateApi,
  useUnsubscribeApi,
  useAdminPushDeviceApi,
  NotificationType,
  type NotificationInfo,
  type QueryNotificationRequest,
  type NotificationPreferenceDto,
  type NotificationPreferenceQueryDto,
  type SetNotificationPreferenceDto,
  type DeliveryReportDto,
  type DevicePlatform,
  type PushDeviceDto,
  type PushDeviceQueryDto,
  type UnsubscribePreviewDto,
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
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap, unwrapOk } from '../_mappers'

type HttpClient = Parameters<typeof useAdminNotificationApi>[0]

export interface NotificationBridgeDeps {
  /** Production path: provide HttpClient; bridge builds API internally. */
  client?: HttpClient
  /** Test path: inject mock API directly. */
  notificationApi?: ReturnType<typeof useAdminNotificationApi>
  preferenceApi?: ReturnType<typeof useAdminNotificationPreferenceApi>
  templateApi?: ReturnType<typeof useAdminNotificationTemplateApi>
  deviceApi?: ReturnType<typeof useAdminPushDeviceApi>
}

/** messages sub-contract with extra send / cancel / delivery-report actions */
export interface NotificationMessageContract extends BridgeCrudContract<NotificationInfo> {
  /** Resend (retry) a notification by id. */
  send(id: string): Promise<void>
  /** Stop a notification that has not gone out yet. */
  cancel(id: string): Promise<void>
  /** Stop several at once; resolves to the number actually cancelled. */
  batchCancel(ids: string[]): Promise<number>
  /** Per-recipient delivery breakdown for one notification. */
  getDeliveryReport(id: string): Promise<DeliveryReportDto>
}

/** Request shape for template preview / test-send actions. */
export interface NotificationTemplatePreviewRequest {
  templateName?: string | null
  /** Notification channel (defaults to Email). Serializes as the member-name string. */
  type?: NotificationType
  subject?: string | null
  content?: string | null
  variables?: Record<string, unknown> | null
  /** Used only by sendTest - leave empty for preview-only. */
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

/** templates sub-contract - full CRUD against /admin/notification-templates plus preview + test-send. */
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
   * the supplied recipient - use with throwaway addresses only.
   */
  sendTest(request: NotificationTemplatePreviewRequest): Promise<void>
}

/**
 * The recipient-facing unsubscribe flow - ANONYMOUS, no permission codes.
 *
 * Kept on this bridge (rather than called from the page) so the landing page
 * keeps the page -> bridge -> core layering every other page follows, and so
 * its tests can `vi.mock` this module.
 */
export interface NotificationPublicUnsubscribeContract {
  /**
   * Echo back what this link would unsubscribe. No side effects.
   *
   * Resolves to `null` for every unusable link - expired secret rotation,
   * tampered payload, malformed token - and for a network failure too. To the
   * recipient those are one situation: there is nothing they can do here.
   */
  preview(token: string): Promise<UnsubscribePreviewDto | null>
  /** Act on the link. Resolves to false when the link is not usable. */
  unsubscribe(token: string, reason?: string): Promise<boolean>
  /** Undo, for someone who clicked by mistake. */
  resubscribe(token: string): Promise<boolean>
}

export interface NotificationBridge {
  messages: NotificationMessageContract
  templates: NotificationTemplateContract
  /** Recipient-facing one-click unsubscribe (anonymous). */
  publicUnsubscribe: NotificationPublicUnsubscribeContract
  /**
   * NotificationPreference-backed "subscriptions" contract. Wired to
   * /admin/notification-preferences (2026-04-14 unstub). Create/update both
   * upsert via PUT /admin/notification-preferences/user/{userId}.
   */
  subscriptions: BridgeCrudContract<NotificationPreferenceDto>

  /**
   * Push device registry, backed by /admin/notification-devices.
   *
   * ★ Read + delete only. Registration is a **client** action - the device
   * posts its own token - so an operator-typed row would match no real device
   * and every push to it would fail forever with nobody knowing why. The
   * create/update members therefore reject rather than call a nonexistent
   * endpoint.
   *
   * ★ Only present when the app loads the optional `Tnzi.Notification.Push`
   * module; an email-only deployment has neither the table nor these routes.
   */
  devices: BridgeCrudContract<PushDeviceDto>
}

const backendGapReject = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`notification-bridge: ${name} - backend gap, no endpoint available`))

export function createNotificationBridge(deps: NotificationBridgeDeps = {}): NotificationBridge {
  const notificationApi = deps.notificationApi ?? (deps.client ? useAdminNotificationApi(deps.client) : null)
  const preferenceApi = deps.preferenceApi ?? (deps.client ? useAdminNotificationPreferenceApi(deps.client) : null)
  const templateApi = deps.templateApi ?? (deps.client ? useAdminNotificationTemplateApi(deps.client) : null)
  const deviceApi = deps.deviceApi ?? (deps.client ? useAdminPushDeviceApi(deps.client) : null)

  if (!notificationApi) {
    const noFetch = backendGapReject('no deps provided')
    return {
      messages: {
        fetch: noFetch as never,
        create: backendGapReject('messages.create'),
        update: backendGapReject('messages.update'),
        delete: backendGapReject('messages.delete'),
        send: backendGapReject('messages.send'),
        cancel: backendGapReject('messages.cancel'),
        batchCancel: backendGapReject('messages.batchCancel'),
        getDeliveryReport: backendGapReject('messages.getDeliveryReport') as never,
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
      devices: {
        fetch: backendGapReject('devices.fetch'),
        create: backendGapReject('devices.create'),
        update: backendGapReject('devices.update'),
        delete: backendGapReject('devices.delete'),
      },
      // ★ The unsubscribe stubs resolve rather than reject: the landing page
      // reads "cannot act on this link", which is exactly what it should show
      // when there is no client to ask with. A rejection there would surface as
      // an unhandled error on a page a recipient reached from an email.
      publicUnsubscribe: {
        preview: async () => null,
        unsubscribe: async () => false,
        resubscribe: async () => false,
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
    // Messages are sent notifications - create/update are not supported from admin.
    create: backendGapReject('messages.create - notifications are sent, not CRUDed; use send/createAndSend'),
    update: backendGapReject('messages.update - notifications are immutable once created'),
    delete: (ids: string[]) => api.batchDelete(ids).then((res) => ensureOk(res)),
    send: (id: string) => api.send(id).then((res) => ensureOk(res)),
    // Cancel stops a send that has not gone out yet (Pending / Scheduled /
    // Sending). It is not the inverse of send and it is not delete: the row
    // stays, and its recipients are marked Cancelled so the record still shows
    // that this notification was created and deliberately stopped.
    cancel: (id: string) => api.cancel(id).then((res) => ensureOk(res)),
    // ★ ensureOk BEFORE unwrap on both of these. `unwrapResult` is tolerant: on a
    // failure envelope it hands back `data` (undefined) without throwing, so the
    // caller's error branch would be unreachable and a refused request would read
    // as "no data" - the page would then show its empty state for a 403.
    batchCancel: (ids: string[]) => api.batchCancel(ids).then((res) => {
      ensureOk(res)
      return unwrap<number>(res)
    }),
    getDeliveryReport: (id: string) => api.getDeliveryReport(id).then((res) => {
      ensureOk(res)
      return unwrap<DeliveryReportDto>(res)
    }),
  }

  // /admin/notification-templates CRUD. The endpoint is a thin
  // notification-scoped view over the generic template store and pins
  // Module="Notification" server-side, so the bridge only forwards the standard
  // template DTO shapes. It used to assemble those calls from a raw HttpClient
  // and a hardcoded base path because @tnzi/core shipped no factory for this
  // route; that factory now exists (useAdminNotificationTemplateApi), so the
  // URLs live in one place with every other notification endpoint.
  const templates: NotificationTemplateContract = templateApi
    ? {
        fetch: async (query: CrudPageQuery): Promise<CrudPageResult<TemplateInfoDto>> => {
          const params = { ...mapQueryToListRequest(query), includeFileSource: true } as unknown as TemplateQueryDto
          const result = unwrap<PagedList<TemplateInfoDto>>(await templateApi.getPagedList(params))
          return pagedResult({
            items: result.items ?? [],
            totalCount: result.totalCount ?? 0,
            pageIndex: result.pageIndex ?? query.pageIndex,
            pageSize: result.pageSize ?? query.pageSize,
          })
        },
        create: async (data) => {
          const payload = data as unknown as CreateTemplateDto
          const result = unwrapOk<TemplateEntityDto>(await templateApi.create(payload))
          // TemplateEntityDto → TemplateInfoDto widening is structural; the
          // list view only consumes Info fields, the form modal reads back
          // the full entity on edit.
          return result as unknown as TemplateInfoDto
        },
        update: async (id, data) => {
          const payload = data as unknown as UpdateTemplateDto
          const result = unwrapOk<TemplateEntityDto>(await templateApi.update(String(id), payload))
          return result as unknown as TemplateInfoDto
        },
        delete: async (ids) => {
          if (ids.length === 1) {
            ensureOk(await templateApi.delete(String(ids[0])))
            return
          }
          ensureOk(await templateApi.batchDelete(ids.map(String)))
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
        // Test-send creates and dispatches a real notification - use a
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
        fetch: backendGapReject('templates.fetch - no HttpClient (deps.client) or templateApi provided') as never,
        create: backendGapReject('templates.create - no HttpClient (deps.client) or templateApi provided'),
        update: backendGapReject('templates.update - no HttpClient (deps.client) or templateApi provided'),
        delete: backendGapReject('templates.delete - no HttpClient (deps.client) or templateApi provided'),
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
  // is (userId, channel, category) rather than a synthetic id - we still
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
          return unwrapOk(await prefApi.setPreference(input.userId, upsert)) as NotificationPreferenceDto
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
          return unwrapOk(await prefApi.setPreference(input.userId, upsert)) as NotificationPreferenceDto
        },
        delete: async (ids) => {
          for (const id of ids) {
            ensureOk(await prefApi.delete(String(id)))
          }
        },
      }
    : {
        fetch: backendGapReject('subscriptions.fetch - no preferenceApi provided') as never,
        create: backendGapReject('subscriptions.create - no preferenceApi provided'),
        update: backendGapReject('subscriptions.update - no preferenceApi provided'),
        delete: backendGapReject('subscriptions.delete - no preferenceApi provided'),
      }

  // ★ Anonymous: built straight off the HttpClient, no admin api factory.
  // The recipient of a bulk message is often not a user of this system at all.
  const unsubscribeApi = deps.client ? useUnsubscribeApi(deps.client) : null
  const publicUnsubscribe: NotificationPublicUnsubscribeContract = unsubscribeApi
    ? {
        preview: async (token) => {
          try {
            const res = await unsubscribeApi.preview(token)
            return res.succeeded ? (res.data ?? null) : null
          } catch {
            // A dead link and a dead network are one situation to the recipient.
            return null
          }
        },
        unsubscribe: async (token, reason) => {
          try {
            return (await unsubscribeApi.unsubscribe(token, reason)).succeeded === true
          } catch {
            return false
          }
        },
        resubscribe: async (token) => {
          try {
            return (await unsubscribeApi.resubscribe(token)).succeeded === true
          } catch {
            return false
          }
        },
      }
    : {
        preview: async () => null,
        unsubscribe: async () => false,
        resubscribe: async () => false,
      }

  const devices: BridgeCrudContract<PushDeviceDto> = deviceApi
    ? {
        fetch: async (query: CrudPageQuery): Promise<CrudPageResult<PushDeviceDto>> => {
          const filters = (query.filters ?? {}) as Record<string, unknown>
          const orderBy = query.sortField
            ? `${query.sortField}${query.sortOrder === 'desc' ? ' desc' : ''}`
            : undefined
          const params: PushDeviceQueryDto = {
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            orderBy,
            userId: typeof filters.userId === 'string' ? filters.userId : undefined,
            // filters 是 Record<string, unknown>，窄化只能到 number；
            // DevicePlatform 是数值联合，需显式断言。
            platform: typeof filters.platform === 'number' ? (filters.platform as DevicePlatform) : undefined,
            lastSeenAfter: typeof filters.lastSeenAfter === 'string' ? filters.lastSeenAfter : undefined,
          }
          const result = unwrap<{ items: PushDeviceDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
            await deviceApi.getList(params),
          )
          return pagedResult({
            items: result.items ?? [],
            totalCount: result.totalCount ?? 0,
            pageIndex: result.pageIndex ?? query.pageIndex,
            pageSize: result.pageSize ?? query.pageSize,
          })
        },
        // A device row is written by the device itself; see the contract docs.
        create: backendGapReject('devices.create - a device registers its own token'),
        update: backendGapReject('devices.update - device fields are reported by the client'),
        delete: async (ids: string[]) => {
          for (const id of ids) {
            ensureOk(await deviceApi.delete(id))
          }
        },
      }
    : {
        fetch: backendGapReject('devices.fetch'),
        create: backendGapReject('devices.create'),
        update: backendGapReject('devices.update'),
        delete: backendGapReject('devices.delete'),
      }

  return { messages, templates, subscriptions, devices, publicUnsubscribe }
}
