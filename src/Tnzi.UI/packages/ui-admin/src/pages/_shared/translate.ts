import { en } from '../../locales/en'
import { zhCn } from '../../locales/zh-cn'
import { useAdminAppStore } from '../../stores/useAdminAppStore'

/**
 * Resolves a page-scoped i18n key within the admin.modules.{pageNs}.{key} namespace.
 * Falls back to returning `key` unchanged if the path is not found.
 * Full locale keys are wired up in Task 3.39; until then this gracefully falls back.
 */
export function translatePageKey(pageNs: string, key: string): string {
  const locale = useAdminAppStore().locale
  const messages = (locale === 'zh-cn' ? zhCn : en) as Record<string, unknown>
  const full = `admin.modules.${pageNs}.${key}`
  const parts = full.split('.')
  let node: unknown = messages
  for (const part of parts) {
    if (typeof node === 'object' && node !== null && part in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[part]
    } else {
      return key
    }
  }
  return typeof node === 'string' ? node : key
}
