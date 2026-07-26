<script setup lang="ts">
/**
 * @experimental
 * TReasoningStage - Manus-style collapsible reasoning/stage block.
 *
 * Renders a card with a leading status circle (spinning loader when
 * running, filled check when done) + title row + collapsible body. Use
 * to show chain-of-thought, tool call traces, or any multi-step reasoning
 * alongside an assistant message.
 *
 * The body is rendered via the default slot so consumers can embed prose,
 * inline tool chips, citations, or nested stages.
 */
import { Icon } from '@iconify/vue'

export type ReasoningStageStatus = 'running' | 'done' | 'error'

withDefaults(
  defineProps<{
    /** Current status - drives the status-circle icon + animation. */
    status?: ReasoningStageStatus
    /** Title shown on the head row. Falls back to a status-derived label. */
    title?: string
    /** Whether the stage starts expanded. Defaults to `true`. */
    open?: boolean
  }>(),
  {
    status: 'done',
    title: '',
    open: true,
  },
)
</script>

<template>
  <details class="t-reasoning-stage" :open="open">
    <summary class="t-reasoning-stage__head">
      <span
        class="t-reasoning-stage__status"
        :class="[
          status === 'running' && 't-reasoning-stage__status--running',
          status === 'done' && 't-reasoning-stage__status--done',
          status === 'error' && 't-reasoning-stage__status--error',
        ]"
        aria-hidden="true"
      >
        <Icon v-if="status === 'running'" icon="lucide:loader-circle" />
        <Icon v-else-if="status === 'error'" icon="lucide:x" />
        <Icon v-else icon="lucide:check" />
      </span>
      <span class="t-reasoning-stage__title">
        {{ title || (status === 'running' ? 'Thinking' : 'Thought process') }}
      </span>
      <Icon icon="lucide:chevron-down" class="t-reasoning-stage__caret" />
    </summary>
    <div class="t-reasoning-stage__body">
      <slot />
    </div>
  </details>
</template>

<style scoped>
.t-reasoning-stage {
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 12px;
  padding: 10px 12px;
  background: var(--tnzi-ai-surface, #ffffff);
}
.t-reasoning-stage__head {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  list-style: none;
  user-select: none;
}
.t-reasoning-stage__head::-webkit-details-marker { display: none; }
.t-reasoning-stage__status {
  width: 18px;
  height: 18px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  font-size: 14px;
  flex-shrink: 0;
}
.t-reasoning-stage__status--running {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-reasoning-stage__status--running > .iconify {
  animation: t-reasoning-stage-spin 1.2s linear infinite;
}
.t-reasoning-stage__status--done {
  background: var(--tnzi-ai-text, #1a1a1a);
  color: var(--tnzi-ai-bg, #ffffff);
  font-size: 11px;
}
.t-reasoning-stage__status--error {
  background: var(--tnzi-ai-danger);
  color: var(--tnzi-ai-on-accent);
  font-size: 11px;
}
.t-reasoning-stage__title {
  flex: 1;
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-reasoning-stage__caret {
  font-size: 16px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  transition: transform 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-reasoning-stage:not([open]) .t-reasoning-stage__caret {
  transform: rotate(-90deg);
}
.t-reasoning-stage__body {
  margin-top: 10px;
  padding: 2px 0 2px 28px;
  margin-left: 8px;
  font-size: 13px;
  line-height: 1.6;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  border-left: 2px solid var(--tnzi-ai-border, #e5e5e5);
}
@keyframes t-reasoning-stage-spin {
  to { transform: rotate(360deg); }
}
</style>
