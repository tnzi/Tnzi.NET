<script setup lang="ts">
/**
 * @experimental
 * TToolRun - tool calls made back to back (no reply text between them), shown as one
 * quiet line that says what happened, with the steps one click away.
 *
 * While the run is in progress (`active`) the line is a working mark and what the latest
 * step is doing ("Reading a page"), the words sliding in as they change (not with reduced
 * motion). Once it is done the line sums the run up by kind ("Read 5 pages, searched the
 * web"), with no mark, unless a step failed and the run never got past it: then the line
 * opens with a failure mark and says what failed. A failure the run retried past (a later
 * step of the same kind completed) is said in the line's own muted words ("2 steps
 * retried after errors"), with no mark; opened, its step still shows as failed. Opened, a box with a hairline border holds one row per step, each
 * drawn by its renderer (`TToolCall`) as a row that still opens its own detail in place.
 *
 * Nothing the user came for is folded away: a step registered `alwaysVisible` (a file
 * handed over) is shown whole under the line, and what a folded step keeps in view (the
 * `after` slot of `TToolStep`) is moved under the line too. A run whose steps are all
 * always visible has no line at all.
 *
 * The words come from each step's description (see `ToolRendererEntry.describe`). One
 * step is a run too, so a reply reads the same however many tools it used, and history
 * looks like a finished live answer.
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n } from '../../i18n/index'
import type { ToolCallInfo } from '../../headless/useChat'
import { isAlwaysVisible, useChatRenderContext } from '../chat/chat-render-context'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import TToolCall from './TToolCall.vue'
import TToolStep from './TToolStep.vue'
import { toolCallState } from './tool-call-status'
import { resolveToolEntry } from './built-in-tool-renderers'
import { describeToolStep, runningRunLabel, stepPhrase, summarizeToolRun, type ToolRunStep } from './tool-steps'
import { ToolRunScope, type ToolRunContext } from './tool-run-context'

const props = withDefaults(
  defineProps<{
    toolCalls: readonly ToolCallInfo[]
    /** The message is still streaming (a call without a result is running, not waiting). */
    streaming?: boolean
    /** The run is still going: the message streams and nothing has been written after it. */
    active?: boolean
  }>(),
  { streaming: false, active: false },
)

defineSlots<{
  /** The working mark at the start of the line while the run is active. */
  mark?: () => unknown
}>()

const t = useAiI18n()
const context = useChatRenderContext()

interface RunItem {
  readonly key: string
  readonly toolCall: ToolCallInfo
  readonly step: ToolRunStep
  readonly alwaysVisible: boolean
}

const items = computed<RunItem[]>(() =>
  props.toolCalls.map((toolCall, i) => {
    const entry = resolveToolEntry(toolCall.name, context.value.toolRenderers)
    return {
      key: toolCall.id ?? `${toolCall.name}:${i}`,
      toolCall,
      step: {
        description: describeToolStep(toolCall, entry.describe, t.value),
        state: toolCallState(toolCall, props.streaming),
      },
      alwaysVisible: isAlwaysVisible(toolCall, entry),
    }
  }),
)

const kept = computed(() => items.value.filter((item) => item.alwaysVisible))
/* A run of nothing but steps that stay in view has nothing to fold. */
const hasLine = computed(() => items.value.some((item) => !item.alwaysVisible))

const summary = computed(() => summarizeToolRun(items.value.map((item) => item.step), t.value))
const runningLabel = computed(() => runningRunLabel(items.value.map((item) => item.step)))
const showFailure = computed(() => !props.active && summary.value.unrecoveredCount > 0)

const open = ref(false)
const outlet = ref<HTMLElement | null>(null)
const openSole = ref(0)
const runContext: ToolRunContext = { outlet, openSole }

function toggle(): void {
  open.value = !open.value
  if (open.value && items.value.length === 1) openSole.value += 1
}
</script>

