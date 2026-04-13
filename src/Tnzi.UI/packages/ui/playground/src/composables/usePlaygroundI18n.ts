import { ref, computed } from 'vue';
import { createI18nContext, LOCALES } from '@tnzi/core/adapters/i18n';
import type { I18nContext, Locale, TranslateFunction } from '@tnzi/core/adapters/i18n';
import { zhCN, dateZhCN, enUS, dateEnUS } from 'naive-ui';

const currentLocale = ref<Locale>('en');
let i18nCtx: I18nContext = createI18nContext('en');

export function usePlaygroundI18n() {
  const t: TranslateFunction = (key, params?) => i18nCtx.t(key, params);

  const setLocale = (locale: Locale) => {
    currentLocale.value = locale;
    i18nCtx = createI18nContext(locale);
  };

  const naiveLocale = computed(() => currentLocale.value === 'zh-CN' ? zhCN : enUS);
  const naiveDateLocale = computed(() => currentLocale.value === 'zh-CN' ? dateZhCN : dateEnUS);

  return {
    locale: currentLocale,
    t,
    setLocale,
    locales: LOCALES,
    naiveLocale,
    naiveDateLocale,
  };
}
