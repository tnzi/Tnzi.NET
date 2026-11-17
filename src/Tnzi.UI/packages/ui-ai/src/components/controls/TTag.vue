<script setup lang="ts">
/**
 * TTag - a short status word beside a name ("Online", "High risk", "Paused").
 *
 * A 22px pill tinted with its tone at 10%, the words in the tone itself
 * (`accent` uses the accent as text, so a light brand colour still reads).
 * `dot` puts a dot of the tone before the words. `size="sm"` is 18px with
 * 11px words, for a dense list whose rows a 22px pill would make taller.
 * It never shrinks in a row: the name beside it gives way, not the tag.
 */
withDefaults(
  defineProps<{
    tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info'
    dot?: boolean
    /** `md` 22px (default), `sm` 18px for dense lists. */
    size?: 'sm' | 'md'
  }>(),
  { tone: 'neutral', dot: false, size: 'md' },
)
</script>

<template>
  <span class="t-tag" :class="[`t-tag--${tone}`, { 't-tag--dot': dot, 't-tag--sm': size === 'sm' }]">
    <span v-if="dot" class="t-tag__dot" aria-hidden="true" />
    <slot />
  </span>
</template>

<style scoped>
.t-tag {
  --t-tag-color: var(--tnzi-ai-text-secondary);
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: 5px;
  height: 22px;
  padding: 0 8px;
  border-radius: var(--tnzi-ai-radius-pill, 999px);
  background: color-mix(in srgb, var(--t-tag-color) 10%, transparent);
  color: var(--t-tag-color);
  font-size: 12px;
  font-weight: 500;
  line-height: 1;
  white-space: nowrap;
}
.t-tag--dot {
  padding-left: 7px;
}
.t-tag--sm {
  gap: 4px;
  height: 18px;
  padding: 0 6px;
  font-size: 11px;
}
.t-tag--sm.t-tag--dot {
  padding-left: 5px;
}
.t-tag--sm .t-tag__dot {
  width: 5px;
  height: 5px;
}
.t-tag__dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--t-tag-color);
}
.t-tag--accent {
  --t-tag-color: var(--tnzi-ai-accent-text);
}
.t-tag--success {
  --t-tag-color: var(--tnzi-ai-success);
}
.t-tag--warning {
  --t-tag-color: var(--tnzi-ai-warning);
}
.t-tag--danger {
  --t-tag-color: var(--tnzi-ai-danger);
}
.t-tag--info {
  --t-tag-color: var(--tnzi-ai-info);
}
</style>
