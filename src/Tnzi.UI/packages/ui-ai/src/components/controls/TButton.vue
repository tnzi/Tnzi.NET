<script setup lang="ts">
/**
 * TButton - the package's button.
 *
 * `primary` is the accent fill, kept for the one action a surface is for;
 * `soft` is a light accent tint with accent text, for the main action of each
 * of several cards on one page (a yellow fill on every card is too loud);
 * `secondary` (the default) is outlined; `ghost` sits quietly in toolbars and
 * rows; `danger` is for what cannot be undone; `link` looks like a link in
 * running text ("View all commits") and behaves as a button. While `loading`
 * it keeps its width, shows a spinner in place of its icon and refuses clicks.
 *
 * `inverse` is for a `ghost` or `link` button on a dark (inverted) bar, such
 * as a notice strip in the tooltip colours: it takes the bar's text colour
 * (`currentColor`) and its hover is a tint of that colour, instead of the
 * page's text colour, which would vanish on the bar.
 *
 * `sm` (30px, the default) is the one size of an app's pages and dialogs and
 * matches `.t-input` / `.t-select`; `md` (36px) is for the primary actions of
 * the standalone pages a visitor lands on (a shared link, an invitation, a
 * consent page), with `.t-input--md` / `.t-select--md` beside it.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    variant?: 'primary' | 'soft' | 'secondary' | 'ghost' | 'danger' | 'link'
    size?: 'sm' | 'md'
    type?: 'button' | 'submit' | 'reset'
    /** Iconify name shown before the label. */
    icon?: string
    loading?: boolean
    disabled?: boolean
    /** Take the full width of the container. */
    block?: boolean
    /** On a dark bar: takes the bar's text colour (`ghost` and `link`). */
    inverse?: boolean
  }>(),
  { variant: 'secondary', size: 'sm', type: 'button', icon: '', loading: false, disabled: false, block: false, inverse: false },
)
</script>

<template>
  <button
    :type="type"
    class="t-button"
    :class="[`t-button--${variant}`, `t-button--${size}`, { 't-button--block': block, 't-button--inverse': inverse, 'is-loading': loading }]"
    :disabled="disabled || loading"
    :aria-busy="loading || undefined"
  >
    <Icon v-if="loading" icon="lucide:loader-circle" class="t-button__icon t-button__spin" aria-hidden="true" />
    <Icon v-else-if="icon" :icon="icon" class="t-button__icon" aria-hidden="true" />
    <span v-if="$slots.default" class="t-button__label"><slot /></span>
  </button>
</template>

<style scoped>
.t-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-width: 0;
  box-sizing: border-box;
  border: 1px solid transparent;
  font-family: inherit;
  font-weight: 500;
  line-height: 1;
  white-space: nowrap;
  cursor: pointer;
  transition:
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-button--sm {
  height: 30px;
  padding: 0 10px;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  font-size: 13px;
}
.t-button--md {
  height: 36px;
  padding: 0 14px;
  border-radius: 10px;
  font-size: 14px;
}
.t-button--block {
  width: 100%;
}
.t-button__label {
  overflow: hidden;
  text-overflow: ellipsis;
  /* The button's line-height of 1 would clip descenders at the label's
     clipped edge: the label gets a full line box, centred in the fixed height. */
  line-height: 1.35;
}
.t-button__icon {
  flex-shrink: 0;
  font-size: 16px;
}
.t-button--sm .t-button__icon {
  font-size: 15px;
}
.t-button__spin {
  animation: t-button-spin 0.9s linear infinite;
}
@keyframes t-button-spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-button__spin {
    animation-duration: 2.4s;
  }
}

.t-button--primary {
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
}
.t-button--primary:hover:not(:disabled) {
  background: color-mix(in srgb, var(--tnzi-ai-accent) 88%, black);
}
.t-button--secondary {
  background: var(--tnzi-ai-surface);
  border-color: var(--tnzi-ai-border-strong);
  color: var(--tnzi-ai-text);
}
.t-button--secondary:hover:not(:disabled) {
  background: var(--tnzi-ai-hover);
}
.t-button--ghost {
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
}
.t-button--ghost:hover:not(:disabled) {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-button--soft {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent-text);
}
.t-button--soft:hover:not(:disabled) {
  background: color-mix(in srgb, var(--tnzi-ai-accent) 22%, transparent);
}
/* A link in looks, a button in behaviour: no box, the accent as text,
   underlined on hover. As tall as its line, so it sits in running text. */
.t-button--link {
  height: auto;
  padding: 0;
  border: none;
  border-radius: var(--tnzi-ai-radius-xs, 4px);
  background: none;
  color: var(--tnzi-ai-accent-text);
}
.t-button--link:hover:not(:disabled) {
  text-decoration: underline;
  text-underline-offset: 3px;
}
/* On a dark bar: the bar's own text colour, and a tint of it on hover. */
.t-button--inverse.t-button--ghost,
.t-button--inverse.t-button--link,
.t-button--inverse.t-button--ghost:hover:not(:disabled),
.t-button--inverse.t-button--link:hover:not(:disabled) {
  color: inherit;
}
.t-button--inverse.t-button--ghost:hover:not(:disabled) {
  background: color-mix(in srgb, currentColor 14%, transparent);
}
.t-button--danger {
  background: var(--tnzi-ai-danger);
  color: var(--tnzi-ai-on-status);
}
.t-button--danger:hover:not(:disabled) {
  background: color-mix(in srgb, var(--tnzi-ai-danger) 88%, black);
}
.t-button:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-button:disabled {
  opacity: 0.5;
  cursor: default;
}
.t-button.is-loading {
  opacity: 0.8;
  cursor: progress;
}
</style>
