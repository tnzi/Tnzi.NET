/**
 * usePlaygroundI18n
 *
 * 为 shadcn playground 提供 i18n 支持。
 */

import { ref, watch } from 'vue';
import { createI18nContext, LOCALES } from '@tnzi/core/adapters/i18n';
import type { I18nContext, Locale, TranslateFunction } from '@tnzi/core/adapters/i18n';

const currentLocale = ref<Locale>('en');
let i18nCtx: I18nContext = createI18nContext('en');

export function usePlaygroundI18n() {
  const t: TranslateFunction = (key, params?) => i18nCtx.t(key, params);

  const setLocale = (locale: Locale) => {
    currentLocale.value = locale;
    i18nCtx = createI18nContext(locale);
  };

  return {
    locale: currentLocale,
    t,
    setLocale,
    locales: LOCALES,
  };
}
