/**
 * Notification Module API - Admin management and user inbox operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  NotificationInfo,
  CreateNotificationRequest,
  QueryNotificationRequest,
  NotificationStatisticsDto,
  ChannelStatisticsDto,
  StatusStatisticsDto,
  NotificationTrendDto,
  NotificationPreviewDto,
  DeliveryReportDto,
  UserNotificationItem,
  UserNotificationDetail,
  QueryUserNotificationRequest,
  UnreadCountDto,
  NotificationPreferenceDto,
  NotificationPreferenceQueryDto,
  SetNotificationPreferenceDto,
  UnsubscribePreviewDto,
  OptOutDto,
  OptOutQueryDto,
  CreateOptOutDto,
  AnonymousDeviceRegistrationDto,
  PushDeviceDto,
  PushDeviceQueryDto,
  RegisterPushDeviceDto,
  UnregisterPushDeviceDto,
} from './types';
import type { TrendInterval } from './metadata';
import type {
  TemplateInfoDto,
  TemplateEntityDto,
  CreateTemplateDto,
  UpdateTemplateDto,
  TemplateQueryDto,
} from '../template';

const ADMIN_BASE = '/admin/notifications';
const PREFERENCES_BASE = '/admin/notification-preferences';
const OPT_OUTS_BASE = '/admin/notification-opt-outs';
const TEMPLATES_BASE = '/admin/notification-templates';
const USER_BASE = '/notifications';
const UNSUBSCRIBE_BASE = '/notifications/unsubscribe';
const DEVICES_BASE = '/notifications/devices';

/**
 * Header that carries the anonymous device key.
 *
 * ★ A header, not a query string: query strings are copied verbatim into access
 * logs and reverse-proxy logs, and this value is the device's ENTIRE credential
 * - whoever holds it can re-point that device's push address.
 */
export const PUSH_DEVICE_KEY_HEADER = 'X-Device-Key';
const ADMIN_DEVICES_BASE = '/admin/notification-devices';

/**
 * Admin Notification Management API
 */
export function useAdminNotificationApi(client: HttpClient) {
  return {
    /** Get notification by ID */
    getById: (id: string) =>
      client.get<NotificationInfo>(`${ADMIN_BASE}/${id}`),

    /** Create notification (without sending) */
    create: (data: CreateNotificationRequest) =>
      client.post<NotificationInfo>(`${ADMIN_BASE}`, data),

    /** Create and send notification */
    createAndSend: (data: CreateNotificationRequest) =>
      client.post<NotificationInfo>(`${ADMIN_BASE}/create-and-send`, data),

    /** Send notification immediately */
    send: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/send`),

    /** Query notifications */
    query: (data?: QueryNotificationRequest) =>
      client.post<PagedList<NotificationInfo>>(`${ADMIN_BASE}/query`, data ?? {}),

    /** Retry failed notification */
    retry: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/retry`),

    /** Retry all failed notifications in date range */
    retryFailed: (startDate?: string, endDate?: string) =>
      client.post<void>(`${ADMIN_BASE}/retry-failed`, undefined, {
        params: { startDate, endDate },
      }),

    /** Cancel notification */
    cancel: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/cancel`),

    /** Delete notification */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_BASE}/${id}`),

    /** Get statistics */
    getStatistics: (startDate?: string, endDate?: string) =>
      client.get<NotificationStatisticsDto>(`${ADMIN_BASE}/statistics`, {
        params: { startDate, endDate },
      }),

    /** Get statistics by channel */
    getStatisticsByChannel: (startDate?: string, endDate?: string) =>
      client.get<ChannelStatisticsDto[]>(`${ADMIN_BASE}/statistics/by-channel`, {
        params: { startDate, endDate },
      }),

    /** Get statistics by status */
    getStatisticsByStatus: (startDate?: string, endDate?: string) =>
      client.get<StatusStatisticsDto[]>(`${ADMIN_BASE}/statistics/by-status`, {
        params: { startDate, endDate },
      }),

    /** Get failed notifications */
    getFailed: (startDate?: string, endDate?: string, top?: number) =>
      client.get<NotificationInfo[]>(`${ADMIN_BASE}/failed`, {
        params: { startDate, endDate, top },
      }),

    /** Batch cancel notifications */
    batchCancel: (ids: string[]) =>
      client.post<number>(`${ADMIN_BASE}/batch-cancel`, ids),

    /** Batch delete notifications */
    batchDelete: (ids: string[]) =>
      client.delete<number>(`${ADMIN_BASE}/batch`, { body: ids }),

    /** Query scheduled notifications */
    getScheduled: (data?: QueryNotificationRequest) =>
      client.post<PagedList<NotificationInfo>>(`${ADMIN_BASE}/scheduled`, data ?? {}),

    /** Get statistics trend */
    getStatisticsTrend: (interval?: TrendInterval, startDate?: string, endDate?: string) =>
      client.get<NotificationTrendDto>(`${ADMIN_BASE}/statistics/trend`, {
        params: { interval, startDate, endDate },
      }),

    /** Preview notification rendering (without creating or sending) */
    preview: (data: CreateNotificationRequest) =>
      client.post<NotificationPreviewDto>(`${ADMIN_BASE}/preview`, data),

    /** Resend to failed recipients */
    resendToFailed: (id: string) =>
      client.post<number>(`${ADMIN_BASE}/${id}/resend-failed`),

    /** Get delivery report */
    getDeliveryReport: (id: string) =>
      client.get<DeliveryReportDto>(`${ADMIN_BASE}/${id}/delivery-report`),
  };
}

