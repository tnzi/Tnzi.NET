export { default as NotificationContainer } from './NotificationContainer.vue';
export { notificationApi, configureNotification } from './notification-store';
// NotificationType intentionally not re-exported — identical type already exported from @tnzi/core stores
export type { NotificationOptions, NotificationReactive, NotificationApi, NotificationPlacement } from './types';
