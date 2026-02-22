/**
 * usePlaygroundI18n
 *
 * Vant playground i18n composable.
 * Wraps @tnzi/core i18n context and syncs Vant's built-in locale.
 */

import { ref } from 'vue';
import { createI18nContext, LOCALES } from '@tnzi/core/adapters/i18n';
import type { I18nContext, Locale, TranslateFunction } from '@tnzi/core/adapters/i18n';
import { Locale as VantLocale } from 'vant';
import enUS from 'vant/es/locale/lang/en-US';
import zhCN from 'vant/es/locale/lang/zh-CN';

const currentLocale = ref<Locale>('en');
let i18nCtx: I18nContext = createI18nContext('en');

export function usePlaygroundI18n() {
  const t: TranslateFunction = (key, params?) => i18nCtx.t(key, params);

  const setLocale = (locale: Locale) => {
    currentLocale.value = locale;
    i18nCtx = createI18nContext(locale);

    // Sync Vant built-in component locale
    if (locale === 'zh-CN' || locale === 'zh-TW') {
      VantLocale.use('zh-CN', zhCN);
    } else {
      VantLocale.use('en-US', enUS);
    }
  };

  return {
    locale: currentLocale,
    t,
    setLocale,
    locales: LOCALES,
  };
}
