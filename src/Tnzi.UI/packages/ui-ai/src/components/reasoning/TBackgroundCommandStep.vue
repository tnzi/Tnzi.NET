<script setup lang="ts">
/**
 * @experimental
 * TBackgroundCommandStep - a command the agent started in the background, as one row among a run's steps: its
 * state (the command's own, not that of the call that started it, which returns at once), what it does, and how long
 * it has run (counting while it runs). Opened, the command line and the CLI's note on how it ended.
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n } from '../../i18n/index'
import type { ToolCallInfo } from '../../headless/useChat'
import { formatTaskDuration, taskElapsedMs, taskStatus, taskTitle } from '../../headless/cli-tasks'
import { useNow } from '../../headless/useNow'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import { inputString } from './web-tools'

const props = withDefaults(
  defineProps<{
    toolCall: ToolCallInfo
    /** Given to every step renderer (`ToolRendererProps`); the row reads the task's own state instead. */
    streaming?: boolean
  }>(),
  { streaming: false },
)

const t = useAiI18n()
const status = computed(() => taskStatus(props.toolCall.task))
const running = computed(() => status.value === 'running')
const title = computed(() => props.toolCall.task?.description?.trim() || t.value.tasks.command)
const command = computed(() => inputString(props.toolCall.input, 'command'))
const note = computed(() => props.toolCall.task?.result?.trim() ?? '')
const now = useNow(() => running.value)
const duration = computed(() => formatTaskDuration(taskElapsedMs(props.toolCall.task, now.value)))
const subject = computed(() => (command.value && command.value !== taskTitle(props.toolCall) ? command.value : ''))
const open = ref(false)

const statusIcon: Record<string, string> = {
  completed: 'lucide:check',
  failed: 'lucide:x',
  stopped: 'lucide:square',
}
</script>

<template>
  <div class="t-background-command" :class="`is-${status}`">
    <button type="button" class="t-background-command__row" :aria-expanded="open" @click="open = !open">
      <span class="t-background-command__mark" role="img" :aria-label="t.tasks[status]">
        <TStreamLoader v-if="running" :size="14" />
        <Icon v-else :icon="statusIcon[status] ?? ''" />
      </span>
      <Icon icon="lucide:terminal" class="t-background-command__icon" aria-hidden="true" />
      <span class="t-background-command__title">{{ title }}</span>
      <span v-if="subject" class="t-background-command__subject">{{ subject }}</span>
      <span v-if="duration" class="t-background-command__duration">{{ duration }}</span>
      <Icon icon="lucide:chevron-right" class="t-background-command__chevron" aria-hidden="true" />
    </button>
    <div v-if="open" class="t-background-command__body">
      <pre v-if="command" class="t-background-command__command t-ai-scroll">{{ command }}</pre>
      <p v-if="note" class="t-background-command__note">{{ note }}</p>
    </div>
  </div>
</template>

<style scoped>
.t-background-command {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-background-command__row {
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
}
.t-background-command__row:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }
.t-background-command__row:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: -2px; }
.t-background-command__mark {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-background-command.is-completed .t-background-command__mark { color: var(--tnzi-ai-success); }
.t-background-command.is-failed .t-background-command__mark { color: var(--tnzi-ai-danger); }
.t-background-command__icon { flex-shrink: 0; font-size: 15px; color: var(--tnzi-ai-text-secondary); }
.t-background-command__title {
  flex: 0 1 auto;
  min-width: 3em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text);
}
.t-background-command__subject {
  flex: 1 1 0;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text-tertiary);
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
}
.t-background-command__duration {
  flex-shrink: 0;
  margin-left: auto;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
.t-background-command__chevron {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-background-command__row[aria-expanded='true'] .t-background-command__chevron { transform: rotate(90deg); }
.t-background-command__body {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  padding: 0 12px 10px 36px;
}
.t-background-command__command {
  margin: 0;
  padding: 6px 8px;
  max-height: 120px;
  overflow: auto;
  position: relative;
  border-radius: 6px;
  background: var(--tnzi-ai-code-bg);
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.t-background-command__note {
  margin: 0;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
}
</style>
