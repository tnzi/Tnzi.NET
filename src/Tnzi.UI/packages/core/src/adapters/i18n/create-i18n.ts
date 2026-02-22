/**
 * @tnzi/core/adapters/i18n/create-i18n
 *
 * Create i18n context factory.
 */

import type { I18nContext, Locale, LocaleInfo, TranslateFunction, TranslationKey } from './types';
import enTranslations from './locales/en';
import zhCnTranslations from './locales/zh-cn';

/** Available locales */
export const LOCALES: LocaleInfo[] = [
  { code: 'en', name: 'English', nativeName: 'English' },
  { code: 'zh-CN', name: 'Chinese', nativeName: '简体中文' },
];

/** Default locale */
export const DEFAULT_LOCALE: Locale = 'en';

/** Translation messages (所有 Locale 映射到翻译包) */
const MESSAGES: Record<Locale, Record<string, unknown>> = {
  'en': enTranslations,
  'en-US': enTranslations,
  'zh-CN': zhCnTranslations,
  'zh-TW': zhCnTranslations,
};

/**
 * Create i18n context
 */
export function createI18nContext(initialLocale: Locale = DEFAULT_LOCALE): I18nContext {
  let currentLocale = initialLocale;

  const t: TranslateFunction = (key: TranslationKey, params?: Record<string, string | number>) => {
    const messages = MESSAGES[currentLocale];
    const value = getNestedValue(messages, key);

    if (!value) return key;

    if (params) {
      return interpolate(value, params);
    }

    return value;
  };

  return {
    locale: currentLocale,
    t,
    setLocale: (locale: Locale) => {
      currentLocale = locale;
    },
    locales: LOCALES,
  };
}

/**
 * Get nested value from object by dot notation
 */
function getNestedValue(obj: Record<string, unknown>, path: string): string {
  const value = path.split('.').reduce<unknown>((current, key) => {
    if (current && typeof current === 'object') return (current as Record<string, unknown>)[key];
    return undefined;
  }, obj);
  return typeof value === 'string' ? value : path;
}

/**
 * Interpolate parameters into message
 */
function interpolate(message: string, params: Record<string, string | number>): string {
  return message.replace(/\{(\w+)\}/g, (_, key) => String(params[key] ?? ''));
}

export type { I18nContext, Locale, LocaleInfo, TranslateFunction, TranslationKey };

