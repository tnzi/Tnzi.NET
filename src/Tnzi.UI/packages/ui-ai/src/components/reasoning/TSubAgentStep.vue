<script setup lang="ts">
/**
 * @experimental
 * TSubAgentStep - a sub-agent the agent dispatched, as one row among a run's steps: its state, what it was sent to
 * do, its type, how many tool calls it made and how long it ran (the clock keeps counting while it runs).
 *
 * The row opens the sub-agent's own view: in the panel the product placed (`TSubAgentPanel`, via the provided
 * `SubAgentView`), or, where there is none, in place under the row.
 */
import { computed, defineAsyncComponent, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import type { ToolCallInfo } from '../../headless/useChat'
import { formatTaskDuration, taskElapsedMs, taskStatus, taskTitle, taskToolCount } from '../../headless/cli-tasks'
import { useNow } from '../../headless/useNow'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import { useMessageToolCalls, useSubAgentView } from './sub-agent-view'

/* The panel is only needed when the step opens in place. */
const TSubAgentPanel = defineAsyncComponent(() => import('./TSubAgentPanel.vue'))

const props = withDefaults(
  defineProps<{
    /** The call that dispatched the sub-agent. */
    toolCall: ToolCallInfo
    /** The message's tool calls (the sub-agent's own are among them); defaults to the message being drawn. */
    toolCalls?: readonly ToolCallInfo[]
    /** The sub-agent whose view this row is in (a nested sub-agent): Back in the panel returns there. */
    from?: string | null
    /** Given to every step renderer (`ToolRendererProps`); the row reads the task's own state instead. */
    streaming?: boolean
  }>(),
  { toolCalls: undefined, from: null, streaming: false },
)

const t = useAiI18n()
const view = useSubAgentView()
const messageCalls = useMessageToolCalls()
const calls = computed(() => props.toolCalls ?? messageCalls.value)

const status = computed(() => taskStatus(props.toolCall.task))
const running = computed(() => status.value === 'running')
const title = computed(() => taskTitle(props.toolCall) || t.value.tasks.agent)
const agentType = computed(() => {
  const type = props.toolCall.task?.agentType?.trim()
  return type && type !== title.value ? type : ''
})
const toolCount = computed(() => taskToolCount(props.toolCall, calls.value))
const toolLabel = computed(() =>
  toolCount.value === 1 ? t.value.tasks.oneTool : formatAiMessage(t.value.tasks.tools, { count: toolCount.value }),
)
const now = useNow(() => running.value)
const duration = computed(() => formatTaskDuration(taskElapsedMs(props.toolCall.task, now.value)))

const inline = ref(false)
const opensPanel = computed(() => !!view?.panel.value)
const isCurrent = computed(() => opensPanel.value && view?.current.value === props.toolCall.id)
const expanded = computed(() => (opensPanel.value ? isCurrent.value : inline.value))

function open(): void {
  if (opensPanel.value && view && props.toolCall.id) {
    view.open(props.toolCall.id, { from: props.from })
    return
  }
  inline.value = !inline.value
}

const statusIcon: Record<string, string> = {
  completed: 'lucide:check',
  failed: 'lucide:x',
  stopped: 'lucide:square',
}
</script>

<template>
  <div class="t-sub-agent-step" :class="[`is-${status}`, { 'is-current': isCurrent }]">
    <button
      type="button"
      class="t-sub-agent-step__row"
      :aria-expanded="opensPanel ? undefined : expanded"
      :aria-pressed="opensPanel ? isCurrent : undefined"
      :aria-label="formatAiMessage(t.tasks.open, { title })"
      data-sub-agent-step
      @click="open"
    >
      <span class="t-sub-agent-step__mark" aria-hidden="true">
        <TStreamLoader v-if="running" :size="14" />
        <Icon v-else :icon="statusIcon[status] ?? ''" />
      </span>
      <Icon icon="lucide:bot" class="t-sub-agent-step__icon" aria-hidden="true" />
      <span class="t-sub-agent-step__title">{{ title }}</span>
      <span v-if="agentType" class="t-sub-agent-step__type">{{ agentType }}</span>
      <span class="t-sub-agent-step__meta">
        <span>{{ toolLabel }}</span>
        <template v-if="duration">
          <span aria-hidden="true">·</span>
          <span class="t-sub-agent-step__duration">{{ duration }}</span>
        </template>
      </span>
      <Icon icon="lucide:chevron-right" class="t-sub-agent-step__chevron" aria-hidden="true" />
    </button>
    <div v-if="!opensPanel && inline" class="t-sub-agent-step__inline">
      <TSubAgentPanel :tool-call="toolCall" :tool-calls="calls" embedded />
    </div>
  </div>
</template>

<style scoped>
.t-sub-agent-step {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-sub-agent-step__row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-width: 0;
  min-height: 36px;
  margin: 0;
  padding: 6px 12px;
  border: 0;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 14px;
  line-height: 20px;
  text-align: left;
  cursor: pointer;
  transition: background-color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-sub-agent-step__row:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }
.t-sub-agent-step__row:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
.t-sub-agent-step.is-current .t-sub-agent-step__row { background: var(--tnzi-ai-selected); color: var(--tnzi-ai-text); }
.t-sub-agent-step__mark {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-sub-agent-step.is-completed .t-sub-agent-step__mark { color: var(--tnzi-ai-success); }
.t-sub-agent-step.is-failed .t-sub-agent-step__mark { color: var(--tnzi-ai-danger); }
.t-sub-agent-step__icon {
  flex-shrink: 0;
  font-size: 15px;
  color: var(--tnzi-ai-accent-text);
}
.t-sub-agent-step__title {
  flex: 0 1 auto;
  min-width: 3em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text);
}
.t-sub-agent-step__type {
  flex: 0 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  padding: 0 6px;
  border-radius: 6px;
  background: var(--tnzi-ai-tool-call-bg);
  color: var(--tnzi-ai-text-secondary);
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
  line-height: 18px;
}
.t-sub-agent-step__meta {
  flex-shrink: 0;
  display: inline-flex;
  gap: 4px;
  margin-left: auto;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 13px;
  white-space: nowrap;
}
.t-sub-agent-step__duration { font-variant-numeric: tabular-nums; }
.t-sub-agent-step__chevron {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-sub-agent-step__row[aria-expanded='true'] .t-sub-agent-step__chevron { transform: rotate(90deg); }
.t-sub-agent-step__inline {
  min-width: 0;
  padding: 4px 12px 12px 36px;
}
/* A phone: the type gives way first, the time and count stay. */
@media (max-width: 480px) {
  .t-sub-agent-step__type { display: none; }
}
</style>
