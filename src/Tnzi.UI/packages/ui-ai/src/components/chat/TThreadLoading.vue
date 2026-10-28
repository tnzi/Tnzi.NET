<script setup lang="ts">
/**
 * @experimental
 * TThreadLoading - the main area of a thread whose transcript is on its way.
 *
 * A conversation picked in the sidebar only switches when its transcript
 * arrives (see `useChatThreads().selectThread`). Until then the shell shows this
 * in place of the previous transcript, so the click visibly did something and
 * the screen no longer shows a conversation the composer would not send to.
 *
 * The placeholder takes the shape of a turn (a user bubble on the right, answer
 * lines on the left) and fades in after a short delay: a load that answers
 * within a frame or two goes straight to the transcript without a skeleton
 * flashing in between.
 */
import { computed } from 'vue'
import { useAiI18n } from '../../i18n/index'

const props = defineProps<{
  /** Read out to assistive technology. Defaults to the current locale's. */
  label?: string
}>()

const t = useAiI18n()
const labelText = computed(() => props.label ?? t.value.chat.openingThread)
</script>

<template>
  <div class="t-thread-loading" role="status" :aria-label="labelText">
    <div class="t-thread-loading__bubble" />
    <div class="t-thread-loading__line" style="width: 92%" />
    <div class="t-thread-loading__line" style="width: 84%" />
    <div class="t-thread-loading__line" style="width: 58%" />
  </div>
</template>

<style scoped>
.t-thread-loading {
  display: flex;
  flex-direction: column;
  gap: 12px;
  animation: t-thread-loading-in var(--tnzi-ai-duration-base, 200ms) var(--tnzi-ai-easing, ease) 150ms both;
}
.t-thread-loading__bubble {
  align-self: flex-end;
  width: min(45%, 320px);
  height: 44px;
  margin-bottom: 12px;
  border-radius: 18px;
}
.t-thread-loading__line {
  height: 14px;
  border-radius: 7px;
}
.t-thread-loading__bubble,
.t-thread-loading__line {
  /* Mixed from the text colour so the placeholder keeps its contrast in both
     modes (the hover/press tokens are too faint to read as content). */
  background: linear-gradient(
    90deg,
    color-mix(in srgb, var(--tnzi-ai-text) 6%, transparent) 0%,
    color-mix(in srgb, var(--tnzi-ai-text) 11%, transparent) 50%,
    color-mix(in srgb, var(--tnzi-ai-text) 6%, transparent) 100%
  );
  background-size: 200% 100%;
  animation: t-thread-loading-sweep 1.4s linear infinite;
}
@keyframes t-thread-loading-in {
  from { opacity: 0; }
  to { opacity: 1; }
}
@keyframes t-thread-loading-sweep {
  from { background-position: 200% 0; }
  to { background-position: -200% 0; }
}
@media (prefers-reduced-motion: reduce) {
  .t-thread-loading__bubble,
  .t-thread-loading__line { animation: none; }
}
</style>
