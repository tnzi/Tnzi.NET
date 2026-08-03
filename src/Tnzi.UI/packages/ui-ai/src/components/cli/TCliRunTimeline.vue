<script setup lang="ts">
/**
 * TCliRunTimeline - what an external CLI agent is doing, for the person who asked.
 *
 * The admin run detail already shows every event verbatim, which is the right
 * view for whoever operates the runtime. This one is for a consumer chat
 * surface: it pairs tool calls with their results, concatenates streamed text,
 * and keeps diagnostics out of the way unless asked for.
 *
 * Feed it the persisted history, the live SSE events, or both concatenated -
 * see `groupCliEvents` for why both shapes are accepted.
 */

import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import { useAutoScroll } from '../../headless/useAutoScroll';
import TStreamLoader from '../streaming/TStreamLoader.vue';
import {
  groupCliEvents,
  pendingToolCount,
  type CliTimelineEvent,
  type CliTimelineToolRow,
} from './timeline';

const props = withDefaults(
  defineProps<{
    /** Run events, oldest first. Persisted messages and live events may be mixed. */
    events: readonly CliTimelineEvent[];
    /** Whether the run is still going, which drives the live indicator. */
    running?: boolean;
    /** Show `Log` events. Off by default - they are runtime diagnostics. */
    showLogs?: boolean;
    /** Stick to the newest row unless the reader has scrolled up. */
    autoScroll?: boolean;
    /** Characters of tool output to show before clamping. 0 disables clamping. */
    outputClamp?: number;
  }>(),
  { running: false, showLogs: false, autoScroll: true, outputClamp: 600 },
);

const t = useAiI18n();
const { containerRef } = useAutoScroll();

const rows = computed(() => groupCliEvents(props.events, { includeLogs: props.showLogs }));
const pending = computed(() => pendingToolCount(rows.value));

/** Tool rows the reader has chosen to expand, keyed by row key. */
const expanded = ref(new Set<string>());

function toggle(key: string): void {
  // Rebuilt rather than mutated so the computed consumers actually re-run.
  const next = new Set(expanded.value);
  if (next.has(key)) next.delete(key);
  else next.add(key);
  expanded.value = next;
}

