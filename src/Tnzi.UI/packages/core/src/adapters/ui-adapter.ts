import type { MessageAdapter } from './message';
import type { DialogAdapter } from './dialog';
import type { ThemeAdapter } from './theme/index';
import { useMessage, setMessageAdapter, resetMessageAdapter } from './message';
import { useDialog, setDialogAdapter, resetDialogAdapter } from './dialog';
import { createNoopThemeAdapter } from './theme/index';
import { createAdapterSingleton } from './singleton';

/**
 * Composite UI adapter - aggregates Message + Dialog + Theme.
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

const _slot = createAdapterSingleton<IUiAdapter>('ui', createDefaultUiAdapter);

/**
 * Set the active composite UI adapter.
 * Also syncs individual adapter runtimes for backward compatibility
 * (useMessage(), useDialog() still work).
 */
export function setActiveUiAdapter(adapter: IUiAdapter): void {
  _slot.set(adapter);
  // Sync individual runtimes for backward compat
  setMessageAdapter(adapter.message);
  setDialogAdapter(adapter.dialog);
}

export function useUiAdapter(): IUiAdapter {
  return _slot.use();
}

export function resetUiAdapter(): void {
  _slot.reset();
  resetMessageAdapter();
  resetDialogAdapter();
}
