<script setup lang="ts">
/**
 * @experimental
 * TThreadIcon - the small icon of a conversation's origin at the end of a row in
 * the thread lists (see `thread-icon.ts`). Its label is the tooltip, and the
 * accessible name unless the icon is `decorative`.
 *
 * It takes no space of its own: each list decides its size and where it sits,
 * and may set `--t-thread-icon-box` for the box it takes.
 */
import { Icon } from '@iconify/vue'
import type { ThreadIcon } from './thread-icon'

defineProps<{
  spec: ThreadIcon
  /**
   * Hidden from assistive tech, the tooltip kept: for a list that already reads
   * the label as part of the row's own name, so it is not announced twice.
   */
  decorative?: boolean
}>()
</script>

<template>
  <!-- Without a label the icon is decoration: hidden from assistive tech rather
       than announced as an unnamed image. -->
  <span
    class="t-thread-icon"
    :role="spec.label && !decorative ? 'img' : undefined"
    :aria-label="(!decorative && spec.label) || undefined"
    :aria-hidden="spec.label && !decorative ? undefined : 'true'"
    :title="spec.label || undefined"
  >
    <Icon :icon="spec.icon" aria-hidden="true" />
  </span>
</template>

<style scoped>
/* Restrained by design: smaller than the title's text, in the secondary text
   colour, so the title stays what the eye reads first. */
.t-thread-icon {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--t-thread-icon-box, 14px);
  height: 14px;
  font-size: 14px;
  line-height: 1;
  color: var(--tnzi-ai-text-secondary);
}
</style>
