/**
 * Naive UI locale bridge: maps a locale code to Naive UI's NLocale +
 * NDateLocale pair. Kept out of the `i18n` barrel on purpose: that barrel is
 * reached from ~45 components, and this module pulls naive's locale objects.
 */

import { enUS, zhCN, dateEnUS, dateZhCN } from 'naive-ui'
import type { NLocale, NDateLocale } from 'naive-ui'

export interface NaiveLocaleBundle {
  locale: NLocale
  dateLocale: NDateLocale
}

export function getNaiveLocale(locale: string): NaiveLocaleBundle {
  switch (locale) {
    case 'zh-cn':
    case 'zh-CN':
      return { locale: zhCN, dateLocale: dateZhCN }
    case 'en':
    case 'en-US':
    default:
      return { locale: enUS, dateLocale: dateEnUS }
  }
}
