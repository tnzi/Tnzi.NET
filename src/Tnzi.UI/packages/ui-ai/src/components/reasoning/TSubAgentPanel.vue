<script setup lang="ts">
/**
 * @experimental
 * TSubAgentPanel - one sub-agent's own conversation, beside the main one: what it was sent to do, the full prompt it
 * received (folded to a few lines, "Show more"), what it is doing now, the model it answers with, and then its work
 * in order: the text it wrote, its tool calls as runs (with the images they returned), a sub-agent it dispatched in
 * turn as a row that opens that one's view, and the report it handed back.
 *
 * Which sub-agent: the one the provided `SubAgentView` has open, looked up in `messages`; or the one given as
 * `toolCall` (with the message's `toolCalls`). Where to put it is the product's choice (`TChatApp`'s side panel via
 * `subAgentAside`, a drawer, a page); `embedded` drops the header for a view inside a run.
 *
 * A running sub-agent has a Stop button when the conversation can stop a single task (`useChatThreads().stopTask`,
 * through the view). A sub-agent another sub-agent dispatched shows no steps: the CLI does not stream them.
 */
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import type { ChatMessage, ToolCallInfo } from '../../headless/useChat'
import {
  findToolCall,
  formatTaskDuration,
  subAgentSteps,
  taskElapsedMs,
  taskStatus,
  taskTitle,
  taskToolCount,
} from '../../headless/cli-tasks'
import { useNow } from '../../headless/useNow'
import TMessageResponse from '../chat/TMessageResponse.vue'
import TShimmer from '../streaming/TShimmer.vue'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import TSubAgentStep from './TSubAgentStep.vue'
import TToolRun from './TToolRun.vue'
import { toolCallState } from './tool-call-status'
import { isTaskStep, taskStepState } from './task-steps'
import { provideMessageToolCalls, useSubAgentView } from './sub-agent-view'

const props = withDefaults(
  defineProps<{
    /** The conversation's messages, where the open sub-agent is looked up. */
    messages?: readonly ChatMessage[]
    /** The sub-agent to show, instead of the one the view has open. */
    toolCall?: ToolCallInfo | null
    /** The tool calls of `toolCall`'s message (its own calls are among them). */
    toolCalls?: readonly ToolCallInfo[]
    /** Inside a run: no header (the row above says what it is). */
    embedded?: boolean
  }>(),
  { messages: () => [], toolCall: null, toolCalls: undefined, embedded: false },
)

const emit = defineEmits<{
  /** The close button was used (the view is closed too). */
  close: []
}>()

const t = useAiI18n()
const view = useSubAgentView()

const found = computed(() => {
  if (props.toolCall) return { toolCall: props.toolCall, calls: props.toolCalls ?? [] }
  const id = view?.current.value
  const hit = id ? findToolCall(props.messages, id) : null
  return hit ? { toolCall: hit.toolCall, calls: hit.message.toolCalls ?? [] } : null
})
const dispatch = computed(() => found.value?.toolCall ?? null)
const calls = computed<readonly ToolCallInfo[]>(() => found.value?.calls ?? [])
provideMessageToolCalls(calls)

const detail = computed(() => dispatch.value?.task ?? null)
const status = computed(() => taskStatus(detail.value))
const running = computed(() => !!dispatch.value && status.value === 'running')
const title = computed(() => (dispatch.value ? taskTitle(dispatch.value) : '') || t.value.tasks.agent)
const agentType = computed(() => {
  const type = detail.value?.agentType?.trim()
  return type && type !== title.value ? type : ''
})
const toolCount = computed(() => (dispatch.value ? taskToolCount(dispatch.value, calls.value) : 0))
const toolLabel = computed(() =>
  toolCount.value === 1 ? t.value.tasks.oneTool : formatAiMessage(t.value.tasks.tools, { count: toolCount.value }),
)
const now = useNow(() => running.value)
const duration = computed(() => formatTaskDuration(taskElapsedMs(detail.value, now.value)))
const steps = computed(() => (dispatch.value ? subAgentSteps(dispatch.value, calls.value) : []))
const nested = computed(() => !!dispatch.value?.parentId)
/* A run of the sub-agent's is going only while one of its steps is: a step that returned shows its own outcome,
   though the sub-agent works on (what it does next is the activity line's). */
const isGoing = (toolCalls: readonly ToolCallInfo[]): boolean =>
  toolCalls.some((call) => (isTaskStep(call) ? taskStepState(call) : toolCallState(call, true)) === 'running')
