import { reactive } from 'vue';
import type { NotificationType, NotificationOptions, NotificationReactive, NotificationApi, NotificationPlacement } from './types';

let _idCounter = 0;

export interface NotificationEntry {
  key: string;
  content: string;
  title: string;
  type: NotificationType;
  duration: number;
  closable: boolean;
  keepAliveOnHover: boolean;
  showIcon: boolean;
  meta?: string;
  onClose?: () => void;
  onAfterLeave?: () => void;
}

interface NotificationState {
  notifications: NotificationEntry[];
  placement: NotificationPlacement;
  max: number;
}

const state = reactive<NotificationState>({
  notifications: [],
  placement: 'top-right',
  max: 0,
});

function createNotification(type: NotificationType, content: string, options: NotificationOptions = {}): NotificationReactive {
  const key = `notif_${++_idCounter}`;
  const entry: NotificationEntry = {
    key,
    content,
    title: options.title ?? '',
    type,
    duration: options.duration ?? 0,
    closable: options.closable ?? true,
    keepAliveOnHover: options.keepAliveOnHover ?? true,
    showIcon: options.showIcon ?? true,
    meta: options.meta,
    onClose: options.onClose,
    onAfterLeave: options.onAfterLeave,
  };

  state.notifications.push(entry);

  // Enforce max limit
  if (state.max > 0 && state.notifications.length > state.max) {
    state.notifications.shift();
  }

  const reactiveNotif: NotificationReactive = {
    key,
    get content() { return entry.content; },
    set content(v: string) { entry.content = v; },
    get title() { return entry.title; },
    set title(v: string) { entry.title = v; },
    get type() { return entry.type; },
    set type(v: NotificationType) { entry.type = v; },
    destroy: () => removeNotification(key),
  };

  return reactiveNotif;
}

function removeNotification(key: string) {
  const idx = state.notifications.findIndex(n => n.key === key);
  if (idx !== -1) state.notifications.splice(idx, 1);
}

function destroyAll() {
  state.notifications.splice(0);
}

function configure(options: { placement?: NotificationPlacement; max?: number }) {
  if (options.placement) state.placement = options.placement;
  if (options.max !== undefined) state.max = options.max;
}

export const notificationApi: NotificationApi = {
  info: (content, options) => createNotification('info', content, options),
  success: (content, options) => createNotification('success', content, options),
  warning: (content, options) => createNotification('warning', content, options),
  error: (content, options) => createNotification('error', content, options),
  destroyAll,
};

export { state as notificationState, removeNotification, configure as configureNotification };
