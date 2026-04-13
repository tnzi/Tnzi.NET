/**
 * @tnzi/core/adapters/loading-bar
 *
 * Platform-agnostic loading bar adapter.
 */

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

const _fallback: LoadingBarAdapter = new NoopLoadingBarAdapter();
let _active: LoadingBarAdapter | null = null;

export function setLoadingBarAdapter(adapter: LoadingBarAdapter): void {
  _active = adapter;
}

export function useLoadingBar(): LoadingBarAdapter {
  return _active ?? _fallback;
}

export function resetLoadingBarAdapter(): void {
  _active = null;
}
