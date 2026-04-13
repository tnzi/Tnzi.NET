import type { MessageAdapter } from './message';
import type { DialogAdapter } from './dialog';
import type { ThemeAdapter } from './theme/index';
import { useMessage, setMessageAdapter, resetMessageAdapter } from './message';
import { useDialog, setDialogAdapter, resetDialogAdapter } from './dialog';
import { createNoopThemeAdapter } from './theme/index';

/**
 * Composite UI adapter — aggregates Message + Dialog + Theme.
 * Each UI package (shadcn/naive-ui/vant) provides a single factory.
 */
export interface IUiAdapter {
  readonly message: MessageAdapter;
  readonly dialog: DialogAdapter;
  readonly theme: ThemeAdapter;
}

function createDefaultUiAdapter(): IUiAdapter {
  return {
    // Delegate to existing individual adapter runtimes (ConsoleMessageAdapter etc.)
    get message() {
      return useMessage();
    },
    get dialog() {
      return useDialog();
    },
    theme: createNoopThemeAdapter(),
  };
}

const _fallback: IUiAdapter = createDefaultUiAdapter();
let _active: IUiAdapter | null = null;

/**
 * Set the active composite UI adapter.
 * Also syncs individual adapter runtimes for backward compatibility
 * (useMessage(), useDialog() still work).
 */
export function setActiveUiAdapter(adapter: IUiAdapter): void {
  _active = adapter;
  // Sync individual runtimes for backward compat
  setMessageAdapter(adapter.message);
  setDialogAdapter(adapter.dialog);
}

export function useUiAdapter(): IUiAdapter {
  return _active ?? _fallback;
}

export function resetUiAdapter(): void {
  _active = null;
  resetMessageAdapter();
  resetDialogAdapter();
}

/** @deprecated Use `resetUiAdapter` instead */
export const resetUiAdapterRuntime = resetUiAdapter;
