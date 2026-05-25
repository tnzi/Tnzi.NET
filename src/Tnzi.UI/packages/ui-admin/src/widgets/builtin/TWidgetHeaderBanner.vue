<script setup lang="ts">
/**
 * `TWidgetHeaderBanner` — Workbench widget wrapping THeaderBanner.
 *
 * Pulls the display name from `useAdminLoginConfig().user?.userName` when
 * the consumer doesn't pass `userName` explicitly, so the default
 * Workbench can render a personalised banner with zero config. Also wires
 * the bundled-locale translator so the greeting (`Good morning`, …)
 * tracks the active locale.
 */
import { computed } from 'vue'
import THeaderBanner from '../../components/dashboard/THeaderBanner.vue'
import { useAdminLoginConfig } from '../../plugin/loginConfig'
import { translatePageKey } from '../../pages/_shared/translate'

interface Props {
  /** Display name override — defaults to `loginConfig.user?.userName`. */
  userName?: string
  /** Static greeting override (skips the time-of-day default). */
  greeting?: string
  /** Subtitle / motto under the greeting. */
  subtitle?: string
  /** Hide the live datetime ticker. */
  hideTime?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  userName: undefined,
  greeting: undefined,
  subtitle: undefined,
  hideTime: false,
})

const loginConfig = useAdminLoginConfig()

const resolvedUserName = computed(
  () => props.userName ?? loginConfig.user?.userName ?? translatePageKey('', 'admin.banner.userFallback') ?? 'there',
)

function bannerTranslate(key: string): string {
  return translatePageKey('', key)
}
</script>

<template>
  <THeaderBanner
    :user-name="resolvedUserName"
    :greeting="greeting ?? ''"
    :subtitle="subtitle ?? ''"
    :hide-time="hideTime"
    :translate="bannerTranslate"
  />
</template>
