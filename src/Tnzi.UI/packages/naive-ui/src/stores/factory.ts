/**
 * @tnzi/naive-ui/stores/factory
 *
 * Simplified store runtime for dependency injection.
 * Provides HTTP client and storage adapter to Pinia stores.
 */

import type { HttpClient } from '@tnzi/core/http/http';
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