/**
 * Admin Notification Template Management API.
 * Backend: DefaultNotificationTemplateAdminController.
 *
 * A notification-scoped view over the generic template store: the server pins
 * Module="Notification", so callers pass the ordinary template DTO shapes and
 * never set the module themselves. Kept here rather than in services/template
 * because the route lives on the notification module and carries notification
 * permission codes.
 */
export function useAdminNotificationTemplateApi(client: HttpClient) {
  return {
    /** Paged list of this module's templates */
    getPagedList: (query?: TemplateQueryDto) =>
      client.get<PagedList<TemplateInfoDto>>(TEMPLATES_BASE, { params: query }),

    /** Get one template by id */
    getById: (id: string) =>
      client.get<TemplateEntityDto>(`${TEMPLATES_BASE}/${id}`),

    /** Create a notification template */
    create: (data: CreateTemplateDto) =>
      client.post<TemplateEntityDto>(TEMPLATES_BASE, data),

    /** Update a notification template */
    update: (id: string, data: UpdateTemplateDto) =>
      client.put<TemplateEntityDto>(`${TEMPLATES_BASE}/${id}`, data),

    /** Delete a notification template */
    delete: (id: string) =>
      client.delete<void>(`${TEMPLATES_BASE}/${id}`),

    /** Delete several notification templates */
    batchDelete: (ids: string[]) =>
      client.delete<void>(`${TEMPLATES_BASE}/batch`, { body: ids }),
  };
}

/**
 * Admin Notification Preference (Subscription) Management API.
 * Backend: DefaultNotificationPreferenceAdminController.
 */
export function useAdminNotificationPreferenceApi(client: HttpClient) {
  return {
    /** Canonical paged list across all users (GET /admin/notification-preferences) */
    getPagedList: (query?: NotificationPreferenceQueryDto) =>
      client.get<PagedList<NotificationPreferenceDto>>(PREFERENCES_BASE, { params: query }),

    /** Get all preferences for a specific user */
    getByUser: (userId: string) =>
      client.get<NotificationPreferenceDto[]>(`${PREFERENCES_BASE}/user/${userId}`),

    /** Upsert a preference for the given user (PUT /admin/notification-preferences/user/{userId}) */
    setPreference: (userId: string, data: SetNotificationPreferenceDto) =>
      client.put<NotificationPreferenceDto>(`${PREFERENCES_BASE}/user/${userId}`, data),

    /** Delete a preference by id */
    delete: (id: string) =>
      client.delete<void>(`${PREFERENCES_BASE}/${id}`),

    /** Reset a user's preferences to platform defaults */
    resetToDefault: (userId: string) =>
      client.post<void>(`${PREFERENCES_BASE}/user/${userId}/reset`),
  };
}

/**
 * Admin opt-out (suppression list) API.
 * Backend: DefaultNotificationOptOutAdminController.
 *
 * Distinct from preferences: a preference is a USER's choice per channel and
 * category, an opt-out is an ADDRESS that said no (one-click link, provider
 * complaint, phone call). Delivery honours both; the admin page for this one
 * exists so a compliance question ("when did this address opt out, on which
 * channel") and an undo request ("I clicked by mistake") can actually be
 * answered.
 */
