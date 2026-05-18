<script setup lang="ts">
/**
 * `TButtonIcon` — header / toolbar icon button with optional NTooltip
 * wrapper. Direct port of soybean's `custom/button-icon.vue`.
 *
 * Mirrors soybean usage:
 *
 *     <TButtonIcon :tooltip-content="$t('icon.search')" icon="heroicons:magnifying-glass">
 *
 * Soybean reference: `src/components/custom/button-icon.vue:36-46`.
 *
 * We expose the same `icon` shorthand for iconify strings AND a default
 * slot for callers that need custom content (e.g. an SVG with state).
 */
import { NButton, NTooltip } from 'naive-ui'
import { Icon } from '@iconify/vue'

interface Props {
  /** Iconify icon name (mdi:..., material-symbols:..., etc.). */
  icon?: string
  /** Tooltip placement; defaults to bottom (matches soybean). */
  tooltipPlacement?:
    | 'top'
    | 'top-start'
    | 'top-end'
    | 'bottom'
    | 'bottom-start'
    | 'bottom-end'
    | 'left'
    | 'left-start'
    | 'left-end'
    | 'right'
    | 'right-start'
    | 'right-end'
  /** Tooltip content. When empty / null the tooltip is suppressed. */
  tooltipContent?: string
  /** NButton size — `medium` matches the rest of the admin chrome. */
  size?: 'tiny' | 'small' | 'medium' | 'large'
  /** Iconify icon size (px). */
  iconSize?: number
}

withDefaults(defineProps<Props>(), {
  icon: undefined,
  tooltipPlacement: 'bottom',
  tooltipContent: undefined,
  size: 'medium',
  iconSize: 20,
})
</script>

<template>
  <NTooltip v-if="tooltipContent" :placement="tooltipPlacement" trigger="hover">
    <template #trigger>
      <NButton quaternary circle :size="size" class="t-button-icon">
        <template #icon>
          <slot>
            <Icon v-if="icon" :icon="icon" :width="iconSize" :height="iconSize" />
          </slot>
        </template>
      </NButton>
    </template>
    {{ tooltipContent }}
  </NTooltip>
  <NButton v-else quaternary circle :size="size" class="t-button-icon">
    <template #icon>
      <slot>
        <Icon v-if="icon" :icon="icon" :width="iconSize" :height="iconSize" />
      </slot>
    </template>
  </NButton>
</template>

<style scoped>
.t-button-icon {
  /* Match soybean's button-icon: 36px square with the icon centred.
     NButton's circular variant already does this; the class is kept
     so consumers can target the wrapper if they need. */
}
</style>
