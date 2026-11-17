<script setup lang="ts">
/**
 * TStateView - the three states every list has besides its content. `loading`
 * shows placeholder rows the shape of the content; `error` says what happened
 * and offers to try again; `empty` says why there is nothing and what to do
 * about it. With none of them set, the default slot is the content.
 *
 * A content page's empty state passes `emptyArt` (or fills `#empty-art`) and
 * gets `TEmptyState`, the drawing every page leads with, and `#empty-more` is
 * what the page offers under it, such as templates; without one (a panel, a
 * dialog, a search that found nothing) the empty state is an icon.
 *
 * The retry button shows when the page listens to `retry`.
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import TButton from './TButton.vue'
import TEmptyState from './TEmptyState.vue'
import type { EmptyArt } from './empty-art'
import { useControlsText } from './controls-text'

const props = withDefaults(
  defineProps<{
    loading?: boolean
    /** What went wrong, already in words. */
    error?: string | null
    empty?: boolean
    /** The page's drawing: set it for a content page that has nothing yet. */
    emptyArt?: EmptyArt | null
    emptyIcon?: string
    emptyTitle?: string
    emptyText?: string
    /** Placeholder rows while loading. */
    rows?: number
    /** Compact: inside a dialog or a card rather than a page. */
    compact?: boolean
    /** Declared so the retry button can show only when someone listens. */
    onRetry?: () => void
  }>(),
  {
    loading: false,
    error: null,
    empty: false,
    emptyArt: null,
    emptyIcon: 'lucide:inbox',
    emptyTitle: '',
    emptyText: '',
    rows: 3,
    compact: false,
    onRetry: undefined,
  },
)

const text = useControlsText()
const canRetry = computed(() => props.onRetry !== undefined)
</script>

<template>
  <div v-if="loading" class="t-state t-state--loading" :class="{ 'is-compact': compact }" aria-busy="true" data-state="loading">
    <span class="sr-only">{{ text.loading }}</span>
    <div v-for="row in rows" :key="row" class="t-state__skeleton" />
  </div>
  <div v-else-if="error" class="t-state t-state--message" :class="{ 'is-compact': compact }" role="alert" data-state="error">
    <Icon icon="lucide:cloud-off" class="t-state__icon t-state__icon--error" aria-hidden="true" />
    <p class="t-state__text t-state__text--error" :title="error">{{ error }}</p>
    <TButton v-if="canRetry" icon="lucide:rotate-ccw" @click="onRetry?.()">{{ text.retry }}</TButton>
  </div>
  <TEmptyState v-else-if="empty && (emptyArt || $slots['empty-art'])" :art="emptyArt" :title="emptyTitle" :text="emptyText" data-state="empty">
    <template v-if="$slots['empty-art']" #art><slot name="empty-art" /></template>
    <template v-if="$slots.action" #action><slot name="action" /></template>
    <slot name="empty-more" />
  </TEmptyState>
  <div v-else-if="empty" class="t-state t-state--message" :class="{ 'is-compact': compact }" data-state="empty">
    <Icon :icon="emptyIcon" class="t-state__icon" aria-hidden="true" />
    <p v-if="emptyTitle" class="t-state__title">{{ emptyTitle }}</p>
    <p v-if="emptyText" class="t-state__text">{{ emptyText }}</p>
    <div v-if="$slots.action" class="t-state__action"><slot name="action" /></div>
  </div>
  <slot v-else />
</template>

<style scoped>
.t-state--loading {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.t-state__skeleton {
  height: 64px;
  border-radius: 14px;
  background: var(--tnzi-ai-hover);
  animation: t-state-pulse 1.4s ease-in-out infinite;
}
/* On a phone a quick load shows no placeholder at all: it appears only once the wait is noticeable. */
@media (max-width: 767px) {
  .t-state--loading {
    animation: t-state-appear 160ms ease-out 180ms both;
  }
}
@keyframes t-state-appear {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}
.is-compact .t-state__skeleton {
  height: 44px;
  border-radius: 10px;
}
.t-state__skeleton:nth-child(3) {
  animation-delay: 0.15s;
}
.t-state__skeleton:nth-child(4) {
  animation-delay: 0.3s;
}
@keyframes t-state-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.45;
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-state__skeleton,
  .t-state--loading {
    animation: none;
  }
}
.t-state--message {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 48px 16px;
  text-align: center;
}
.t-state--message.is-compact {
  padding: 24px 12px;
}
.t-state__icon {
  font-size: 32px;
  color: var(--tnzi-ai-text-tertiary);
}
.is-compact .t-state__icon {
  font-size: 24px;
}
.t-state__icon--error {
  color: var(--tnzi-ai-danger);
}
.t-state__title {
  margin: 4px 0 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-ai-text);
}
.t-state__text {
  max-width: 440px;
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--tnzi-ai-text-secondary);
  overflow-wrap: anywhere;
}
/* What went wrong is a sentence or two; a longer one is cut rather than pushing the rest of its panel away. */
.t-state__text--error {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 4;
  line-clamp: 4;
  overflow: hidden;
}
.t-state__action {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 8px;
}
</style>