export function useAdminNotificationOptOutApi(client: HttpClient) {
  return {
    /** Paged suppression list with address / channel / category / window filters */
    getPagedList: (query?: OptOutQueryDto) =>
      client.get<PagedList<OptOutDto>>(OPT_OUTS_BASE, { params: query }),

    /** Hand-register an opt-out. Idempotent on (address, channel, category). */
    create: (data: CreateOptOutDto) =>
      client.post<OptOutDto>(OPT_OUTS_BASE, data),

    /** Revoke one opt-out by row id; the address receives again from the next send on */
    delete: (id: string) =>
      client.delete<void>(`${OPT_OUTS_BASE}/${id}`),
  };
}

/**
 * One-click unsubscribe API - ANONYMOUS.
 *
 * The recipient of a bulk message is not necessarily a user of this system
 * (imported contact lists, former customers, closed accounts), and requiring a
 * login before someone can stop receiving mail defeats the point of one-click
 * unsubscribe. Identity comes from the signed token in the link itself.
 *
 * The GET preview is deliberately separate from the POST that acts: mail
 * clients prefetch links, and a prefetcher must not be able to unsubscribe
 * someone on their behalf.
 */
export function useUnsubscribeApi(client: HttpClient) {
  return {
    /** Echo back what this link would unsubscribe. No side effects. */
    preview: (token: string) =>
      client.get<UnsubscribePreviewDto>(`${UNSUBSCRIBE_BASE}`, { params: { token } }),

    /** Act on the link. */
    unsubscribe: (token: string, reason?: string) =>
      client.post<void>(`${UNSUBSCRIBE_BASE}`, { token, reason }),

    /** Undo, for someone who clicked by mistake. */
    resubscribe: (token: string) =>
      client.post<void>(`${UNSUBSCRIBE_BASE}/resubscribe`, { token }),
  };
}

/**
 * User Notification API (Inbox)
 */
export function useNotificationApi(client: HttpClient) {
  return {
    /** Get current user's notification inbox */
    getInbox: (data?: QueryUserNotificationRequest) =>
      client.post<PagedList<UserNotificationItem>>(`${USER_BASE}/inbox`, data ?? {}),

    /** Get notification detail (auto marks as read) */
    getDetail: (id: string) =>
      client.get<UserNotificationDetail>(`${USER_BASE}/${id}`),

    /** Mark notification as read */
    markAsRead: (id: string) =>
      client.post<void>(`${USER_BASE}/${id}/read`),

    /** Mark all notifications as read */
    markAllAsRead: () =>
      client.post<void>(`${USER_BASE}/read-all`),

    /** Get unread notification count */
    getUnreadCount: () =>
      client.get<UnreadCountDto>(`${USER_BASE}/unread-count`),

    /** Delete notification */
    delete: (id: string) =>
      client.delete<void>(`${USER_BASE}/${id}`),

    /** Batch mark notifications as read */
    batchMarkAsRead: (ids: string[]) =>
      client.post<void>(`${USER_BASE}/batch-read`, ids),

    /** Batch delete notifications */
    batchDelete: (ids: string[]) =>
      client.post<void>(`${USER_BASE}/batch-delete`, ids),
  };
}


/**
 * Push device registry API (user side).
 *
 * Only available when the app loads the optional `Tnzi.Notification.Push`
 * module - the table and these endpoints do not exist otherwise, by design:
 * an email-only app should not carry a permanently empty device table.
 *
 * The framework deliberately does NOT ship the client-side Firebase
 * integration: obtaining a token is three different native jobs on
 * Android / iOS / Web, and bundling `firebase` into `@tnzi/mobile` would push
 * it onto every mobile consumer. Get the token yourself, then post it here.
 */
