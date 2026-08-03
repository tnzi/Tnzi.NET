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

// Notification adapters
export * from './notification';

// Loading bar adapters
export * from './loading-bar';

// Event bus
export * from './event-bus';

// Storage adapters
export * from './storage';

// Internationalization
export * from './i18n';

// Store adapters (state management)
export * from './store';

// Theme adapter
export * from './theme/index';

// Router adapter
export * from './router/index';

// Logger adapter
export * from './logger';

// Composite adapters
export * from './ui-adapter';
export * from './runtime-adapter';

// ============================================
// Runtime Reset (for tests and SSR isolation)
// ============================================

import { resetStorageAdapter } from './storage';
import { resetEventBusAdapter } from './event-bus';
import { resetI18nRuntime } from './i18n/runtime';
import { resetMessageAdapter } from './message';
import { resetStoreAdapter } from './store';
import { resetDialogAdapter } from './dialog';
import { resetNotificationAdapter } from './notification';
import { resetLoadingBarAdapter } from './loading-bar';
import { resetUiAdapter } from './ui-adapter';
import { resetRuntimeAdapter } from './runtime-adapter';
import { resetLoggerAdapter } from './logger';

/**
 * Reset all adapter runtimes to their defaults.
 * Call between SSR requests or in test teardown to avoid cross-contamination.
 */
export function resetAllRuntimes(): void {
  resetStorageAdapter();
  resetEventBusAdapter();
  resetI18nRuntime();
  resetMessageAdapter();
  resetStoreAdapter();
  resetDialogAdapter();
  resetNotificationAdapter();
  resetLoadingBarAdapter();
  resetLoggerAdapter();
  resetUiAdapter();
  resetRuntimeAdapter();
}
