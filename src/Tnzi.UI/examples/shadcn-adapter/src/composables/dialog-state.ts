/**
 * @tnzi/ui/composables/dialog-state
 *
 * Reactive dialog state management — naive-ui style.
 * Supports multiple concurrent dialogs via reactive list.
 */

import { reactive, ref } from 'vue';

export type DialogType = 'default' | 'info' | 'success' | 'warning' | 'error';

export interface DialogOptions {
  title?: string;
  content?: string;
  type?: DialogType;
  positiveText?: string;
  negativeText?: string;
  closable?: boolean;
  maskClosable?: boolean;
  closeOnEsc?: boolean;
  showIcon?: boolean;
  loading?: boolean;
  /** Enable prompt mode — renders an input field, positive click passes input value */
  prompt?: boolean;
  /** Placeholder text for prompt input */
  promptPlaceholder?: string;
  /** Return false to prevent close */
  onPositiveClick?: (e: MouseEvent) => Promise<boolean | void> | boolean | void;
  /** Return false to prevent close */
  onNegativeClick?: (e: MouseEvent) => Promise<boolean | void> | boolean | void;
  /** Return false to prevent close */
  onClose?: () => Promise<boolean | void> | boolean | void;
  onMaskClick?: () => void;
  onEsc?: () => void;
  onAfterLeave?: () => void;
}

export interface DialogReactive extends DialogOptions {
  key: string;
  destroy: () => void;
}

/** Internal dialog entry */
export interface DialogEntry extends DialogReactive {
  /** Controls visibility — set to false to trigger leave animation */
  _show: boolean;
  /** Prompt mode: current input value (internal) */
  _inputValue?: string;
  /** Prompt mode: input change callback (internal) */
  _onInputChange?: (value: string) => void;
}

// --- State ---

let idCounter = 0;
const dialogList = ref<DialogEntry[]>([]);
const dialogInstances: Record<string, { hide: () => void }> = {};

function createId(): string {
  return `dlg_${++idCounter}`;
}

// --- Public API ---

function create(options: DialogOptions & { type?: DialogType }): DialogReactive {
  const key = createId();
  const entry = reactive<DialogEntry>({
    ...options,
    key,
    _show: true,
    destroy: () => {
      dialogInstances[key]?.hide();
    },
  });
  dialogList.value.push(entry);
  return entry;
}

function destroyAll(): void {
  Object.values(dialogInstances).forEach(inst => inst.hide());
}

function handleAfterLeave(key: string): void {
  const idx = dialogList.value.findIndex(d => d.key === key);
  if (idx !== -1) {
    dialogList.value.splice(idx, 1);
  }
  delete dialogInstances[key];
}

function registerInstance(key: string, inst: { hide: () => void }): void {
  dialogInstances[key] = inst;
}

export const dialogApi = {
  create: (options: DialogOptions) => create({ type: 'default', ...options }),
  info: (options: DialogOptions) => create({ ...options, type: 'info' }),
  success: (options: DialogOptions) => create({ ...options, type: 'success' }),
  warning: (options: DialogOptions) => create({ ...options, type: 'warning' }),
  error: (options: DialogOptions) => create({ ...options, type: 'error' }),
  destroyAll,
};

export type DialogApi = typeof dialogApi;

/** Used by DialogProvider */
export function getDialogList() {
  return dialogList;
}

/** Used by DialogProvider */
export { handleAfterLeave, registerInstance };

// --- Legacy compat: showAlert / showConfirm / showPrompt ---

export interface DialogStateOptions {
  title?: string;
  type?: 'info' | 'success' | 'warning' | 'error';
  confirmText?: string;
  cancelText?: string;
}

export function showAlert(message: string, options?: DialogStateOptions): Promise<void> {
  return new Promise<void>((resolve) => {
    create({
      type: options?.type ?? 'info',
      title: options?.title ?? 'Notice',
      content: message,
      positiveText: options?.confirmText ?? 'OK',
      showIcon: true,
      onPositiveClick: () => { resolve(); },
      onClose: () => { resolve(); },
    });
  });
}

export function showConfirm(message: string, options?: DialogStateOptions): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    create({
      type: options?.type ?? 'info',
      title: options?.title ?? 'Confirm',
      content: message,
      positiveText: options?.confirmText ?? 'OK',
      negativeText: options?.cancelText ?? 'Cancel',
      showIcon: true,
      onPositiveClick: () => { resolve(true); },
      onNegativeClick: () => { resolve(false); },
      onClose: () => { resolve(false); },
    });
  });
}

export function showPrompt(message: string, options?: DialogStateOptions): Promise<string | null> {
  return new Promise<string | null>((resolve) => {
    let inputValue = '';
    const entry = create({
      type: options?.type ?? 'info',
      title: options?.title ?? 'Input',
      content: message,
      positiveText: options?.confirmText ?? 'OK',
      negativeText: options?.cancelText ?? 'Cancel',
      showIcon: true,
      prompt: true,
      onPositiveClick: () => { resolve(inputValue); },
      onNegativeClick: () => { resolve(null); },
      onClose: () => { resolve(null); },
    });
    // Dialog reads _inputValue and writes back via _onInputChange
    (entry as DialogEntry)._inputValue = '';
    (entry as DialogEntry)._onInputChange = (v: string) => { inputValue = v; };
  });
}

/** @deprecated Use dialogApi instead */
export function getDialogState() {
  // Legacy single-dialog compat — return a fake state object
  return reactive({
    open: false,
    mode: 'alert' as 'alert' | 'confirm' | 'prompt',
    title: '',
    content: '',
    type: 'info' as 'info' | 'success' | 'warning' | 'error',
    confirmText: 'OK',
    cancelText: 'Cancel',
    inputValue: '',
    resolve: null as ((value: boolean | string | null | void) => void) | null,
  });
}
