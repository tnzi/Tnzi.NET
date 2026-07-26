<script setup lang="ts">
import { NButton, NTooltip } from 'naive-ui'
import TSvgIcon from './TSvgIcon.vue'

/**
 * Icon button with optional tooltip - the workhorse for header action bars,
 * row actions, and toolbar pills. Wraps NButton + NTooltip and the project's
 * TSvgIcon to keep one consistent set of props for every clickable icon.
 *
 * Inspired by soybean-admin's `ButtonIcon` (src/components/custom/button-icon.vue).
 */
interface Props {
  /** Iconify icon name, e.g. `mdi:refresh`. */
  icon?: string
  /** Local SVG sprite id. */
  localIcon?: string
  /** Pixel size for the icon. Defaults to 18. */
  iconSize?: number | string
  /** Tooltip text. When empty the tooltip wrapper is skipped. */
  tooltip?: string
  /** Tooltip placement - passes through to NTooltip. Default `bottom`. */
  tooltipPlacement?:
    | 'top'
    | 'top-start'
    | 'top-end'
    | 'right'
    | 'right-start'
    | 'right-end'
    | 'bottom'
    | 'bottom-start'
    | 'bottom-end'
    | 'left'
    | 'left-start'
    | 'left-end'
  /** Naive button kind / type. */
  type?: 'default' | 'tertiary' | 'primary' | 'info' | 'success' | 'warning' | 'error'
  /** Disable button + tooltip. */
  disabled?: boolean
  /** Render as quaternary (transparent) - typical for header bars. Default true. */
  ghost?: boolean
  /** Aria-label override; falls back to `tooltip`. */
  ariaLabel?: string
}

// Props are accessed in the template directly via Vue's auto-import.
withDefaults(defineProps<Props>(), {
  icon: '',
  localIcon: '',
  iconSize: 18,
  tooltip: '',
  tooltipPlacement: 'bottom',
  type: 'tertiary',
  disabled: false,
  ghost: true,
  ariaLabel: '',
})

defineEmits<{ click: [event: MouseEvent] }>()
</script>

<template>
  <NTooltip
    v-if="tooltip && !disabled"
    :placement="tooltipPlacement"
    :show-arrow="true"
    trigger="hover"
  >
    <template #trigger>
      <NButton
        class="t-button-icon"
        :type="type"
        :quaternary="ghost"
        :disabled="disabled"
        circle
        :aria-label="ariaLabel || tooltip"
        @click="(e) => $emit('click', e)"
      >
        <template #icon>
          <TSvgIcon :icon="icon" :local-icon="localIcon" :size="iconSize" />
        </template>
      </NButton>
    </template>
    {{ tooltip }}
  </NTooltip>
  <NButton
    v-else
    class="t-button-icon"
    :type="type"
    :quaternary="ghost"
    :disabled="disabled"
    circle
    :aria-label="ariaLabel"
    @click="(e) => $emit('click', e)"
  >
    <template #icon>
      <TSvgIcon :icon="icon" :local-icon="localIcon" :size="iconSize" />
    </template>
  </NButton>
</template>

<style scoped>
.t-button-icon {
  --n-color-hover: var(--tnzi-admin-menu-item-hover-bg, transparent);
  --n-color-pressed: var(--tnzi-admin-menu-item-active-bg, transparent);
  transition:
    transform var(--tnzi-admin-motion-duration-fast, 0.15s) var(--tnzi-admin-motion-ease-out, ease);
}
.t-button-icon:active:not(:disabled) {
  transform: scale(0.92);
}
</style>
