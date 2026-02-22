/**
 * @tnzi/shadcn/composables/useShadcnDialog
 *
 * Programmatic dialog composable for shadcn-vue.
 * Uses the reactive singleton state consumed by TDialogProvider.
 */

import { showAlert, showConfirm, showPrompt } from './dialog-state';

export function useShadcnDialog() {
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
    show,
    confirm,
    alert,
    prompt,
  };
}
