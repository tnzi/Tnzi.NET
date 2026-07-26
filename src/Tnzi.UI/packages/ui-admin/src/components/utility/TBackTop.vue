<script setup lang="ts">
/**
 * `TBackTop` - Phase H4 L6: thin Naive UI `NBackTop` wrapper that
 * watches the nearest scrollable ancestor (or window) and shows a
 * floating circular button in the bottom-right when scrolled past
 * a threshold. Click → smooth-scroll to top.
 *
 * soybean uses a similar component but plain HTML + scroll
 * listener; NBackTop already does all this plus respects RTL and
 * keyboard. Mirrors soybean visually (24px primary icon in a 40px
 * circle, ~24px from the corner).
 */
import { NBackTop } from 'naive-ui'
import { Icon } from '@iconify/vue'

interface Props {
  /** Distance from the bottom edge in pixels. Default 24. */
  bottom?: number
  /** Distance from the right edge in pixels. Default 24. */
  right?: number
  /** Scroll offset (px) past which the button appears. Default 200. */
  visibilityHeight?: number
  /** Optional selector or element to scroll. Defaults to window. */
  to?: string
}

withDefaults(defineProps<Props>(), {
  bottom: 24,
  right: 24,
  visibilityHeight: 200,
  to: undefined,
})
</script>

<template>
  <NBackTop
    class="t-back-top"
    :right="right"
    :bottom="bottom"
    :visibility-height="visibilityHeight"
    :to="to ?? undefined"
  >
    <Icon icon="mdi:chevron-up" width="20" height="20" />
  </NBackTop>
</template>

<style scoped>
.t-back-top :deep(.n-back-top) {
  /* NBackTop already renders as a circle; let it use its theme defaults.
     We just lift the z-index above the tabs (70) but below drawers
     (1000+) so it floats on top of content. */
  z-index: var(--tnzi-admin-z-back-top, 90);
}
</style>
