/**
 * @tnzi/core/adapters/notification
 *
 * Platform-agnostic notification adapter.
 */

import { createAdapterSingleton } from './singleton';

export interface NotificationOptions {
  title?: string;
  duration?: number;
  closable?: boolean;
  meta?: string;
}

export interface NotificationAdapter {
  info(content: string, options?: NotificationOptions): void;
  success(content: string, options?: NotificationOptions): void;
  warning(content: string, options?: NotificationOptions): void;
  error(content: string, options?: NotificationOptions): void;
  destroyAll(): void;
}

class ConsoleNotificationAdapter implements NotificationAdapter {
  info(content: string, options?: NotificationOptions) {
    console.log(`[Notification:Info] ${options?.title ? options.title + ': ' : ''}${content}`);
  }
  success(content: string, options?: NotificationOptions) {
    console.log(`[Notification:Success] ${options?.title ? options.title + ': ' : ''}${content}`);
  }
  warning(content: string, options?: NotificationOptions) {
    console.warn(`[Notification:Warning] ${options?.title ? options.title + ': ' : ''}${content}`);
  }
  error(content: string, options?: NotificationOptions) {
    console.error(`[Notification:Error] ${options?.title ? options.title + ': ' : ''}${content}`);
  }
  destroyAll() {}
}

// ============================================
// Singleton
// ============================================

const _slot = createAdapterSingleton<NotificationAdapter>(
  'notification',
  () => new ConsoleNotificationAdapter()
);

export function setNotificationAdapter(adapter: NotificationAdapter): void {
  _slot.set(adapter);
}

export function useNotification(): NotificationAdapter {
  return _slot.use();
}

export function resetNotificationAdapter(): void {
  _slot.reset();
}