export function usePushDeviceApi(client: HttpClient) {
  return {
    /** Register or refresh this device's push token. Call it on every app start. */
    register: (data: RegisterPushDeviceDto) =>
      client.post<PushDeviceDto>(`${DEVICES_BASE}`, data),

    /** List the current user's registered devices. Tokens come back masked. */
    getMine: () =>
      client.get<PushDeviceDto[]>(`${DEVICES_BASE}`),

    /**
     * Unregister THIS device by its token - the sign-out call.
     *
     * ★ Sign-out is the reason this exists rather than `remove(id)`: at that
     * moment the client holds a token and no row id, and the device list hands
     * back a masked token it cannot search by. Idempotent - a token that is
     * already gone still resolves.
     */
    unregister: (data: UnregisterPushDeviceDto) =>
      client.post<void>(`${DEVICES_BASE}/unregister`, data),

    /** Remove another of the current user's devices from the device list (by row id). */
    remove: (id: string) =>
      client.delete<void>(`${DEVICES_BASE}/${id}`),

    /**
     * Register or refresh this anonymous device - call it on EVERY app start.
     *
     * For apps with no sign-in at all: the device submits something, and the
     * receipt has to come back to that same device. The alternative with zero
     * stored identifiers is topic broadcast (server-side `SendToTopicAsync`),
     * but topics carry no delivery record, no retry and no opt-out - this path
     * exists for the case that needs those.
     *
     * Three lines on the client, with no branch:
     * ```ts
     * import { isSuccess } from '@tnzi/core/http'
     *
     * const key = await secureStorage.get('pushDeviceKey')      // may be absent
     * const r = await devices.registerAnonymous({ token, platform }, key)
     * if (isSuccess(r) && r.data.deviceKey) {
     *   await secureStorage.set('pushDeviceKey', r.data.deviceKey)
     * }
     * ```
     *
     * ★ The server only accepts keys in the shape it issues (43 base64url
     * characters). Do not substitute a platform identifier such as
     * `identifierForVendor` to avoid storing one - it is refused with a 400,
     * and it would turn this device's entire credential into a public value.
     *
     * ★ In a browser this sends a custom header, so the deployment must allow
     * `X-Device-Key` through CORS (`AspNetCore:Cors:WithHeaders` or
     * `AllowAnyHeader`). Native apps are unaffected.
     * Then send `r.data.deviceId` with your business request; the server pushes
     * the receipt back to this device by it.
     *
     * ★ Registration and refresh are the SAME call - passing a key decides
     * which one it is. Splitting them would force the client to decide "is this
     * a first launch", and its only evidence is "does secure storage have one":
     * a failed read looks exactly like a first launch, and taking that branch
     * issues a NEW identity that evicts the old one, silently breaking receipts
     * for everything already submitted.
     *
     * ★ `deviceKey` comes back ONLY when it is issued. Persist it in the
     * platform's secure storage immediately - there is no recovery endpoint,
     * because such an endpoint would be a bypass around the key itself.
     *
     * ★ `deviceId` comes back every time, so there is no need to store it too.
     */
    registerAnonymous: (data: RegisterPushDeviceDto, deviceKey?: string) =>
      client.post<AnonymousDeviceRegistrationDto>(
        `${DEVICES_BASE}/anonymous`,
        data,
        deviceKey ? { headers: { [PUSH_DEVICE_KEY_HEADER]: deviceKey } } : undefined,
      ),

    /**
     * Unregister this anonymous device.
     *
     * Idempotent: a key whose row is already gone still resolves. That also
     * makes "wrong key" and "key is right but the row was retired" answer the
     * same way - telling them apart would help someone probe which keys are
     * real.
     */
    unregisterAnonymous: (deviceKey: string) =>
      client.post<void>(`${DEVICES_BASE}/anonymous/unregister`, undefined, {
        headers: { [PUSH_DEVICE_KEY_HEADER]: deviceKey },
      }),
  };
}

/**
 * Push device registry API (admin side).
 *
 * Read and delete only. Registration is a client action - a row an operator
 * typed in matches no real device, so pushes to it would fail forever with
 * nobody knowing why. Requires `notification.pushDevice.view`, and
 * `notification.pushDevice.delete` on top for removal.
 */
export function useAdminPushDeviceApi(client: HttpClient) {
  return {
    /** Paged device list. Tokens come back masked. */
    getList: (query?: PushDeviceQueryDto) =>
      client.get<PagedList<PushDeviceDto>>(`${ADMIN_DEVICES_BASE}`, { params: query }),

    /** Delete one device. */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_DEVICES_BASE}/${id}`),
  };
}
