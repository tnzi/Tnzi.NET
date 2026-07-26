<script setup lang="ts">
/**
 * `THint` - compact inline help/hint affordance (icon + popover).
 *
 * For NON-essential explanatory copy that would otherwise eat vertical space:
 * drop it right after a label or a control and the full text lives in a hover /
 * click popover instead of a permanent paragraph. One primitive for every "ⓘ"
 * / "?" / "!" tooltip across the ecosystem.
 *
 * The `type` picks a sensible default glyph + tone (info = ⓘ, help = ?, warning
 * = !, …); override the glyph with `icon`. Content is the `content` prop or the
 * default slot (rich content).
 */
import { computed } from 'vue'
import { NPopover } from 'naive-ui'
import type { PopoverProps } from 'naive-ui'
import TSvgIcon from './TSvgIcon.vue'

type HintType = 'info' | 'help' | 'tip' | 'warning' | 'success' | 'error'

const props = withDefaults(
  defineProps<{
    /** Preset glyph + tone. `help` = question mark, `warning` = exclamation. */
    type?: HintType
    /** Override the Iconify glyph (e.g. `mdi:shield-alert-outline`). */
    icon?: string
    /** Icon pixel size. Default 15. */
    size?: number
    /** Optional bold title above the body. */
    title?: string
    /** Popover body text. Ignored when the default slot is used. */
    content?: string
    /** Popover placement (NPopover). Default `top`. */
    placement?: PopoverProps['placement']
    /** Show trigger - hover (default), click, focus. */
    trigger?: 'hover' | 'click' | 'focus'
    /** Popover content max width (number = px). Default 280. */
    maxWidth?: number | string
    /** Accessible label for the trigger button. Falls back to title/content. */
    ariaLabel?: string
  }>(),
  {
    type: 'info',
    icon: undefined,
    size: 15,
    title: undefined,
    content: undefined,
    placement: 'top',
    trigger: 'hover',
    maxWidth: 280,
    ariaLabel: undefined,
  },
)

const ICONS: Record<HintType, string> = {
  info: 'mdi:information-outline',
  help: 'mdi:help-circle-outline',
  tip: 'mdi:lightbulb-outline',
  warning: 'mdi:alert-circle-outline',
  success: 'mdi:check-circle-outline',
  error: 'mdi:close-circle-outline',
}

const resolvedIcon = computed(() => props.icon || ICONS[props.type])
const maxWidthStyle = computed(() =>
  typeof props.maxWidth === 'number' ? `${props.maxWidth}px` : props.maxWidth,
)
</script>

<template>
  <NPopover :trigger="trigger" :placement="placement" :show-arrow="true">
    <template #trigger>
      <button
        type="button"
        class="t-hint"
        :class="`t-hint--${type}`"
        :aria-label="ariaLabel || title || content || 'hint'"
        @click.prevent
      >
        <TSvgIcon :icon="resolvedIcon" :size="size" />
      </button>
    </template>
    <div class="t-hint__content" :style="{ maxWidth: maxWidthStyle }">
      <div v-if="title" class="t-hint__title">{{ title }}</div>
      <div class="t-hint__body"><slot>{{ content }}</slot></div>
    </div>
  </NPopover>
</template>

<style scoped>
.t-hint {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  margin: 0;
  border: 0;
  background: transparent;
  cursor: help;
  vertical-align: middle;
  line-height: 1;
  color: var(--tnzi-base-text-muted, #999);
  transition: color 0.15s ease;
}
.t-hint:hover,
.t-hint:focus-visible {
  color: var(--tnzi-primary);
  outline: none;
}
.t-hint--warning {
  color: var(--tnzi-warning, #f0a020);
}
.t-hint--warning:hover,
.t-hint--warning:focus-visible {
  color: var(--tnzi-warning, #f0a020);
  filter: brightness(1.12);
}
.t-hint--error {
  color: var(--tnzi-error, #d03050);
}
.t-hint--error:hover,
.t-hint--error:focus-visible {
  color: var(--tnzi-error, #d03050);
  filter: brightness(1.12);
}
.t-hint--success {
  color: var(--tnzi-success, #18a058);
}
.t-hint--success:hover,
.t-hint--success:focus-visible {
  color: var(--tnzi-success, #18a058);
  filter: brightness(1.12);
}
.t-hint__content {
  font-size: 13px;
  line-height: 1.5;
}
.t-hint__title {
  font-weight: 600;
  margin-bottom: 4px;
  color: var(--tnzi-base-text);
}
.t-hint__body {
  color: var(--tnzi-base-text-muted, #888);
}
</style>
