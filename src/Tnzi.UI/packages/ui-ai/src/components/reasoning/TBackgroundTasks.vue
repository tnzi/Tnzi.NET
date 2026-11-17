<script setup lang="ts">
/**
 * @experimental
 * TBackgroundTasks - the conversation's sub-agents and background commands, as a group for a side panel.
 *
 * Each running task is a row: an icon for its kind, its name (a sub-agent's task description, a command's
 * description or command line), how long it has been running ("18s", "6m", counting) and a stop square when the
 * conversation can stop a single task. The finished ones fold into one row ("15 finished") that opens to list them
 * with their outcome and time. Clicking a sub-agent opens its own view (`TSubAgentPanel`, through the provided
 * `SubAgentView`); every click also emits `select`.
 *
 * It reads the messages the conversation shows (`useChatThreads().messages`), so it follows the stream. How to stop
 * a task comes from the view (`TChatApp` wires `useChatThreads().stopTask`), or from `stopTask`.
 */
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import type { ChatMessage } from '../../headless/useChat'
import { collectTasks, formatTaskDuration, taskElapsedMs, taskTitle, type TaskEntry } from '../../headless/cli-tasks'
import { useNow } from '../../headless/useNow'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import { useSubAgentView, type StopTaskHandler } from './sub-agent-view'

const props = withDefaults(
  defineProps<{
    /** The conversation's messages. */
    messages: readonly ChatMessage[]
    /** Heading; defaults to the dictionary's. An empty string for none. */
    title?: string
    /** How to stop a task, instead of the view's. */
    stopTask?: StopTaskHandler | null
    /** Show the finished ones opened at first. */
    finishedOpen?: boolean
  }>(),
  { title: undefined, stopTask: null, finishedOpen: false },
)

const emit = defineEmits<{
  /** A row was clicked (after a sub-agent's view was opened). */
  select: [entry: TaskEntry]
}>()

const t = useAiI18n()
const view = useSubAgentView()

const entries = computed(() => collectTasks(props.messages))
const running = computed(() => entries.value.filter((e) => e.status === 'running'))
const finished = computed(() => entries.value.filter((e) => e.status !== 'running'))
const heading = computed(() => props.title ?? t.value.tasks.listTitle)

/* A nested sub-agent is set in under the one that dispatched it only when that one is in the same group. */
const depthIn = (entry: TaskEntry, group: readonly TaskEntry[]): number =>
  entry.depth > 0 && group.some((other) => other.id === entry.toolCall.parentId) ? entry.depth : 0
const showFinished = ref(props.finishedOpen)

const now = useNow(() => running.value.length > 0)
const durationOf = (entry: TaskEntry): string => formatTaskDuration(taskElapsedMs(entry.detail, now.value))
const titleOf = (entry: TaskEntry): string =>
  taskTitle(entry.toolCall) || (entry.kind === 'agent' ? t.value.tasks.agent : t.value.tasks.command)

/* Stopping: the list's own handler wins over the view's. */
const localStopping = ref<ReadonlySet<string>>(new Set())
const canStop = (entry: TaskEntry): boolean => !!entry.detail.taskId && !!(props.stopTask ?? view?.stopTask.value)
const isStopping = (entry: TaskEntry): boolean =>
  !!entry.detail.taskId && (localStopping.value.has(entry.detail.taskId) || !!view?.stopping.value.has(entry.detail.taskId))

async function stop(entry: TaskEntry): Promise<void> {
  const taskId = entry.detail.taskId
  if (!taskId) return
  if (!props.stopTask) {
    await view?.stop(taskId)
    return
  }
  localStopping.value = new Set([...localStopping.value, taskId])
  let accepted = false
  try {
    accepted = (await props.stopTask(taskId)) !== false
  } catch {
    accepted = false
  }
  if (!accepted) localStopping.value = new Set([...localStopping.value].filter((id) => id !== taskId))
}

/* A task that ended is no longer stopping. */
watch(finished, (done) => {
  for (const entry of done) {
    const taskId = entry.detail.taskId
    if (!taskId) continue
    view?.settled(taskId)
    if (localStopping.value.has(taskId)) localStopping.value = new Set([...localStopping.value].filter((id) => id !== taskId))
  }
})

function select(entry: TaskEntry): void {
  if (entry.kind === 'agent' && view) view.open(entry.id)
  emit('select', entry)
}

const statusIcon: Record<string, string> = {
  completed: 'lucide:check',
  failed: 'lucide:x',
  stopped: 'lucide:square',
}
const kindIcon = (entry: TaskEntry): string => (entry.kind === 'agent' ? 'lucide:bot' : 'lucide:terminal')
</script>

