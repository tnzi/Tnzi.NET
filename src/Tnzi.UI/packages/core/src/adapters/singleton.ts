/**
 * @tnzi/core/adapters/singleton
 *
 * Cross-chunk adapter singleton registry.
 *
 * The adapter pattern is a core design of this package: core declares the
 * interface, the UI package installs the implementation via `setXxxAdapter()`,
 * and every consumer reads it back through `useXxx()`. That contract only holds
 * while the setter and the getter touch the SAME piece of state.
 *
 * A plain module-level `let _active` does not give us that. This package is
 * built by tsup with `splitting: false` across ~20 entry points, so a module
 * imported from several entries is INLINED into each of their bundles. Reading
 * the built output: `dist/http/index.js` carries its own copy of the message
 * adapter's fallback and `_active`, and `setMessageAdapter` is tree-shaken out
 * of that entry entirely - so `http/middleware`'s default error notifier could
 * only ever reach a private console fallback, never the toast adapter the app
 * installed through `dist/index.js`.
 *
 * Parking the state on `globalThis` under one key is the standard fix: it is
 * immune to bundle duplication, works identically for the CJS and ESM outputs,
 * and survives a consumer that (legitimately) resolves two different subpaths.
 *
 * The fallback is created lazily and stored in the same slot, so stateful
 * fallbacks (the in-process event bus, most importantly) are shared too - a
 * duplicated fallback bus would silently drop events between publishers and
 * subscribers that happen to sit in different chunks.
 */

const REGISTRY_KEY = '__TNZI_CORE_ADAPTERS__';

interface AdapterSlot<T> {
  /** Implementation installed by a UI package; null while unset. */
  active: T | null;
  /** Lazily created default, shared across chunks like `active`. */
  fallback: T | null;
}

type AdapterRegistry = Record<string, AdapterSlot<unknown>>;

function getRegistry(): AdapterRegistry {
  const globals = globalThis as typeof globalThis & { [REGISTRY_KEY]?: AdapterRegistry };
  globals[REGISTRY_KEY] ??= {};
  return globals[REGISTRY_KEY];
}

/**
 * A set/use/reset trio backed by one process-wide slot.
 */
export interface AdapterSingleton<T> {
  /** Install the active implementation. */
  set(adapter: T): void;
  /** Read the active implementation, falling back to the built-in default. */
  use(): T;
  /** Drop the active implementation (tests / SSR isolation). */
  reset(): void;
  /** The installed implementation, or null when running on the fallback. */
  peek(): T | null;
}

/**
 * Create a globally-shared adapter slot.
 *
 * @param name unique slot name; must stay stable across releases because it is
 *   the only thing tying duplicated chunks together.
 * @param createFallback builds the default implementation on first use.
 */
export function createAdapterSingleton<T>(name: string, createFallback: () => T): AdapterSingleton<T> {
  const registry = getRegistry();
  registry[name] ??= { active: null, fallback: null };
  const slot = registry[name] as AdapterSlot<T>;

  return {
    set(adapter: T): void {
      slot.active = adapter;
    },
    use(): T {
      if (slot.active) return slot.active;
      slot.fallback ??= createFallback();
      return slot.fallback;
    },
    reset(): void {
      slot.active = null;
    },
    peek(): T | null {
      return slot.active;
    },
  };
}
