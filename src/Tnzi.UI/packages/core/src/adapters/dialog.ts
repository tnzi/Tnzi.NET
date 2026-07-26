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

class ConsoleDialogAdapter implements DialogAdapter {
  async alert(message: string): Promise<void> {
    console.log(`[Alert] ${message}`);
  }

  async confirm(message: string): Promise<boolean> {
    // In node environment, default to true
    console.log(`[Confirm] ${message}`);
    return true;
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
