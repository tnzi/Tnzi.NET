/**
 * @tnzi/core/adapters/loading-bar
 *
 * Platform-agnostic loading bar adapter.
 */

import { createAdapterSingleton } from './singleton';

export interface LoadingBarAdapter {
  start(): void;
  finish(): void;
  error(): void;
}

class NoopLoadingBarAdapter implements LoadingBarAdapter {
  start() {}
  finish() {}
  error() {}
}

// ============================================
// Singleton
// ============================================

const _slot = createAdapterSingleton<LoadingBarAdapter>('loading-bar', () => new NoopLoadingBarAdapter());

export function setLoadingBarAdapter(adapter: LoadingBarAdapter): void {
  _slot.set(adapter);
}

export function useLoadingBar(): LoadingBarAdapter {
  return _slot.use();
}

export function resetLoadingBarAdapter(): void {
  _slot.reset();
}
