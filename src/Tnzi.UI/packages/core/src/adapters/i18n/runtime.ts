/**
 * @tnzi/core/adapters/i18n/runtime
 *
 * Framework-agnostic i18n runtime context helpers.
 */

import { createI18nContext, DEFAULT_LOCALE } from './create-i18n';
import type { I18nContext, Locale } from './types';
import { createAdapterSingleton } from '../singleton';

export interface I18nRuntime {
  provide(app?: unknown, locale?: Locale): I18nContext;
  use(): I18nContext;
  reset(locale?: Locale): I18nContext;
}

export interface I18nRuntimeOptions {
  locale?: Locale;
}

export function createI18nRuntime(options: I18nRuntimeOptions = {}): I18nRuntime {
  let activeContext: I18nContext = createI18nContext(options.locale ?? DEFAULT_LOCALE);

  return {
    provide(_app?: unknown, locale: Locale = DEFAULT_LOCALE): I18nContext {
      activeContext = createI18nContext(locale);
      return activeContext;
    },
    use(): I18nContext {
      return activeContext;
    },
    reset(locale: Locale = DEFAULT_LOCALE): I18nContext {
      activeContext = createI18nContext(locale);
      return activeContext;
    },
  };
}

/**
 * The active runtime, parked on the globalThis registry like every other
 * adapter slot: with `splitting: false` this module is inlined into each tsup
 * entry, and a module-level `let` would give the UI plugin's `provideI18n`
 * (through one entry) and a consumer's `useI18n` (through another) two
 * different runtimes. The default runtime is created lazily on first use and
 * shared the same way.
 */
const runtimeSlot = createAdapterSingleton<I18nRuntime>('i18n-runtime', () => createI18nRuntime());

export function setActiveI18nRuntime(runtime: I18nRuntime): void {
  runtimeSlot.set(runtime);
}

export function getActiveI18nRuntime(): I18nRuntime {
  return runtimeSlot.use();
}

/**
 * Provide/replace active i18n context.
 *
 * The first argument is intentionally unused to keep compatibility with UI plugins
 * that call `provideI18n(app, locale)` while keeping core framework-agnostic.
 */
export function provideI18n(_app?: unknown, locale: Locale = DEFAULT_LOCALE): I18nContext {
  return getActiveI18nRuntime().provide(_app, locale);
}

/**
 * Get active i18n context.
 */
export function useI18n(): I18nContext {
  return getActiveI18nRuntime().use();
}

export function resetI18n(locale: Locale = DEFAULT_LOCALE): I18nContext {
  return getActiveI18nRuntime().reset(locale);
}

/**
 * Reset i18n runtime to default. For tests and SSR isolation.
 */
export function resetI18nRuntime(): void {
  runtimeSlot.reset();
}

