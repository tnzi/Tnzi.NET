/**
 * @tnzi/ui/stores/factory
 *
 * Simplified store runtime for dependency injection.
 * Provides HTTP client and storage adapter to Pinia stores.
 */

import { inject, type App, type InjectionKey } from 'vue';
import type { HttpClient } from '@tnzi/core/http/http';
import type { StorageAdapter } from '@tnzi/core/adapters';

// ============================================
// Injection Keys (SSR-safe provide/inject DI)
// ============================================

export const STORE_HTTP_CLIENT: InjectionKey<HttpClient> = Symbol('tnzi:store-http-client');
export const STORE_STORAGE: InjectionKey<StorageAdapter> = Symbol('tnzi:store-storage');

// ============================================
// Store Runtime State
// ============================================

let _httpClient: HttpClient | null = null;
let _storageAdapter: StorageAdapter | null = null;

// ============================================
// Initialization
// ============================================

/**
 * Initialize store runtime with HTTP client and optional storage adapter.
 * Must be called before using any store that requires API access.
 *
 * @param httpClient - HTTP client for API calls
 * @param storage - Optional storage adapter for persistence
 */
export function initStoreRuntime(httpClient: HttpClient, storage?: StorageAdapter): void {
  _httpClient = httpClient;
  _storageAdapter = storage ?? null;
}

// ============================================
// Accessors
// ============================================

/**
 * Get the HTTP client for store API calls.
 * Throws if not initialized.
 */
export function getStoreHttpClient(): HttpClient {
  if (!_httpClient) {
    throw new Error('HTTP client not initialized. Call initStoreRuntime() first.');
  }
  return _httpClient;
}

/**
 * Get the storage adapter for persistence.
 * Returns null if not configured.
 */
export function getStoreStorage(): StorageAdapter | null {
  return _storageAdapter;
}

// ============================================
// Setters (for plugin integration)
// ============================================

/**
 * Set HTTP client (for dependency injection from plugin).
 */
export function setStoreHttpClient(client: HttpClient): void {
  _httpClient = client;
}

/**
 * Set storage adapter (for dependency injection from plugin).
 */
export function setStoreStorageAdapter(adapter: StorageAdapter): void {
  _storageAdapter = adapter;
}

// ============================================
// Reset & Convenience
// ============================================

/**
 * Reset store runtime to uninitialized state.
 * Useful for SSR isolation or test teardown.
 */
export function resetStoreRuntime(): void {
  _httpClient = null;
  _storageAdapter = null;
}

// ============================================
// Provide/Inject DI (SSR-safe)
// ============================================

/**
 * Provide store runtime via Vue's provide/inject system (SSR-safe).
 * Also sets module-level fallback for non-component contexts (e.g. Pinia actions).
 *
 * @param app - Vue app instance
 * @param httpClient - HTTP client for API calls
 * @param storage - Optional storage adapter for persistence
 */
export function provideStoreRuntime(app: App, httpClient: HttpClient, storage?: StorageAdapter): void {
  app.provide(STORE_HTTP_CLIENT, httpClient);
  if (storage) {
    app.provide(STORE_STORAGE, storage);
  }
  // Also set module-level fallback for non-component contexts
  _httpClient = httpClient;
  _storageAdapter = storage ?? null;
}

/**
 * Inject the HTTP client from Vue's provide/inject system.
 * Falls back to module-level variable if not in a component context.
 * Must be called inside setup() or a composable.
 */
export function useStoreHttpClient(): HttpClient {
  const injected = inject(STORE_HTTP_CLIENT, null);
  if (injected) return injected;
  if (_httpClient) return _httpClient;
  throw new Error('HTTP client not provided. Call provideStoreRuntime() or initStoreRuntime() first.');
}

/**
 * Inject the storage adapter from Vue's provide/inject system.
 * Falls back to module-level variable if not in a component context.
 * Must be called inside setup() or a composable.
 */
export function useStoreStorage(): StorageAdapter | null {
  const injected = inject(STORE_STORAGE, null);
  if (injected) return injected;
  return _storageAdapter;
}
