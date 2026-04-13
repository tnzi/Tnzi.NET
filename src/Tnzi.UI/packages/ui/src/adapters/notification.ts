/**
 * @tnzi/ui/adapters/notification
 *
 * Notification adapter using Naive UI's notification API.
 *
 * Supports two modes:
 * 1. Explicit API injection: pass the useNotification() result directly
 * 2. Global handle: uses window.$notification (set by a setup component under NNotificationProvider)
 */

import type { NotificationAdapter, NotificationOptions } from '@tnzi/core/adapters';

interface NaiveNotificationApi {
  success: (options: Record<string, unknown>) => void;
  error: (options: Record<string, unknown>) => void;
  warning: (options: Record<string, unknown>) => void;
  info: (options: Record<string, unknown>) => void;
  destroyAll: () => void;
}

function toNaiveOptions(content: string, options?: NotificationOptions): Record<string, unknown> {
  return {
    title: options?.title,
    content,
    duration: options?.duration,
    closable: options?.closable ?? true,
    meta: options?.meta,
  };
}

/**
 * Resolve the notification API: use explicit API if provided, otherwise fallback to window.$notification.
 */
function resolveApi(notificationApi?: NaiveNotificationApi): NaiveNotificationApi | undefined {
  if (notificationApi) return notificationApi;
  return (window as unknown as Record<string, unknown>).$notification as NaiveNotificationApi | undefined;
}

/**
 * Create a Naive UI notification adapter.
 *
 * When called without arguments, the adapter uses window.$notification as the global handle.
 * The application must wrap the root with NNotificationProvider and expose the API:
 * ```ts
 * // In a setup component under NNotificationProvider
 * window.$notification = useNotification();
 * ```
 */
export function createNotificationAdapter(notificationApi?: NaiveNotificationApi): NotificationAdapter {
  return {
    info(content: string, options?: NotificationOptions) {
      resolveApi(notificationApi)?.info(toNaiveOptions(content, options));
    },
    success(content: string, options?: NotificationOptions) {
      resolveApi(notificationApi)?.success(toNaiveOptions(content, options));
    },
    warning(content: string, options?: NotificationOptions) {
      resolveApi(notificationApi)?.warning(toNaiveOptions(content, options));
    },
    error(content: string, options?: NotificationOptions) {
      resolveApi(notificationApi)?.error(toNaiveOptions(content, options));
    },
    destroyAll() {
      resolveApi(notificationApi)?.destroyAll();
    },
  };
}
