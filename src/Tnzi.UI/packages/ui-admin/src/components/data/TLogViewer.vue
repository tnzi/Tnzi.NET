<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'

export interface LogEntry {
  level: 'error' | 'warn' | 'info' | 'debug'
  message: string
  timestamp: string
}

interface Props {
  entries: LogEntry[]
}

const props = defineProps<Props>()

const buffer = ref<LogEntry[]>([...props.entries])
const level = ref<'all' | LogEntry['level']>('all')
const filter = ref('')
const autoScroll = ref(true)
const listRef = ref<HTMLUListElement | null>(null)

watch(
  () => props.entries,
  (next) => {
    buffer.value = [...next]
  },
  { deep: false },
)

const visibleEntries = computed<LogEntry[]>(() => {
  const q = filter.value.trim().toLowerCase()
  return buffer.value.filter((entry) => {
    if (level.value !== 'all' && entry.level !== level.value) return false
    if (q && !entry.message.toLowerCase().includes(q)) return false
    return true
  })
})

watch(visibleEntries, async () => {
  if (!autoScroll.value) return
  await nextTick()
  const el = listRef.value
  if (el) el.scrollTop = el.scrollHeight
})

function append(entry: LogEntry): void {
  buffer.value = [...buffer.value, entry]
}

function clear(): void {
  buffer.value = []
}

function setLevel(value: 'all' | LogEntry['level']): void {
  level.value = value
}

function setFilter(value: string): void {
  filter.value = value
}

defineExpose({ append, clear, setLevel, setFilter })
</script>

<template>
  <div class="t-log-viewer">
    <div class="t-log-viewer__toolbar">
      <select v-model="level" class="t-log-viewer__level" aria-label="Log level filter">
        <option value="all">All levels</option>
        <option value="error">Error</option>
        <option value="warn">Warn</option>
        <option value="info">Info</option>
        <option value="debug">Debug</option>
      </select>
      <input
        v-model="filter"
        class="t-log-viewer__filter"
        type="text"
        placeholder="Filter text..."
        aria-label="Log text filter"
      />
      <label class="t-log-viewer__auto-scroll">
        <input type="checkbox" v-model="autoScroll" />
        Auto-scroll
      </label>
      <button type="button" class="t-log-viewer__clear" @click="clear">Clear</button>
    </div>
    <ul ref="listRef" class="t-log-viewer__list">
      <li
        v-for="(entry, idx) in visibleEntries"
        :key="idx"
        class="t-log-viewer__entry"
        :class="`t-log-viewer__entry--${entry.level}`"
      >
        <span class="t-log-viewer__ts">{{ entry.timestamp }}</span>
        <span class="t-log-viewer__level-tag">{{ entry.level.toUpperCase() }}</span>
        <span class="t-log-viewer__msg">{{ entry.message }}</span>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.t-log-viewer {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 240px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  background-color: var(--tnzi-log-bg, #1e1e1e);
  color: var(--tnzi-log-text, #e0e0e0);
  border-radius: 4px;
  overflow: hidden;
}

.t-log-viewer__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px;
  background-color: var(--tnzi-log-toolbar-bg, #2a2a2a);
  border-bottom: 1px solid var(--tnzi-log-border, #3a3a3a);
}

.t-log-viewer__filter {
  flex: 1;
  min-width: 0;
}

.t-log-viewer__list {
  flex: 1;
  margin: 0;
  padding: 8px;
  list-style: none;
  overflow-y: auto;
  font-size: 12px;
  line-height: 1.5;
}

.t-log-viewer__entry {
  display: flex;
  gap: 8px;
  padding: 2px 0;
  white-space: pre-wrap;
  word-break: break-word;
}

.t-log-viewer__ts {
  color: var(--tnzi-log-debug, #64748b);
  flex-shrink: 0;
}

.t-log-viewer__level-tag {
  flex-shrink: 0;
  font-weight: bold;
  min-width: 48px;
}

.t-log-viewer__msg {
  flex: 1;
  min-width: 0;
}

.t-log-viewer__entry--error {
  color: var(--tnzi-log-error, #ff6b6b);
}

.t-log-viewer__entry--warn {
  color: var(--tnzi-log-warn, #ffd93d);
}

.t-log-viewer__entry--info {
  color: var(--tnzi-log-info, #a3e635);
}

.t-log-viewer__entry--debug {
  color: var(--tnzi-log-debug, #64748b);
}
</style>
