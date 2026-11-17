<script setup lang="ts">
/**
 * @experimental
 * TThreadStatus - a conversation's state at the end of a row in the thread lists
 * (`TThreadList`, `TRailThreadList`, `TRecentsPage`): a raised hand in the accent
 * colour while a turn of it waits for the user (`ThreadItem.awaitingOwner`: a
 * permission request, a question, a confirmation card), otherwise a small turning
 * ring while a turn is going on (`ThreadItem.running`), otherwise an accent dot when
 * it has a reply the user has not seen (`ThreadItem.unread`). Awaiting wins over
 * running (the turn is stopped on the user), running over unread (the dot is for
 * something finished).
 *
 * Decorative: each list reads the state as words in the row's own name, so it is
 * not announced twice. Under `prefers-reduced-motion` the ring stands still.
 * Like `TThreadIcon` it takes the cell the list gives it.
 */
import { Icon } from '@iconify/vue'

defineProps<{
  awaiting?: boolean
  running?: boolean
  unread?: boolean
}>()
</script>

<template>
  <Icon
    v-if="awaiting"
    icon="lucide:hand"
    class="t-thread-status t-thread-status--awaiting"
    data-thread-status="awaiting"
    aria-hidden="true"
  />
  <span
    v-else-if="running"
    class="t-thread-status t-thread-status--running"
    data-thread-status="running"
    aria-hidden="true"
  />
  <span
    v-else-if="unread"
    class="t-thread-status t-thread-status--unread"
    data-thread-status="unread"
    aria-hidden="true"
  />
</template>

<style scoped>
.t-thread-status {
  flex-shrink: 0;
  display: block;
  box-sizing: border-box;
  pointer-events: none;
}
/* Quiet: thin, in the secondary text colour with a lighter track, so a column of
   working conversations does not pull the eye from the titles. */
.t-thread-status--running {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  border: 1.5px solid color-mix(in srgb, var(--tnzi-ai-text-secondary) 28%, transparent);
  border-top-color: var(--tnzi-ai-text-secondary);
  animation: t-thread-status-spin 0.9s linear infinite;
}
@keyframes t-thread-status-spin { to { transform: rotate(360deg); } }
/* The accent: the turn is stopped until the user answers, the strongest call in the row. */
.t-thread-status--awaiting {
  width: 14px;
  height: 14px;
  color: var(--tnzi-ai-accent-text);
}
/* The accent, filled: the one thing in the row that asks to be looked at. */
.t-thread-status--unread {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--tnzi-ai-accent);
}
@media (prefers-reduced-motion: reduce) {
  .t-thread-status--running { animation: none; }
}
</style>
