/**
 * @tnzi/ui/adapters/dialog
 *
 * Dialog adapter using Naive UI's dialog API.
 *
 * Supports two modes:
 * 1. Explicit API injection: pass the useDialog() result directly
 * 2. Global handle: uses window.$dialog (set by a setup component under NDialogProvider)
 */

import { h, ref } from 'vue';
import type { DialogAdapter, DialogOptions } from '@tnzi/core/adapters';

interface NaiveDialogApi {
  success: (options: Record<string, unknown>) => void;
  error: (options: Record<string, unknown>) => void;
  warning: (options: Record<string, unknown>) => void;
  info: (options: Record<string, unknown>) => void;
  create: (options: Record<string, unknown>) => void;
}

/**
 * Resolve the dialog API: use explicit API if provided, otherwise fallback to window.$dialog.
 */
function resolveApi(dialogApi?: NaiveDialogApi): NaiveDialogApi | undefined {
  if (dialogApi) return dialogApi;
  return (window as unknown as Record<string, unknown>).$dialog as NaiveDialogApi | undefined;
}

/**
 * Create a Naive UI dialog adapter.
 *
 * When called without arguments, the adapter uses window.$dialog as the global handle.
 * The application must wrap the root with NDialogProvider and expose the API:
 * ```ts
 * // In a setup component under NDialogProvider
 * window.$dialog = useDialog();
 * ```
 *
 * When called with an explicit API instance (from useDialog() inside setup),
 * it uses that instance directly:
 * ```ts
 * const dialogApi = useDialog();
 * setDialogAdapter(createDialogAdapter(dialogApi));
 * ```
 */
export function createDialogAdapter(dialogApi?: NaiveDialogApi): DialogAdapter {
  return {
    async confirm(message: string, options?: DialogOptions) {
      return new Promise<boolean>((resolve) => {
        const api = resolveApi(dialogApi);
        if (!api) {
          resolve(window.confirm(message));
          return;
        }
        const method = options?.type ?? 'warning';
        const showFn = api[method] ?? api.warning;
        showFn({
          title: options?.title ?? 'Confirm',
          content: message,
          positiveText: options?.confirmText ?? 'OK',
          negativeText: options?.cancelText ?? 'Cancel',
          onPositiveClick: () => resolve(true),
          onNegativeClick: () => resolve(false),
          onClose: () => resolve(false),
        });
      });
    },

    async alert(message: string, options?: DialogOptions) {
      const api = resolveApi(dialogApi);
      if (!api) {
        window.alert(message);
        return;
      }
      return new Promise<void>((resolve) => {
        const method = options?.type ?? 'info';
        const showFn = api[method] ?? api.info;
        showFn({
          title: options?.title ?? 'Alert',
          content: message,
          positiveText: options?.confirmText ?? 'OK',
          onPositiveClick: () => resolve(),
          onClose: () => resolve(),
        });
      });
    },

    async prompt(message: string, options?: DialogOptions): Promise<string | null> {
      return new Promise<string | null>((resolve) => {
        const api = resolveApi(dialogApi);
        if (!api) {
          resolve(window.prompt(message) ?? null);
          return;
        }
        const inputValue = ref(options?.content ?? '');
        api.create({
          title: options?.title ?? 'Input',
          content: () =>
            h('div', {}, [
              h('p', { style: 'margin-bottom: 8px' }, message),
              h('input', {
                value: inputValue.value,
                onInput: (e: Event) => {
                  inputValue.value = (e.target as HTMLInputElement).value;
                },
                style:
                  'width: 100%; padding: 6px 12px; border: 1px solid #e0e0e6; border-radius: 3px; outline: none; font-size: 14px;',
                placeholder: message,
              }),
            ]),
          positiveText: options?.confirmText ?? 'OK',
          negativeText: options?.cancelText ?? 'Cancel',
          onPositiveClick: () => resolve(inputValue.value),
          onNegativeClick: () => resolve(null),
          onClose: () => resolve(null),
        });
      });
    },
  };
}
