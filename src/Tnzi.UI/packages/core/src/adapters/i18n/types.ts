/**
 * @tnzi/core/adapters/i18n/types
 *
 * i18n type definitions.
 */

/** Supported locale */
export type Locale = 'en' | 'en-US' | 'zh-CN' | 'zh-TW';

/** Locale display name */
export interface LocaleInfo {
  code: Locale;
  name: string;
  nativeName: string;
}

/** Translation key type */
export type TranslationKey = string;

/** Translation function type */
export type TranslateFunction = (key: TranslationKey, params?: Record<string, string | number>) => string;

/** i18n context type */
export interface I18nContext {
  /** Current locale */
  locale: Locale;
  /** Translation function */
  t: TranslateFunction;
  /** Change locale */
  setLocale: (locale: Locale) => void;
  /** Available locales */
  locales: LocaleInfo[];
}

