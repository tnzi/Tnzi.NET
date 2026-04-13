/**
 * @tnzi/ui/adapters/notification
 *
 * Notification adapter using built-in Notification component.
 */

import type { NotificationAdapter, NotificationOptions } from '@tnzi/core/adapters';
import { notificationApi } from '../components/primitive/ui/notification';

export function createNotificationAdapter(): NotificationAdapter {
  return {
    info: (content, options?) => {
      notificationApi.info(content, {
        title: options?.title,
        duration: options?.duration,
        closable: options?.closable,
        meta: options?.meta,
      });
    },
    success: (content, options?) => {
      notificationApi.success(content, {
        title: options?.title,
        duration: options?.duration,
        closable: options?.closable,
        meta: options?.meta,
      });
    },
    warning: (content, options?) => {
      notificationApi.warning(content, {
        title: options?.title,
        duration: options?.duration,
        closable: options?.closable,
        meta: options?.meta,
      });
    },
    error: (content, options?) => {
      notificationApi.error(content, {
        title: options?.title,
        duration: options?.duration,
        closable: options?.closable,
        meta: options?.meta,
      });
    },
    destroyAll: () => {
      notificationApi.destroyAll();
    },
  };
}