const result = computed(() => detail.value?.result?.trim() ?? '')
const activity = computed(() => detail.value?.activity?.trim() || t.value.tasks.working)

/* The prompt is folded to a few lines when it is long. */
const PROMPT_FOLD_CHARS = 280
const prompt = computed(() => detail.value?.prompt?.trim() ?? '')
const promptLong = computed(() => prompt.value.length > PROMPT_FOLD_CHARS || prompt.value.split('\n').length > 5)
const promptOpen = ref(false)
watch(() => dispatch.value?.id, () => (promptOpen.value = false))

const taskId = computed(() => detail.value?.taskId ?? null)
const canStop = computed(() => running.value && !!taskId.value && !!view?.stopTask.value)
const stopping = computed(() => !!taskId.value && !!view?.stopping.value.has(taskId.value))
watch(running, (isRunning) => {
  if (!isRunning && taskId.value) view?.settled(taskId.value)
})

function stop(): void {
  if (taskId.value) void view?.stop(taskId.value)
}

function close(): void {
  view?.close()
  emit('close')
}

const statusIcon: Record<string, string> = {
  completed: 'lucide:check',
  failed: 'lucide:x',
  stopped: 'lucide:square',
}
</script>

<template>
  <section
    v-if="dispatch"
    class="t-sub-agent-panel"
    :class="[`is-${status}`, { 'is-embedded': embedded }]"
    :aria-label="embedded ? undefined : title"
  >
    <header v-if="!embedded" class="t-sub-agent-panel__header">
      <button
        v-if="view && view.trail.value.length"
        type="button"
        class="t-sub-agent-panel__icon-btn"
        :aria-label="t.tasks.back"
        @click="view.back()"
      >
        <Icon icon="lucide:arrow-left" aria-hidden="true" />
      </button>
      <h2 class="t-sub-agent-panel__title" :title="title">{{ title }}</h2>
      <button type="button" class="t-sub-agent-panel__icon-btn" :aria-label="t.tasks.close" @click="close">
        <Icon icon="lucide:x" aria-hidden="true" />
      </button>
    </header>

    <div class="t-sub-agent-panel__meta">
      <span class="t-sub-agent-panel__status" role="status">
        <TStreamLoader v-if="running" :size="12" />
        <Icon v-else :icon="statusIcon[status] ?? ''" aria-hidden="true" />
        <span>{{ t.tasks[status] }}</span>
      </span>
      <span v-if="agentType" class="t-sub-agent-panel__chip">{{ agentType }}</span>
      <span v-if="detail?.model" class="t-sub-agent-panel__model" :title="t.tasks.model">{{ detail.model }}</span>
      <span v-if="detail?.background" class="t-sub-agent-panel__tag">{{ t.tasks.background }}</span>
      <span class="t-sub-agent-panel__usage">
        <span>{{ toolLabel }}</span>
        <template v-if="duration">
          <span aria-hidden="true">·</span>
          <span class="t-sub-agent-panel__duration">{{ duration }}</span>
        </template>
      </span>
      <button
        v-if="canStop"
        type="button"
        class="t-sub-agent-panel__stop"
        :disabled="stopping"
        :aria-label="formatAiMessage(t.tasks.stopNamed, { title })"
        @click="stop"
      >
        <Icon icon="lucide:square" aria-hidden="true" />
        <span>{{ stopping ? t.tasks.stopping : t.tasks.stop }}</span>
      </button>
    </div>

    <div v-if="running" class="t-sub-agent-panel__activity">
      <TShimmer :key="activity">{{ activity }}</TShimmer>
    </div>

    <div v-if="prompt" class="t-sub-agent-panel__section">
      <div class="t-sub-agent-panel__heading">{{ t.tasks.prompt }}</div>
      <div class="t-sub-agent-panel__prompt">
        <p class="t-sub-agent-panel__prompt-text" :class="{ 'is-folded': promptLong && !promptOpen }">{{ prompt }}</p>
      </div>
      <button
        v-if="promptLong"
        type="button"
        class="t-sub-agent-panel__more"
        :aria-expanded="promptOpen"
        @click="promptOpen = !promptOpen"
      >
        {{ promptOpen ? t.tasks.showLess : t.tasks.showMore }}
      </button>
    </div>

    <div class="t-sub-agent-panel__steps">
      <p v-if="nested && !steps.length" class="t-sub-agent-panel__note">{{ t.tasks.nestedSteps }}</p>
      <p v-else-if="!steps.length && running" class="t-sub-agent-panel__note">{{ t.tasks.noSteps }}</p>
      <template v-for="(step, i) in steps" :key="i">
        <TMessageResponse v-if="step.kind === 'text'" class="t-sub-agent-panel__text" :content="step.text" />
        <TToolRun
          v-else-if="step.kind === 'tools'"
          :tool-calls="step.toolCalls"
          :streaming="running"
          :active="running && i === steps.length - 1 && isGoing(step.toolCalls)"
        />
        <div v-else class="t-sub-agent-panel__nested">
          <TSubAgentStep :tool-call="step.toolCall" :tool-calls="calls" :from="dispatch.id" />
        </div>
      </template>
    </div>

    <div v-if="result" class="t-sub-agent-panel__section t-sub-agent-panel__result">
      <div class="t-sub-agent-panel__heading">{{ t.tasks.result }}</div>
      <TMessageResponse class="t-sub-agent-panel__text" :content="result" />
    </div>
  </section>
