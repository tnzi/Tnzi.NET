/**
 * @tnzi/mobile/adapters/dialog
 *
 * Dialog adapter implementation for Vant.
 */

import { h, ref } from 'vue';
import type { DialogAdapter, DialogOptions } from '@tnzi/core/adapters';
import type { DialogOptions as VantDialogOptions } from 'vant';
import { Field as VanField, showConfirmDialog, showDialog } from 'vant';

// Re-export Vant functions for direct use in playgrounds
export { showConfirmDialog, showDialog };

/**
 * Map DialogOptions.type to the dialog severity class.
 * The classes live in `styles/dialog.css`; `info` is the default dialog look
 * and therefore carries no class.
 */
function getThemeClass(type?: DialogOptions['type']): string | undefined {
  if (!type || type === 'info') return undefined;
  return `t-dialog-${type}`;
}

export function createVantDialogAdapter(): DialogAdapter {
  return {
    confirm: async (message: string, options?: DialogOptions) => {
      try {
        await showConfirmDialog({
          title: options?.title ?? 'Confirm',
          message: message,
          confirmButtonText: options?.confirmText,
          cancelButtonText: options?.cancelText,
          className: getThemeClass(options?.type),
        });
        return true;
      } catch {
        return false;
      }
    },
    alert: async (message: string, options?: DialogOptions) => {
      await showDialog({
        title: options?.title ?? 'Alert',
        message: message,
        confirmButtonText: options?.confirmText,
        className: getThemeClass(options?.type),
      });
    },
    prompt: async (message: string, options?: DialogOptions) => {
      // window.prompt is unusable here: iOS WKWebView only shows it when the
      // host app implements the panel delegate, and many in-app browsers no-op
      // it outright. Render a real Vant dialog with a field instead.
      if (typeof window === 'undefined') return null;

      const value = ref('');
      // Vant types the render form of `message` as `() => JSX.Element`; this
      // package compiles without JSX types, so the VNode goes through a cast.
      const renderBody = (() =>
        h('div', { class: 't-dialog-prompt' }, [
          h('p', { class: 't-dialog-prompt__message' }, message),
          h(VanField, {
            'modelValue': value.value,
            'autofocus': true,
            'onUpdate:modelValue': (next: string) => {
              value.value = next;
            },
          }),
        ])) as unknown as VantDialogOptions['message'];

      try {
        await showConfirmDialog({
          title: options?.title,
          message: renderBody,
          confirmButtonText: options?.confirmText,
          cancelButtonText: options?.cancelText,
          className: getThemeClass(options?.type),
        });
        return value.value;
      } catch {
        return null;
      }
    },
  };
}
