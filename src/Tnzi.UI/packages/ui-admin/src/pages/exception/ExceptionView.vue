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
import { translateChromeKey } from '../../i18n/translate'

type ExceptionCode = '403' | '404' | '500'

const route = useRoute()
const router = useRouter()

const type = computed<ExceptionCode>(() => {
  const t = route.meta?.exceptionType
  return t === '403' || t === '404' || t === '500' ? t : '404'
})

/**
 * Keys resolve through the shared chrome resolver, which checks consumer
 * overrides before the bundled pack and returns the fallback verbatim on a
 * miss - the right policy here, because each fallback is a real English
 * string and not a key to be humanised.
 *
 * ⚠️ This file used to carry a private copy of the walk, written as a
 * deliberate hand-copy ("mirrors AdminShellRoot's defaultTranslate"), which
 * is exactly why fixing AdminShellRoot left it behind: it read the bundled
 * dictionary only and never saw `messageOverrides`, so 403 / 404 / 500
 * rendered in English on an otherwise translated console. Do not reintroduce
 * a local resolver - `translateChromeKey` is the entry point.
 */
const title = computed(() =>
  translateChromeKey(`admin.exception.${type.value}.title`, type.value),
)
// Empty fallback on purpose: TExceptionPage owns a richer per-type preset
// subtitle and uses it when this is blank.
const subtitle = computed(() => translateChromeKey(`admin.exception.${type.value}.subtitle`, ''))
const primaryLabel = computed(() => translateChromeKey('admin.exception.actions.home', 'Back to home'))

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