<template>
  <section class="t-background-tasks" :aria-label="heading || t.tasks.listTitle">
    <h3 v-if="heading" class="t-background-tasks__title">{{ heading }}</h3>
    <p v-if="!entries.length" class="t-background-tasks__empty">{{ t.tasks.listEmpty }}</p>

    <ul v-if="running.length" class="t-background-tasks__rows">
      <li v-for="entry in running" :key="entry.id" class="t-background-tasks__item">
        <button
          type="button"
          class="t-background-tasks__row is-running"
          :class="{ 'is-current': view?.current.value === entry.id }"
          :style="{ '--t-task-depth': depthIn(entry, running) }"
          :title="titleOf(entry)"
          @click="select(entry)"
        >
          <span class="t-background-tasks__icon" aria-hidden="true">
            <Icon :icon="kindIcon(entry)" />
          </span>
          <span class="t-background-tasks__name">{{ titleOf(entry) }}</span>
          <span class="t-background-tasks__time">{{ durationOf(entry) }}</span>
        </button>
        <button
          v-if="canStop(entry)"
          type="button"
          class="t-background-tasks__stop"
          :disabled="isStopping(entry)"
          :aria-label="formatAiMessage(t.tasks.stopNamed, { title: titleOf(entry) })"
          :title="isStopping(entry) ? t.tasks.stopping : t.tasks.stop"
          @click="stop(entry)"
        >
          <TStreamLoader v-if="isStopping(entry)" :size="12" />
          <Icon v-else icon="lucide:square" aria-hidden="true" />
        </button>
      </li>
    </ul>

    <template v-if="finished.length">
      <button
        type="button"
        class="t-background-tasks__toggle"
        :aria-expanded="showFinished"
        @click="showFinished = !showFinished"
      >
        <Icon icon="lucide:chevron-right" class="t-background-tasks__chevron" aria-hidden="true" />
        <span>{{ formatAiMessage(t.tasks.finished, { count: finished.length }) }}</span>
      </button>
      <ul v-show="showFinished" class="t-background-tasks__rows">
        <li v-for="entry in finished" :key="entry.id" class="t-background-tasks__item">
          <button
            type="button"
            class="t-background-tasks__row"
            :class="[`is-${entry.status}`, { 'is-current': view?.current.value === entry.id }]"
            :style="{ '--t-task-depth': depthIn(entry, finished) }"
            :title="titleOf(entry)"
            @click="select(entry)"
          >
            <span class="t-background-tasks__icon t-background-tasks__outcome" :aria-label="t.tasks[entry.status]" role="img">
              <Icon :icon="statusIcon[entry.status] ?? ''" />
            </span>
            <span class="t-background-tasks__name">{{ titleOf(entry) }}</span>
            <span class="t-background-tasks__time">{{ durationOf(entry) }}</span>
          </button>
        </li>
      </ul>
    </template>
  </section>
</template>

<style scoped>
.t-background-tasks {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  color: var(--tnzi-ai-text);
  font-size: 14px;
  line-height: 20px;
}
.t-background-tasks__title {
  margin: 0;
  padding: 0 8px 4px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
  font-weight: 600;
  line-height: 20px;
}
.t-background-tasks__empty {
  margin: 0;
  padding: 0 8px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 13px;
}
.t-background-tasks__rows {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.t-background-tasks__item {
  display: flex;
  align-items: center;
  gap: 2px;
  min-width: 0;
}
.t-background-tasks__row {
  display: flex;
  flex: 1 1 auto;
  align-items: center;
  gap: 8px;
  min-width: 0;
  min-height: var(--tnzi-ai-row-height, 36px);
  margin: 0;
  padding: 6px 8px 6px calc(8px + var(--t-task-depth, 0) * 16px);
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: background-color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-background-tasks__row:hover { background: var(--tnzi-ai-hover); }
.t-background-tasks__row.is-current { background: var(--tnzi-ai-selected); }
.t-background-tasks__row:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: -2px; }
.t-background-tasks__icon {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  font-size: 15px;
  color: var(--tnzi-ai-text-secondary);
}
.t-background-tasks__row.is-running .t-background-tasks__icon { color: var(--tnzi-ai-accent-text); }
.t-background-tasks__row.is-completed .t-background-tasks__outcome { color: var(--tnzi-ai-success); }
.t-background-tasks__row.is-failed .t-background-tasks__outcome { color: var(--tnzi-ai-danger); }
.t-background-tasks__row.is-stopped .t-background-tasks__outcome { color: var(--tnzi-ai-text-tertiary); }
.t-background-tasks__name {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-background-tasks__time {
  flex-shrink: 0;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
.t-background-tasks__stop {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--tnzi-ai-row-action-size, 28px);
  height: var(--tnzi-ai-row-action-size, 28px);
  margin: 0;
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
  cursor: pointer;
}
.t-background-tasks__stop:hover:not(:disabled) { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-danger); }
.t-background-tasks__stop:disabled { cursor: default; }
.t-background-tasks__stop:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: -2px; }
.t-background-tasks__toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 32px;
  margin: 0;
  padding: 4px 8px;
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}
.t-background-tasks__toggle:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }
.t-background-tasks__toggle:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: -2px; }
.t-background-tasks__chevron {
  font-size: 14px;
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-background-tasks__toggle[aria-expanded='true'] .t-background-tasks__chevron { transform: rotate(90deg); }
</style>
