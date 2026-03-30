/**
 * @tnzi/core/adapters
 *
 * Third-party library wrappers and adapters.
 * Provides abstraction layers for common third-party libraries.
 */

// Validation adapters
export * from './validation';

// Message/Toast adapters
export * from './message';

// Dialog adapters
export * from './dialog';

// Storage adapters
export * from './storage';

// Internationalization
export * from './i18n';

// Icons
export * from './icons';

// Store adapters (state management)
export * from './store';

// Theme adapter
export * from './theme/index';

// Router adapter
export * from './router/index';

// ============================================
// Runtime Reset (for tests and SSR isolation)
// ============================================

import { resetStorageRuntime } from './storage';
import { resetEventBusRuntime } from './event-bus';
import { resetI18nRuntime } from './i18n/runtime';
import { resetMessageRuntime } from './message';
import { resetStoreRuntime } from './store';
/** @deprecated Will be removed in next major version. */
import { resetIconRegistry } from './icons/registry';

export { resetStorageRuntime, resetEventBusRuntime, resetI18nRuntime, resetMessageRuntime, resetStoreRuntime, resetIconRegistry };

/**
 * Reset all adapter runtimes to their defaults.
 * Call between SSR requests or in test teardown to avoid cross-contamination.
 */
export function resetAllRuntimes(): void {
  resetStorageRuntime();
  resetEventBusRuntime();
  resetI18nRuntime();
  resetMessageRuntime();
  resetStoreRuntime();
}
