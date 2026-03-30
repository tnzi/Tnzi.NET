/**
 * @tnzi/shadcn/composables/dialog-state
 *
 * Reactive singleton state for programmatic dialog management.
 * Used by TDialogProvider component and useShadcnDialog composable.
 */

import { reactive } from 'vue';

export interface DialogStateOptions {
  title?: string;
  type?: 'info' | 'success' | 'warning' | 'error';
  confirmText?: string;
  cancelText?: string;
}

export interface DialogState {
  open: boolean;
  mode: 'alert' | 'confirm' | 'prompt';
  title: string;
  content: string;
  type: 'info' | 'success' | 'warning' | 'error';
  confirmText: string;
  cancelText: string;
  inputValue: string;
  resolve: ((value: boolean | string | null | void) => void) | null;
}

const state = reactive<DialogState>({
  open: false,
  mode: 'alert',
  title: '',
  content: '',
  type: 'info',
  confirmText: 'OK',
  cancelText: 'Cancel',
  inputValue: '',
  resolve: null,
});

function resetState() {
  state.open = false;
  state.mode = 'alert';
  state.title = '';
  state.content = '';
  state.type = 'info';
  state.confirmText = 'OK';
  state.cancelText = 'Cancel';
  state.inputValue = '';
  state.resolve = null;
}

/**
 * Show an alert dialog (informational, single OK button).
 */
export function showAlert(message: string, options?: DialogStateOptions): Promise<void> {
  return new Promise<void>((resolve) => {
    resetState();
    state.mode = 'alert';
    state.content = message;
    state.title = options?.title ?? 'Notice';
    state.type = options?.type ?? 'info';
    state.confirmText = options?.confirmText ?? 'OK';
    state.resolve = () => resolve();
    state.open = true;
  });
}

/**
 * Show a confirm dialog (OK / Cancel buttons, returns boolean).
 */
export function showConfirm(message: string, options?: DialogStateOptions): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    resetState();
    state.mode = 'confirm';
    state.content = message;
    state.title = options?.title ?? 'Confirm';
    state.type = options?.type ?? 'info';
    state.confirmText = options?.confirmText ?? 'OK';
    state.cancelText = options?.cancelText ?? 'Cancel';
    state.resolve = resolve as (value: boolean | string | null | void) => void;
    state.open = true;
  });
}

/**
 * Show a prompt dialog (input field + OK / Cancel, returns string or null).
 */
export function showPrompt(message: string, options?: DialogStateOptions): Promise<string | null> {
  return new Promise<string | null>((resolve) => {
    resetState();
    state.mode = 'prompt';
    state.content = message;
    state.title = options?.title ?? 'Input';
    state.type = options?.type ?? 'info';
    state.confirmText = options?.confirmText ?? 'OK';
    state.cancelText = options?.cancelText ?? 'Cancel';
    state.inputValue = '';
    state.resolve = resolve as (value: boolean | string | null | void) => void;
    state.open = true;
  });
}

/**
 * Get the reactive dialog state (used by TDialogProvider).
 */
export function getDialogState() {
  return state;
}
