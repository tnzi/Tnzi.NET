<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

/**
 * Unified icon component - accepts either an Iconify icon name (e.g.
 * `mdi:home`) or a local SVG sprite reference (`#sprite-id` or any URL
 * fragment). The Iconify form lazy-loads on first paint via @iconify/vue;
 * the local form renders an inline `<svg><use href=…/></svg>` so consumers
 * who ship hand-tuned brand glyphs via vite-plugin-svg-sprite stay fast.
 *
 * Inspired by soybean-admin's `SvgIcon` (src/components/custom/svg-icon.vue).
 */
interface Props {
  /** Iconify icon name, e.g. `mdi:account-circle` or `material-symbols:settings`. */
  icon?: string
  /** Local SVG sprite reference - pass without the leading `#`. */
  localIcon?: string
  /** Pixel size for both width and height. Defaults to 1em (inherits font-size). */
  size?: number | string
  /** Color - defaults to `currentColor`. */
  color?: string
}

const props = withDefaults(defineProps<Props>(), {
  icon: '',
  localIcon: '',
  size: '1em',
  color: 'currentColor',
})

const sizeValue = computed(() =>
  typeof props.size === 'number' ? `${props.size}px` : props.size,
)

const styleObj = computed(() => ({
  width: sizeValue.value,
  height: sizeValue.value,
  color: props.color,
}))

const useHref = computed(() => (props.localIcon ? `#${props.localIcon}` : ''))
</script>

<template>
  <svg
    v-if="localIcon"
    class="t-svg-icon"
    aria-hidden="true"
    :style="styleObj"
  >
    <use :href="useHref" />
  </svg>
  <Icon
    v-else-if="icon"
    class="t-svg-icon"
    :icon="icon"
    :style="styleObj"
  />
</template>

<style scoped>
.t-svg-icon {
  display: inline-block;
  flex-shrink: 0;
  vertical-align: -0.125em;
  overflow: hidden;
  fill: currentColor;
}
</style>
