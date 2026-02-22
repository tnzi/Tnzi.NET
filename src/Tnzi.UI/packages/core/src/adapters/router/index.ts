/**
 * @tnzi/core/adapters/router
 *
 * Router adapter interface for UI framework abstraction.
 */

// ============================================
// Router Adapter Interface
// ============================================

/**
 * Router adapter for navigation abstraction.
 * Each UI package provides its own implementation (e.g., vue-router).
 */
export interface RouterAdapter {
  /**
   * Navigate to a path (push to history).
   */
  push(path: string): void | Promise<void>;

  /**
   * Replace current path in history.
   */
  replace(path: string): void | Promise<void>;

  /**
   * Go back in history.
   */
  back(): void;

  /**
   * Get current route path.
   */
  getCurrentPath(): string;
}

// ============================================
// Default Implementation (No-op)
// ============================================

/**
 * Create a no-op router adapter.
 */
export function createNoopRouterAdapter(): RouterAdapter {
  return {
    push() {},
    replace() {},
    back() {},
    getCurrentPath() {
      return '/';
    },
  };
}
