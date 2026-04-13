/**
 * @tnzi/ui/adapters/create-ui-adapter
 *
 * Factory for the composite UI adapter (message + dialog + theme).
 */

import type { IUiAdapter } from '@tnzi/core/adapters';
import { createMessageAdapter } from './message';
import { createDialogAdapter } from './dialog';
import { createThemeAdapter } from './theme';

export function createUiAdapter(): IUiAdapter {
  return {
    message: createMessageAdapter(),
    dialog: createDialogAdapter(),
    theme: createThemeAdapter(),
  };
}
