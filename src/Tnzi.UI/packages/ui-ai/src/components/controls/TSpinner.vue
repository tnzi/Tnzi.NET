<script setup lang="ts">
/**
 * TSpinner - a page or panel waiting on something it cannot draw yet: a ring
 * in the accent (as text, so a light brand colour still shows). `size="md"`
 * (default) is 24px, for a page or a panel; `sm` is 16px, for a line of text
 * or a button. It never shrinks in a row.
 *
 * It is decoration (`aria-hidden`): the element around it says what is going
 * on (`aria-busy`, or a status with words).
 */
withDefaults(
  defineProps<{
    /** `md` 24px (default), `sm` 16px beside text. */
    size?: 'sm' | 'md'
  }>(),
  { size: 'md' },
)
</script>

<template>
  <span class="t-spinner" :class="{ 't-spinner--sm': size === 'sm' }" aria-hidden="true" />
</template>

<style scoped>
.t-spinner {
  display: inline-block;
  flex: none;
  box-sizing: border-box;
  width: 24px;
  height: 24px;
  border: 2px solid var(--tnzi-ai-border);
  border-top-color: var(--tnzi-ai-accent-text);
  border-radius: 50%;
  animation: t-spinner-turn 0.8s linear infinite;
}
.t-spinner--sm {
  width: 16px;
  height: 16px;
}
@keyframes t-spinner-turn {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-spinner {
    animation-duration: 2.4s;
  }
}
</style>
