/**
 * Global type declarations for Naive UI provider APIs.
 *
 * Naive UI's useMessage() and useDialog() must be called inside setup().
 * To use them globally, the APIs are exposed on the window object from a
 * setup component wrapped by NMessageProvider / NDialogProvider.
 *
 * `@tnzi/ui-admin`'s `TAdminAppRoot` does this registration automatically
 * (internal `TAdminWindowHandles` component inside its provider stack).
 * Applications NOT using `TAdminAppRoot` register the handles themselves:
 * ```ts
 * // In a component under NMessageProvider + NDialogProvider
 * import { useMessage, useDialog } from 'naive-ui';
 * window.$message = useMessage();
 * window.$dialog = useDialog();
 * ```
 */

interface NaiveMessageApi {
  info(content: string, options?: Record<string, unknown>): { destroy: () => void };
  success(content: string, options?: Record<string, unknown>): { destroy: () => void };
  warning(content: string, options?: Record<string, unknown>): { destroy: () => void };
  error(content: string, options?: Record<string, unknown>): { destroy: () => void };
  loading(content: string, options?: Record<string, unknown>): { destroy: () => void };
}

interface NaiveDialogApi {
  create(options: Record<string, unknown>): { destroy: () => void };
  success(options: Record<string, unknown>): { destroy: () => void };
  error(options: Record<string, unknown>): { destroy: () => void };
  warning(options: Record<string, unknown>): { destroy: () => void };
  info(options: Record<string, unknown>): { destroy: () => void };
}

interface NaiveNotificationApi {
  info(options: Record<string, unknown>): { destroy: () => void };
  success(options: Record<string, unknown>): { destroy: () => void };
  warning(options: Record<string, unknown>): { destroy: () => void };
  error(options: Record<string, unknown>): { destroy: () => void };
  destroyAll(): void;
}

interface NaiveLoadingBarApi {
  start(): void;
  finish(): void;
  error(): void;
}

declare global {
  interface Window {
    $message?: NaiveMessageApi;
    $dialog?: NaiveDialogApi;
    $notification?: NaiveNotificationApi;
    $loadingBar?: NaiveLoadingBarApi;
  }
}

export {};
