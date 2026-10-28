/**
 * @tnzi/core/adapters/dialog
 *
 * Platform-agnostic dialog adapter.
 */

import { createAdapterSingleton } from './singleton';

export interface DialogOptions {
  title?: string;
  content?: string;
  confirmText?: string;
  cancelText?: string;
  type?: 'info' | 'success' | 'warning' | 'error';
}

export interface DialogAdapter {
  alert(message: string, options?: DialogOptions): Promise<void>;
  confirm(message: string, options?: DialogOptions): Promise<boolean>;
  prompt(message: string, options?: DialogOptions): Promise<string | null>;
}

/**
 * Used until a UI package installs a real dialog (`setDialogAdapter` /
 * `setActiveUiAdapter`).
 *
 * `confirm` never answers "yes" on the user's behalf. Callers put destructive
 * or disruptive actions behind it (the app-update prompt reloads the page and
 * discards unsaved input), and this fallback is reached in any app that simply
 * never installed an adapter, not only in node. In a browser it asks through the
 * native `window.confirm`; with no way to ask at all it answers "no".
 */
class ConsoleDialogAdapter implements DialogAdapter {
  async alert(message: string): Promise<void> {
    console.log(`[Alert] ${message}`);
  }

  async confirm(message: string): Promise<boolean> {
    if (typeof window !== 'undefined' && typeof window.confirm === 'function') {
      return window.confirm(message) === true;
    }
    console.warn(`[Confirm] ${message} (no dialog adapter installed; answering no)`);
    return false;
  }

  async prompt(message: string): Promise<string | null> {
    console.log(`[Prompt] ${message}`);
    return null;
  }
}

// ============================================
// Singleton
// ============================================

const _slot = createAdapterSingleton<DialogAdapter>('dialog', () => new ConsoleDialogAdapter());

export function setDialogAdapter(adapter: DialogAdapter): void {
  _slot.set(adapter);
}

export function useDialog(): DialogAdapter {
  return _slot.use();
}

export function resetDialogAdapter(): void {
  _slot.reset();
}
