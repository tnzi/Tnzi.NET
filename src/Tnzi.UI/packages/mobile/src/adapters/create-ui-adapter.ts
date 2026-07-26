/**
 * @tnzi/mobile/adapters/create-ui-adapter
 *
 * Composite UI adapter factory for Vant.
 */

import type { IUiAdapter } from '@tnzi/core/adapters';
import { createVantMessageAdapter } from './message';
import { createVantDialogAdapter } from './dialog';
import { createVantThemeAdapter } from './theme';

export function createVantUiAdapter(): IUiAdapter {
  return {
    message: createVantMessageAdapter(),
    dialog: createVantDialogAdapter(),
    theme: createVantThemeAdapter(),
  };
}
