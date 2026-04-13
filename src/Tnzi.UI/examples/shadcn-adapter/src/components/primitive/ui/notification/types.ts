export type NotificationType = 'info' | 'success' | 'warning' | 'error';
export type NotificationPlacement = 'top-right' | 'top-left' | 'bottom-right' | 'bottom-left';

export interface NotificationOptions {
  /** Notification title */
  title?: string;
  /** Auto-dismiss duration in ms. 0 = no auto-close. Default: undefined (no auto-close) */
  duration?: number;
  /** Show close button. Default: true */
  closable?: boolean;
  /** Pause auto-dismiss timer on hover */
  keepAliveOnHover?: boolean;
  /** Show icon. Default: true */
  showIcon?: boolean;
  /** Timestamp or metadata text */
  meta?: string;
  /** Callback when notification closes */
  onClose?: () => void;
  /** Callback after leave animation completes */
  onAfterLeave?: () => void;
}

export interface NotificationReactive {
  /** Unique ID */
  readonly key: string;
  /** Notification content */
  content: string;
  /** Notification title */
  title: string;
  /** Notification type */
  type: NotificationType;
  /** Close this notification */
  destroy: () => void;
}

export interface NotificationApi {
  info: (content: string, options?: NotificationOptions) => NotificationReactive;
  success: (content: string, options?: NotificationOptions) => NotificationReactive;
  warning: (content: string, options?: NotificationOptions) => NotificationReactive;
  error: (content: string, options?: NotificationOptions) => NotificationReactive;
  destroyAll: () => void;
}
