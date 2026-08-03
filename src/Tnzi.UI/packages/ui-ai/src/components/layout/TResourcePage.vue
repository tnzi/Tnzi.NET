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
 * It holds no data and fetches nothing - `@tnzi/ui-ai` never owns transport.
 * Consumers pass `loading` / `empty` and fill the slots.
 */
withDefaults(
  defineProps<{
    /** Swap the body for a spinner-free skeleton hint. */
    loading?: boolean
    /** Show the `empty` slot instead of the body. */
    empty?: boolean
    /** Constrain the body to a readable column. Omit for full-bleed grids. */
    maxWidth?: number | string
  }>(),
  {
    loading: false,
    empty: false,
    maxWidth: undefined,
  },
)
</script>

<template>
  <div class="t-resource-page">
    <div v-if="$slots.toolbar" class="t-resource-page__toolbar">
      <slot name="toolbar" />
    </div>

    <div
      class="t-resource-page__body"
      :style="maxWidth ? { maxWidth: typeof maxWidth === 'number' ? `${maxWidth}px` : maxWidth } : undefined"
    >
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
  display: flex;
  flex-direction: column;
  min-height: 100%;
}
.t-resource-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 4px 32px 16px;
  flex-shrink: 0;
}
.t-resource-page__body {
  flex: 1;
  min-height: 0;
  width: 100%;
  padding: 0 32px 32px;
  margin: 0 auto;
  box-sizing: border-box;
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
</style>
