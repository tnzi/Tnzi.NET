<script setup lang="ts">
/**
 * @experimental
 * TUpgradeBanner — Soft-background CTA banner for plan / tier upgrades.
 *
 * Meant to nudge users toward a higher-tier plan when their current
 * agent hits its limits. Minimal state: a text line, a CTA link, and
 * an optional close button. All rendering is visual — consumers wire
 * the actual upgrade flow via the `cta` event.
 *
 * Use the default slot for custom body (e.g. rich text with inline
 * emphasis). The `text` prop is a convenience for the common case.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    /** Plain-text body. Ignored when the default slot has content. */
    text?: string
    /** CTA link label (e.g. "Upgrade", "Get Pro"). */
    ctaLabel?: string
    /** When `true`, renders the close button. */
    dismissible?: boolean
  }>(),
  {
    text: '',
    ctaLabel: 'Upgrade',
    dismissible: true,
  },
)

const emit = defineEmits<{
  /** Fired when the CTA link is clicked. */
  cta: []
  /** Fired when the close button is clicked. */
  dismiss: []
}>()
</script>

<template>
  <div class="t-upgrade-banner" role="note">
    <span class="t-upgrade-banner__text">
      <slot>{{ text }}</slot>
    </span>
    <a
      href="#"
      class="t-upgrade-banner__cta"
      @click.prevent="emit('cta')"
    >
      {{ ctaLabel }}
    </a>
    <button
      v-if="dismissible"
      type="button"
      class="t-upgrade-banner__close"
      aria-label="Dismiss"
      @click="emit('dismiss')"
    >
      <Icon icon="lucide:x" />
    </button>
  </div>
</template>

<style scoped>
.t-upgrade-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 10px;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-upgrade-banner__text {
  flex: 1;
  min-width: 0;
}
.t-upgrade-banner__text :deep(strong) {
  color: var(--tnzi-ai-text, #1a1a1a);
  font-weight: 500;
}
.t-upgrade-banner__cta {
  color: #2563eb;
  text-decoration: none;
  font-weight: 500;
  font-size: 13px;
  padding: 4px 2px;
}
.t-upgrade-banner__cta:hover {
  text-decoration: underline;
}
.t-upgrade-banner__close {
  width: 24px;
  height: 24px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  border-radius: 6px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  flex-shrink: 0;
}
.t-upgrade-banner__close:hover {
  background: var(--tnzi-ai-selected, rgba(55, 53, 47, 0.08));
  color: var(--tnzi-ai-text, #1a1a1a);
}
</style>
