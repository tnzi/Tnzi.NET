/**
 * @tnzi/vant/adapters/dialog
 *
 * Dialog adapter implementation for Vant.
 */

import type { DialogAdapter, DialogOptions } from '@tnzi/core/adapters';
import { showConfirmDialog, showDialog } from 'vant';

// Re-export Vant functions for direct use in playgrounds
export { showConfirmDialog, showDialog };

export function createVantDialogAdapter(): DialogAdapter {
  return {
    confirm: async (message: string, options?: DialogOptions) => {
      try {
        await showConfirmDialog({
          title: options?.title,
          message: message,
          confirmButtonText: options?.confirmText,
          cancelButtonText: options?.cancelText,
        });
        return true;
      } catch {
        return false;
      }
    },
    alert: async (message: string, options?: DialogOptions) => {
      await showDialog({
        title: options?.title,
        message: message,
        confirmButtonText: options?.confirmText,
      });
    },
    prompt: async (message: string, options?: DialogOptions) => {
      const content = options?.title ? `${options.title}\n\n${message}` : message;
      const value = window.prompt(content);
      return value;
    },
  };
}
