<script setup lang="ts">
/**
 * Route-level exception page - the wired, router-aware wrapper around the
 * reusable {@link TExceptionPage} component. One component serves the `/403`,
 * `/404` and `/500` routes; the concrete error is read from
 * `route.meta.exceptionType` so the three routes share a single wiring point.
 *
 * Replaces the old crude `ForbiddenPlaceholder` (`h('div', '403 Forbidden')`)
 * that the `/403` route used to render. Follows soybean-admin's exception page
 * design (centered illustration + heading + primary CTA), localized via the
 * bundled admin locale pack and with CTAs wired to vue-router.
 */
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import TExceptionPage from '../../components/pages/TExceptionPage.vue'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { en } from '../../locales/en'
import { zhCn } from '../../locales/zh-cn'

type ExceptionCode = '403' | '404' | '500'

const route = useRoute()
const router = useRouter()
const appStore = useAdminAppStore()

const type = computed<ExceptionCode>(() => {
  const t = route.meta?.exceptionType
  return t === '403' || t === '404' || t === '500' ? t : '404'
})

/**
 * Resolve a dotted admin-locale key in the active locale; returns `fallback`
 * on miss (mirrors AdminShellRoot's `defaultTranslate`). Consumers who register
 * their own i18n stack still get sensible bundled defaults here.
 */
function tr(key: string, fallback: string): string {
  const messages = (appStore.locale === 'zh-cn' ? zhCn : en) as Record<string, unknown>
  const normalized = key.startsWith('tnzi.') ? key.slice(5) : key
  let node: unknown = messages
  for (const part of normalized.split('.')) {
    if (typeof node === 'object' && node !== null && part in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[part]
    } else {
      return fallback
    }
  }
  return typeof node === 'string' ? node : fallback
}

const title = computed(() => tr(`admin.exception.${type.value}.title`, type.value))
const subtitle = computed(() => tr(`admin.exception.${type.value}.subtitle`, ''))
const primaryLabel = computed(() => tr('admin.exception.actions.home', 'Back to home'))

// A single "Back to home" CTA - one clear escape hatch instead of a redundant
// "Back to home" + "Go back" pair (matches soybean-admin's exception pages).
function onPrimary(): void {
  void router.push({ name: 'dashboard' }).catch(() => undefined)
}
</script>

<template>
  <TExceptionPage
    :type="type"
    :title="title"
    :subtitle="subtitle"
    :primary-label="primaryLabel"
    @primary="onPrimary"
  />
</template>
