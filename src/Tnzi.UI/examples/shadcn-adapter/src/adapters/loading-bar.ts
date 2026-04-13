/**
 * @tnzi/ui/adapters/loading-bar
 *
 * Loading bar adapter using built-in LoadingBar component.
 */

import type { LoadingBarAdapter } from '@tnzi/core/adapters';
import { loadingBarApi } from '../components/primitive/ui/loading-bar';

export function createLoadingBarAdapter(): LoadingBarAdapter {
  return {
    start: () => loadingBarApi.start(),
    finish: () => loadingBarApi.finish(),
    error: () => loadingBarApi.error(),
  };
}
