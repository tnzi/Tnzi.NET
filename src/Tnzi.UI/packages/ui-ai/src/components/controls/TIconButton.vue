<script setup lang="ts">
/**
 * TIconButton - a square button that is only an icon.
 *
 * `label` names it for screen readers and is its tooltip (`vTooltip`, under
 * the button by default, or the side an ancestor's `data-tooltip-side` names;
 * `tooltipSide` moves it). `sm` (30px, the default) is as tall as `TButton`
 * and the fields beside it in a row of pages and dialogs; `xs` (28px) is for
 * a dense list row or inside a field; `md` (34px) is for a header that needs
 * a larger target. `active` shows that what it switches is on (and sets
 * `aria-pressed`); `danger` turns its hover red, for a delete; `inverse` is
 * for a dark bar (it takes the bar's text colour, see `TButton`).
 */
import { Icon } from '@iconify/vue'
import { vTooltip } from '../../headless/tooltip'
import type { FloatingSide } from '../../headless/place-beside'

withDefaults(
  defineProps<{
    icon: string
    label: string
    size?: 'xs' | 'sm' | 'md'
    disabled?: boolean
    danger?: boolean
    /** What it switches is on: drawn pressed, `aria-pressed="true"`. Leave unset for a plain action. */
    active?: boolean
    /** Side of the tooltip; unset follows the nearest `data-tooltip-side`, else below. */
    tooltipSide?: FloatingSide
    /** On a dark bar: takes the bar's text colour. */
    inverse?: boolean
  }>(),
  { size: 'sm', disabled: false, danger: false, active: undefined, tooltipSide: undefined, inverse: false },
)
</script>

<template>
  <button
    v-tooltip="{ text: label, side: tooltipSide }"
    type="button"
    class="t-icon-button"
    :class="[`t-icon-button--${size}`, { 't-icon-button--danger': danger, 't-icon-button--active': active, 't-icon-button--inverse': inverse }]"
    :aria-label="label"
    :aria-pressed="active === undefined ? undefined : active"
    :disabled="disabled"
  >
    <Icon :icon="icon" aria-hidden="true" />
  </button>
</template>

<style scoped>
.t-icon-button {
  /* Its own text metrics, not the page reset's (which differ: 1.15 and the
     system font in most resets, `inherit` under a utility preflight). */
  font-family: inherit;
  font-size: 100%;
  line-height: 1.5;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: none;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-icon-button--xs {
  width: 28px;
  height: 28px;
  font-size: 16px;
}
.t-icon-button--sm {
  width: 30px;
  height: 30px;
  font-size: 16px;
}
.t-icon-button--md {
  width: 34px;
  height: 34px;
  font-size: 18px;
}
.t-icon-button:hover:not(:disabled) {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-icon-button--active {
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
}
.t-icon-button--inverse,
.t-icon-button--inverse:hover:not(:disabled) {
  color: inherit;
}
.t-icon-button--inverse:hover:not(:disabled),
.t-icon-button--inverse.t-icon-button--active {
  background: color-mix(in srgb, currentColor 14%, transparent);
}
.t-icon-button--danger:hover:not(:disabled) {
  background: var(--tnzi-ai-danger-soft);
  color: var(--tnzi-ai-danger);
}
.t-icon-button:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-icon-button:disabled {
  opacity: 0.4;
  cursor: default;
}
</style>