</template>

<style scoped>
.t-sub-agent-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  padding: 12px 16px 24px;
  color: var(--tnzi-ai-text);
  font-size: 14px;
  line-height: 20px;
}
.t-sub-agent-panel.is-embedded {
  gap: 8px;
  padding: 0;
}
.t-sub-agent-panel__header {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-sub-agent-panel__title {
  flex: 1 1 auto;
  min-width: 0;
  margin: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 15px;
  font-weight: 600;
  line-height: 22px;
}
.t-sub-agent-panel__icon-btn {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--tnzi-ai-toolbar-control-size, 32px);
  height: var(--tnzi-ai-toolbar-control-size, 32px);
  margin: 0;
  padding: 0;
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-size: 16px;
  cursor: pointer;
}
.t-sub-agent-panel__icon-btn:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }
.t-sub-agent-panel__icon-btn:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: 2px; }

.t-sub-agent-panel__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 8px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
}
.t-sub-agent-panel__status {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.t-sub-agent-panel.is-completed .t-sub-agent-panel__status { color: var(--tnzi-ai-success); }
.t-sub-agent-panel.is-failed .t-sub-agent-panel__status { color: var(--tnzi-ai-danger); }
.t-sub-agent-panel.is-stopped .t-sub-agent-panel__status { color: var(--tnzi-ai-text-tertiary); }
.t-sub-agent-panel__chip,
.t-sub-agent-panel__model {
  padding: 0 6px;
  border-radius: 6px;
  background: var(--tnzi-ai-tool-call-bg);
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
  line-height: 18px;
}
.t-sub-agent-panel__tag {
  padding: 0 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 6px;
  font-size: 12px;
  line-height: 18px;
}
.t-sub-agent-panel__usage {
  display: inline-flex;
  gap: 4px;
  white-space: nowrap;
}
.t-sub-agent-panel__duration { font-variant-numeric: tabular-nums; }
.t-sub-agent-panel__stop {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin: 0 0 0 auto;
  padding: 2px 10px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}
.t-sub-agent-panel__stop:hover:not(:disabled) { background: var(--tnzi-ai-hover); }
.t-sub-agent-panel__stop:disabled { color: var(--tnzi-ai-text-tertiary); cursor: default; }
.t-sub-agent-panel__stop:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: 2px; }

.t-sub-agent-panel__activity {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text-secondary);
}
.t-sub-agent-panel__section {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  min-width: 0;
}
.t-sub-agent-panel__heading {
  color: var(--tnzi-ai-text-tertiary);
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
}
.t-sub-agent-panel__prompt {
  align-self: stretch;
  padding: 8px 10px;
  border-radius: 8px;
  background: var(--tnzi-ai-chat-user-bg);
  color: var(--tnzi-ai-chat-user-text);
}
/* The fold is on the text itself, inside the padding: a clamped box with padding shows part of the next line. */
.t-sub-agent-panel__prompt-text {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.t-sub-agent-panel__prompt-text.is-folded {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 5;
  line-clamp: 5;
  overflow: hidden;
}
.t-sub-agent-panel__more {
  margin: 0;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--tnzi-ai-accent-text);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}
.t-sub-agent-panel__more:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: 2px; }
.t-sub-agent-panel__steps {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}
.t-sub-agent-panel__note {
  margin: 0;
  color: var(--tnzi-ai-text-tertiary);
}
.t-sub-agent-panel__nested {
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  overflow: hidden;
}
.t-sub-agent-panel__text { min-width: 0; }
.t-sub-agent-panel__result {
  padding-top: 12px;
  border-top: 1px solid var(--tnzi-ai-divider);
}
</style>
