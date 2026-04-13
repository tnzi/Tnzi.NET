/**
 * @tnzi/mobile/adapters/dialog
 *
 * Dialog adapter implementation for Vant.
 */

import type { DialogAdapter, DialogOptions } from '@tnzi/core/adapters';
import { showConfirmDialog, showDialog } from 'vant';

// Re-export Vant functions for direct use in playgrounds
export { showConfirmDialog, showDialog };

/** Map DialogOptions.type to Vant dialog theme class */
function getThemeClass(type?: DialogOptions['type']): string | undefined {
  if (!type || type === 'info') return undefined;
  return `tz-dialog-${type}`;
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
      if (typeof window === 'undefined') return null;
      const content = options?.title ? `${options.title}\n\n${message}` : message;
      const value = window.prompt(content);
      return value;
    },
  };
}
