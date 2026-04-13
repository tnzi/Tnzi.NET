/**
 * @tnzi/mobile/stores/factory
 *
 * Store runtime management for Vant stores.
 * Provides HTTP client and storage adapter injection.
 */

import type { HttpClient } from '@tnzi/core/http/http';
import type { StorageAdapter } from '@tnzi/core/adapters';

// ============================================
// Module-Level State
// ============================================

let _httpClient: HttpClient | null = null;
let _storageAdapter: StorageAdapter | null = null;

// ============================================
// Runtime Initialization
// ============================================

/**
 * Initialize store runtime with HTTP client and optional storage adapter.
 * Must be called before any store actions that require API calls.
 *
 * @param httpClient - HTTP client for API requests
 * @param storage - Optional storage adapter for token persistence
 */
export function initStoreRuntime(httpClient: HttpClient, storage?: StorageAdapter): void {
  _httpClient = httpClient;
  if (storage) {
    _storageAdapter = storage;
  }
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
    throw new Error('HTTP client not available. Call initStoreRuntime() first.');
  }
  return _httpClient;
}

/**
 * Get the storage adapter for token persistence.
 * Returns null if not configured.
 */
export function getStoreStorage(): StorageAdapter | null {
  return _storageAdapter;
}

// ============================================
// Setters (for dependency injection)
// ============================================

/**
 * Set HTTP client (for late initialization or replacement).
 */
export function setStoreHttpClient(client: HttpClient): void {
  _httpClient = client;
}

/**
 * Set storage adapter (for late initialization or replacement).
 */
export function setStoreStorageAdapter(adapter: StorageAdapter): void {
  _storageAdapter = adapter;
}