/** snake_case / camelCase tool name to something readable. */
function readableTool(name: string): string {
  return name
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function argSummary(row: CliTimelineToolRow): string | null {
  if (!row.input) return null;
  const entries = Object.entries(row.input);
  if (entries.length === 0) return null;
  // One line, first argument only: the point is to tell two calls to the same
  // tool apart at a glance, not to render the payload.
  const [key, value] = entries[0]!;
  const rendered = typeof value === 'string' ? value : JSON.stringify(value);
  return entries.length > 1 ? `${key}: ${rendered}, +${entries.length - 1}` : `${key}: ${rendered}`;
}

function isClamped(row: CliTimelineToolRow): boolean {
  return (
    props.outputClamp > 0 &&
    !expanded.value.has(row.key) &&
    (row.output?.length ?? 0) > props.outputClamp
  );
}

function outputText(row: CliTimelineToolRow): string {
  const full = row.output ?? '';
  return isClamped(row) ? full.slice(0, props.outputClamp) : full;
}
</script>

<template>
  <div ref="containerRef" class="t-cli-timeline" :class="{ 't-cli-timeline--scroll': autoScroll }">
    <div v-for="row in rows" :key="row.key" class="t-cli-timeline__row">
      <!-- Assistant text -->
      <p v-if="row.kind === 'text'" class="t-cli-timeline__text whitespace-pre-wrap">
        {{ row.content }}
      </p>

      <!-- Reasoning -->
      <p v-else-if="row.kind === 'thinking'" class="t-cli-timeline__thinking whitespace-pre-wrap">
        {{ row.content }}
      </p>

      <!-- Tool call, with its result folded in -->
      <div v-else-if="row.kind === 'tool'" class="t-cli-timeline__tool">
        <div class="t-cli-timeline__tool-head">
          <TStreamLoader v-if="!row.settled" :size="14" />
          <Icon v-else icon="lucide:check-circle-2" class="size-3.5 t-cli-timeline__ok shrink-0" />
          <span class="t-cli-timeline__tool-name font-medium">{{ readableTool(row.tool) }}</span>
          <span v-if="argSummary(row)" class="t-cli-timeline__args truncate">
            {{ argSummary(row) }}
          </span>
        </div>

        <template v-if="row.output">
          <pre class="t-cli-timeline__output"><code>{{ outputText(row) }}</code></pre>
          <button
            v-if="isClamped(row) || expanded.has(row.key)"
            type="button"
            class="t-cli-timeline__more"
            @click="toggle(row.key)"
          >
            {{ expanded.has(row.key) ? t.cli.showLess : t.cli.showMore }}
          </button>
        </template>
      </div>

      <!-- Status / error / log -->
      <div
        v-else
        class="t-cli-timeline__notice"
        :class="`t-cli-timeline__notice--${row.kind}`"
      >
        <Icon
          :icon="row.kind === 'error' ? 'lucide:x-circle' : 'lucide:info'"
          class="size-3.5 shrink-0"
        />
        <span class="truncate">{{ row.content }}</span>
      </div>
    </div>

    <!-- Live tail. Only meaningful while the run is open. -->
    <div v-if="running" class="t-cli-timeline__live">
      <TStreamLoader :size="14" />
      <span>{{ pending > 0 ? t.cli.waitingTools : t.cli.working }}</span>
    </div>

    <p v-else-if="rows.length === 0" class="t-cli-timeline__empty">{{ t.cli.noActivity }}</p>
  </div>
</template>

<style scoped>
.t-cli-timeline {
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 14px;
  color: var(--tnzi-base-text);
}
.t-cli-timeline--scroll {
  overflow-y: auto;
  /* Both bounds are load-bearing, for different parents. `min-height: 0` lets it
     shrink inside a flex column (flex items default to min-height: auto and would
     otherwise refuse to be smaller than their content). `max-height: 100%` bounds it
     inside a plain block parent of fixed height - without it the element simply grows
     past the parent, `overflow-y: auto` never triggers, and the stick-to-bottom
     behaviour is silently inert because nothing ever scrolls. */
  min-height: 0;
  max-height: 100%;
}
.t-cli-timeline__text {
  margin: 0;
  line-height: 1.6;
}
.t-cli-timeline__thinking {
  margin: 0;
  line-height: 1.6;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
  border-left: 2px solid var(--tnzi-border);
  padding-left: 10px;
}
.t-cli-timeline__tool {
  display: flex;
  flex-direction: column;
  gap: 6px;
  border-radius: 6px;
  padding: 8px 10px;
  background-color: var(--tnzi-ai-tool-call-bg);
}
.t-cli-timeline__tool-head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-cli-timeline__tool-name {
  font-size: 12px;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  /* The name identifies the call, so it is the one thing on this row that must stay
     whole. Without these it is the first thing the flex row squeezes, and a narrow
     viewport breaks "Read File" across two lines inside its own chip. The arguments
     next to it are what should give way - they already truncate. */
  flex-shrink: 0;
  white-space: nowrap;
}
.t-cli-timeline__args {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  min-width: 0;
}
.t-cli-timeline__ok {
  color: var(--tnzi-ai-node-completed);
}
.t-cli-timeline__output {
  margin: 0;
  padding: 8px;
  border-radius: 4px;
  background-color: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  font-size: 12px;
  line-height: 1.5;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-word;
}
.t-cli-timeline__more {
  align-self: flex-start;
  border: none;
  background: none;
  padding: 0;
  cursor: pointer;
  font-size: 12px;
  color: var(--tnzi-primary);
}
.t-cli-timeline__notice {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  min-width: 0;
  color: var(--tnzi-base-text-muted);
}
.t-cli-timeline__notice--error {
  color: var(--tnzi-ai-node-failed);
}
.t-cli-timeline__live {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-cli-timeline__empty {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
</style>
