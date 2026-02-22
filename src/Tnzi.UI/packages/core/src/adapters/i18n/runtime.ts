/**
 * @tnzi/core/adapters/i18n/runtime
 *
 * Framework-agnostic i18n runtime context helpers.
 */

import { createI18nContext, DEFAULT_LOCALE } from './create-i18n';
import type { I18nContext, Locale } from './types';

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

const defaultI18nRuntime = createI18nRuntime();
let activeI18nRuntime: I18nRuntime = defaultI18nRuntime;

export function setActiveI18nRuntime(runtime: I18nRuntime): void {
  activeI18nRuntime = runtime;
}

export function getActiveI18nRuntime(): I18nRuntime {
  return activeI18nRuntime;
}

/**
 * Provide/replace active i18n context.
 *
 * The first argument is intentionally unused to keep compatibility with UI plugins
 * that call `provideI18n(app, locale)` while keeping core framework-agnostic.
 */
export function provideI18n(_app?: unknown, locale: Locale = DEFAULT_LOCALE): I18nContext {
  return activeI18nRuntime.provide(_app, locale);
}

/**
 * Get active i18n context.
 */
export function useI18n(): I18nContext {
  return activeI18nRuntime.use();
}

export function resetI18n(locale: Locale = DEFAULT_LOCALE): I18nContext {
  return activeI18nRuntime.reset(locale);
}

