/**
 * @tnzi/core/adapters/dialog
 *
 * Platform-agnostic dialog adapter.
 */

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

export interface DialogRuntime {
  setAdapter(newAdapter: DialogAdapter): void;
  use(): DialogAdapter;
  reset(): void;
}

export interface DialogRuntimeOptions {
  adapter?: DialogAdapter;
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

function createDefaultDialogAdapter(): DialogAdapter {
  return new ConsoleDialogAdapter();
}

export function createDialogRuntime(options: DialogRuntimeOptions = {}): DialogRuntime {
  const defaultAdapter = createDefaultDialogAdapter();
  let adapter: DialogAdapter = options.adapter ?? defaultAdapter;

  return {
    setAdapter(newAdapter: DialogAdapter) {
      adapter = newAdapter;
    },
    use() {
      return adapter;
    },
    reset() {
      adapter = defaultAdapter;
    },
  };
}

const defaultDialogRuntime = createDialogRuntime();
let activeDialogRuntime: DialogRuntime = defaultDialogRuntime;

export function setActiveDialogRuntime(runtime: DialogRuntime): void {
  activeDialogRuntime = runtime;
}

export function getActiveDialogRuntime(): DialogRuntime {
  return activeDialogRuntime;
}

export function useDialog() {
  return activeDialogRuntime.use();
}

export function setDialogAdapter(newAdapter: DialogAdapter) {
  activeDialogRuntime.setAdapter(newAdapter);
}

export function resetDialogAdapter() {
  activeDialogRuntime.reset();
}
