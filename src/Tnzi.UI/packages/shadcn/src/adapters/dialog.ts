/**
 * @tnzi/shadcn/adapters/dialog
 *
 * Dialog adapter implementation for shadcn-vue.
 * Uses the reactive singleton state consumed by TDialogProvider.
 */

import type { DialogAdapter, DialogOptions } from '@tnzi/core/adapters';
import { showAlert, showConfirm, showPrompt } from '../composables/dialog-state';

export function createShadcnDialogAdapter(): DialogAdapter {
  return {
    confirm: (message: string, options?: DialogOptions) => {
      return showConfirm(message, {
        title: options?.title,
        type: options?.type,
        confirmText: options?.confirmText,
        cancelText: options?.cancelText,
      });
    },

    alert: (message: string, options?: DialogOptions) => {
      return showAlert(message, {
        title: options?.title,
        type: options?.type,
        confirmText: options?.confirmText,
      });
    },

    prompt: (message: string, options?: DialogOptions) => {
      return showPrompt(message, {
        title: options?.title,
        type: options?.type,
        confirmText: options?.confirmText,
        cancelText: options?.cancelText,
      });
    },
  };
}
