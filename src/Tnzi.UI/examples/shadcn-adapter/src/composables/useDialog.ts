/**
 * @tnzi/ui/composables/useDialog
 *
 * Programmatic dialog composable — naive-ui style API.
 * Supports info/success/warning/error typed dialogs with async close guards.
 */

import { dialogApi, showAlert, showConfirm, showPrompt } from './dialog-state';
import type { DialogApi, DialogOptions } from './dialog-state';

export function useDialog(): DialogApi & {
  /** Legacy: show an alert (single OK button) */
  show: (options: { title?: string; content: string; type?: 'info' | 'success' | 'warning' | 'error' }) => Promise<void>;
  /** Legacy: show a confirm dialog */
  confirm: (options: { title?: string; content: string; positiveText?: string; negativeText?: string }) => Promise<boolean>;
  /** Legacy: show an alert dialog */
  alert: (options: { title?: string; content: string }) => Promise<void>;
  /** Legacy: show a prompt dialog */
  prompt: (message: string, options?: { title?: string }) => Promise<string | null>;
} {
  const show = (options: { title?: string; content: string; type?: 'info' | 'success' | 'warning' | 'error' }) => {
    return showAlert(options.content, {
      title: options.title,
      type: options.type ?? 'info',
    });
  };

  const confirm = (options: { title?: string; content: string; positiveText?: string; negativeText?: string }) => {
    return showConfirm(options.content, {
      title: options.title,
      confirmText: options.positiveText,
      cancelText: options.negativeText,
    });
  };

  const alert = (options: { title?: string; content: string }) => {
    return showAlert(options.content, { title: options.title });
  };

  const prompt = (message: string, options?: { title?: string }) => {
    return showPrompt(message, { title: options?.title });
  };

  return {
    // Native naive-ui style API
    ...dialogApi,
    // Legacy compat
    show,
    confirm,
    alert,
    prompt,
  };
}
