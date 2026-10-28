/**
 * @tnzi/ui/stores/factory
 *
 * The store runtime: the HTTP client and storage adapter the Pinia stores in
 * `@tnzi/ui/stores` build their state managers from.
 *
 * The runtime is module-level, one per JavaScript realm. It is not scoped to a
 * Vue app or to a request: two apps on one page, or concurrent SSR requests in
 * one process, share it. Each store builds its state manager on first use and
 * keeps it until that store's reset helper runs (`resetAuthRuntime()`,
 * `resetUserRuntime()`, `resetAppRuntime()`), so pointing the runtime at
 * another client afterwards does not reach a store already in use.
 */

import type { HttpClient } from '@tnzi/core/http';
import type { StorageAdapter } from '@tnzi/core/adapters';

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
 * `createTnziUi({ httpClient, storage })` calls it at install.
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
// Setters
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
// Reset
// ============================================

/**
 * Reset store runtime to uninitialized state (test teardown). Stores already
 * in use keep their state managers; reset those with their own helpers.
 */
export function resetStoreRuntime(): void {
  _httpClient = null;
  _storageAdapter = null;
}
