<script setup lang="ts">
/**
 * @experimental
 * TResourcePage - the shell every non-conversation page in an AI product ends
 * up needing: an optional toolbar, a scrolling body, an empty state, and a
 * loading state.
 *
 * Derived from the shape real agent products repeat across their Agents /
 * Scheduled / Plugins / Library screens: page name in the **top bar** (not
 * repeated in the body), an optional filter/search/view-toggle row, then a
 * grid or list, with a centred empty state carrying the primary call to
 * action. `TChatApp` already renders the page name in its top bar, so this
 * component deliberately has no title of its own.
 *
 * The toolbar row is `TPageToolbar` in the `#toolbar` slot. Toolbar and body
 * share one column: `max-width="page"` is the package's readable page width
 * (`--tnzi-ai-page-max-width`), so every content page lines up with the
 * others; a number or CSS length sets another; none leaves both full-bleed.
 * The gutters are `--tnzi-ai-page-padding-x` (`-narrow` on a phone), the space
 * under the body `--tnzi-ai-page-padding-bottom` or `bodyPaddingBottom` (a page
 * with something floating over its bottom edge, an upload tray, needs more).
 *
 * It holds no data and fetches nothing - `@tnzi/ui-ai` never owns transport.
 * Consumers pass `loading` / `empty` and fill the slots.
 */
import { computed } from 'vue'
import { useViewportBelow } from '../../headless/useViewportBelow'

const props = withDefaults(
  defineProps<{
    /** Swap the body for a spinner-free skeleton hint. */
    loading?: boolean
    /** Show the `empty` slot instead of the body. */
    empty?: boolean
    /**
     * Constrain the toolbar and the body to one readable column: `'page'` for
     * the package's page width, a number (px) or a CSS length for another.
     * Omit for full-bleed grids.
     */
    maxWidth?: number | string
    /** Space under the body (number = px), e.g. room for a tray floating over the bottom. */
    bodyPaddingBottom?: number | string
    /** Viewport width (px) below which the phone gutters apply. Default 768. */
    narrowBreakpoint?: number
    /** Force the phone gutters (`true`) or the wide ones (`false`). */
    narrow?: boolean
  }>(),
  {
    loading: false,
    empty: false,
    maxWidth: undefined,
    bodyPaddingBottom: undefined,
    narrowBreakpoint: 768,
    narrow: undefined,
  },
)

const viewportNarrow = useViewportBelow(() => props.narrowBreakpoint)
const isNarrow = computed(() => props.narrow ?? viewportNarrow.value)

const length = (value: number | string) => (typeof value === 'number' ? `${value}px` : value)

const columnStyle = computed(() => {
  const style: Record<string, string> = {}
  const width = props.maxWidth
  if (width !== undefined && width !== '') {
    style['--t-resource-page-max-width'] = width === 'page' ? 'var(--tnzi-ai-page-max-width, 1040px)' : length(width)
  }
  const bottom = props.bodyPaddingBottom
  if (bottom !== undefined && bottom !== '') style['--t-resource-page-bottom'] = length(bottom)
  return Object.keys(style).length ? style : undefined
})
</script>

<template>
  <div class="t-resource-page" :class="{ 't-resource-page--narrow': isNarrow }" :style="columnStyle">
    <div v-if="$slots.toolbar" class="t-resource-page__toolbar">
      <slot name="toolbar" />
    </div>

    <div class="t-resource-page__body">
      <div v-if="loading" class="t-resource-page__state">
        <slot name="loading">
          <div class="t-resource-page__skeleton" />
          <div class="t-resource-page__skeleton" />
          <div class="t-resource-page__skeleton" />
        </slot>
      </div>

      <div v-else-if="empty" class="t-resource-page__state">
        <slot name="empty" />
      </div>

      <slot v-else />
    </div>
  </div>
</template>

<style scoped>
.t-resource-page {
  --t-resource-page-gutter: var(--tnzi-ai-page-padding-x, 32px);
  display: flex;
  flex-direction: column;
  min-height: 100%;
}
.t-resource-page--narrow {
  --t-resource-page-gutter: var(--tnzi-ai-page-padding-x-narrow, 16px);
}
/* Toolbar and body: the same column and the same gutters, so the toolbar's
   last button lines up with the right edge of the list under it. */
.t-resource-page__toolbar,
.t-resource-page__body {
  box-sizing: border-box;
  width: 100%;
  max-width: var(--t-resource-page-max-width, none);
  margin: 0 auto;
  padding-left: var(--t-resource-page-gutter);
  padding-right: var(--t-resource-page-gutter);
}
.t-resource-page__toolbar {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding-top: 4px;
  padding-bottom: 16px;
}
/* The standard toolbar takes the whole row. */
.t-resource-page__toolbar > :slotted(.t-page-toolbar) {
  flex: 1 1 auto;
}
.t-resource-page__body {
  flex: 1;
  min-height: 0;
  padding-bottom: var(--t-resource-page-bottom, var(--tnzi-ai-page-padding-bottom, 32px));
}
/* Centres the empty and loading states in whatever height is left, so a page
   with three items and a page with none both look deliberate. */
.t-resource-page__state {
  min-height: 320px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}
.t-resource-page__skeleton {
  width: min(520px, 100%);
  height: 56px;
  border-radius: 12px;
  background: var(--tnzi-ai-hover);
  animation: t-resource-page-pulse 1.4s ease-in-out infinite;
}
.t-resource-page__skeleton:nth-child(2) { animation-delay: 0.15s; }
.t-resource-page__skeleton:nth-child(3) { animation-delay: 0.3s; }
@keyframes t-resource-page-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.45; }
}
@media (prefers-reduced-motion: reduce) {
  .t-resource-page__skeleton {
    animation: none;
  }
}
</style>