<template>
  <div class="t-tool-run" :class="{ 'is-open': open, 'is-active': active }">
    <template v-if="hasLine">
      <button type="button" class="t-tool-run__line" :aria-expanded="open" @click="toggle">
        <span v-if="active" class="t-tool-run__mark">
          <slot name="mark"><TStreamLoader :size="14" /></slot>
        </span>
        <span v-else-if="showFailure" class="t-tool-run__mark t-tool-run__mark--failed">
          <Icon icon="lucide:x-circle" aria-hidden="true" />
        </span>
        <span v-if="active" :key="runningLabel" class="t-tool-run__label is-live">{{ runningLabel }}</span>
        <span v-else class="t-tool-run__label" :title="summary.text">
          <span
            v-for="(part, i) in summary.parts"
            :key="i"
            :class="`t-tool-run__part--${part.tone}`"
          >{{ part.text }}</span>
        </span>
        <Icon icon="lucide:chevron-right" class="t-tool-run__chevron" aria-hidden="true" />
      </button>

      <div v-show="open" class="t-tool-run__box">
        <ToolRunScope :context="runContext">
          <div v-for="item in items" :key="item.key" class="t-tool-run__row">
            <!-- A step shown whole under the line is listed here by its words only. -->
            <TToolStep
              v-if="item.alwaysVisible"
              :state="item.step.state"
              :label="stepPhrase(item.step)"
              :subject="item.step.description.subject"
              :expandable="false"
            />
            <TToolCall v-else :tool-call="item.toolCall" :streaming="streaming" />
          </div>
        </ToolRunScope>
      </div>
      <div ref="outlet" class="t-tool-run__outlet" />
    </template>

    <div v-if="kept.length" class="t-tool-run__kept">
      <TToolCall v-for="item in kept" :key="item.key" :tool-call="item.toolCall" :streaming="streaming" />
    </div>
    <!-- No line to carry the working mark: it follows the steps instead, still only once. -->
    <span v-if="active && !hasLine" class="t-tool-run__mark t-tool-run__mark--alone">
      <slot name="mark"><TStreamLoader :size="14" /></slot>
    </span>
  </div>
</template>

<style scoped>
.t-tool-run {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: 0;
  max-width: 100%;
  font-size: 14px;
  line-height: 20px;
}
/* The line: muted text, no box, the chevron right after the words. */
.t-tool-run__line {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 100%;
  min-height: 32px;
  margin: 0;
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-tool-run__line:hover { color: var(--tnzi-ai-text); }
.t-tool-run__line:focus-visible {
  outline: 2px solid var(--tnzi-ai-accent);
  outline-offset: 2px;
}
.t-tool-run__mark {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 20px;
  font-size: 16px;
  line-height: 1;
  color: var(--tnzi-ai-text-tertiary);
}
.t-tool-run__mark--failed { color: var(--tnzi-ai-danger); }
.t-tool-run__label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-tool-run__part--failed { color: var(--tnzi-ai-danger); }
.t-tool-run__chevron {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-tool-run.is-open .t-tool-run__chevron { transform: rotate(90deg); }
/* Each new step's words slide in; the label is re-created when they change. */
@media (prefers-reduced-motion: no-preference) {
  .t-tool-run__label.is-live { animation: t-tool-run-label-in 180ms var(--tnzi-ai-easing, ease) both; }
}
@keyframes t-tool-run-label-in {
  from { opacity: 0; transform: translateY(4px); }
  to { opacity: 1; transform: none; }
}
.t-tool-run__box {
  align-self: stretch;
  min-width: 0;
  margin: 2px 0 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  overflow: hidden;
}
.t-tool-run__row + .t-tool-run__row { border-top: 1px solid var(--tnzi-ai-border); }
.t-tool-run__outlet {
  align-self: stretch;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-tool-run__outlet:empty { display: none; }
.t-tool-run__kept {
  align-self: stretch;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
  min-width: 0;
  margin: 4px 0;
}
</style>
