<script setup lang="ts">
/**
 * @experimental
 * TToolStep - one tool call as a step of the answer: a quiet line saying what the agent
 * did, with what it found folded under it.
 *
 * The line is secondary text with no box: the state (a spinner while it runs, a mark
 * when it failed or never finished, the step's icon once done), the words for the
 * action in a muted tone ("Searched the web", or the tool's name), then what it acted
 * on in body text ("tauri deep link", a path, a site). When there is more to see, the
 * line toggles it open below, in a box with a hairline border.
 *
 * Every tool view in this package is drawn with it (the generic card and the CLI's
 * web tools), and a product's own tool renderers use it too, so all tool calls in a
 * thread read as one family. The body is the default slot; the parts that fit in it
 * are `TToolFields` (labelled lines), `TToolLinkList` (web pages) and `TToolOutput`
 * (text, monospace by default, folded when long). The `after` slot stays visible
 * under the line whether the step is open or not: the files a call handed over, the
 * pictures it took, whatever the user came for.
 *
 * Inside a run of steps (`TToolRun`) the step is a row of the run's box instead, and
 * its `after` content moves out to the run's outlet under the run's line, so it stays
 * in view while the run is folded.
 */
import { computed, inject, ref, useSlots, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n } from '../../i18n/index'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import type { ToolCallState } from './tool-call-status'
import { TOOL_RUN_KEY } from './tool-run-context'

const props = withDefaults(
  defineProps<{
    /** The call's state (`toolCallState(toolCall, streaming)`). */
    state: ToolCallState
    /** What the agent did, in words: a verb ("Searched the web") or the tool's readable name. */
    label: string
    /** What it acted on (the query, the command, the site), in body text. Truncated to one line. */
    subject?: string | null
    /** The subject is code (a command, a path): set in monospace. */
    mono?: boolean
    /** The mark once the call has completed (an Iconify name). */
    icon?: string
    /**
     * Whether there is a body to open. Defaults to whether a default slot was given; pass
     * it when the slot is always there but may have nothing to show.
     */
    expandable?: boolean
    /** Start open. */
    defaultOpen?: boolean
  }>(),
  { subject: null, mono: false, icon: 'lucide:check', expandable: undefined, defaultOpen: false },
)

const emit = defineEmits<{
  /** The step was opened or closed. */
  toggle: [open: boolean]
}>()

defineSlots<{
  /** The body: shown, in the bordered box, while the step is open. */
  default?: () => unknown
  /** Short text after the subject (a computer's name, a count). Text only: it sits inside the line's button. */
  meta?: () => unknown
  /** Shown under the line whether the step is open or not (files, pictures). */
  after?: () => unknown
  /** Replaces the leading mark. */
  icon?: (props: { state: ToolCallState }) => unknown
}>()

const slots = useSlots()
const t = useAiI18n()

const open = ref(props.defaultOpen)
const canOpen = computed(() => props.expandable ?? Boolean(slots.default))
const stateLabel = computed(() => t.value.tools[props.state])

/* Inside a run: a row, whose kept-in-view content goes to the run's outlet. */
const run = inject(TOOL_RUN_KEY, null)
const outlet = computed(() => run?.outlet.value ?? null)
if (run) {
  watch(run.openSole, () => {
    if (canOpen.value) open.value = true
  })
}

function toggle(): void {
  open.value = !open.value
  emit('toggle', open.value)
}
</script>

<template>
  <div class="t-tool-step" :class="[`t-tool-step--${state}`, { 'is-open': canOpen && open, 'is-row': !!run }]">
    <component
      :is="canOpen ? 'button' : 'div'"
      class="t-tool-step__line"
      v-bind="canOpen ? { type: 'button', 'aria-expanded': open, title: open ? t.chat.hideToolDetails : t.chat.showToolDetails } : {}"
      @click="canOpen ? toggle() : undefined"
    >
      <span class="t-tool-step__mark">
        <slot name="icon" :state="state">
          <TStreamLoader v-if="state === 'running'" :size="14" :label="stateLabel" />
          <template v-else>
            <Icon v-if="state === 'failed'" icon="lucide:x-circle" aria-hidden="true" />
            <Icon v-else-if="state === 'pending'" icon="lucide:clock" aria-hidden="true" />
            <Icon v-else :icon="icon" aria-hidden="true" />
            <span class="sr-only">{{ stateLabel }}</span>
          </template>
        </slot>
      </span>
      <span class="t-tool-step__label">{{ label }}</span>
      <span v-if="subject" class="t-tool-step__subject" :class="{ 'is-mono': mono }" :title="subject">{{ subject }}</span>
      <span v-if="$slots.meta" class="t-tool-step__meta"><slot name="meta" /></span>
      <Icon v-if="canOpen" icon="lucide:chevron-right" class="t-tool-step__chevron" aria-hidden="true" />
    </component>

    <div v-if="canOpen && open" class="t-tool-step__body">
      <slot />
    </div>
    <!-- In place, or under the run's line when the step is folded into a run. -->
    <Teleport v-if="$slots.after" :to="outlet" :disabled="!outlet">
      <div class="t-tool-step__after">
        <slot name="after" />
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.t-tool-step {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: 0;
  max-width: 100%;
  font-size: 14px;
  line-height: 1.5;
}
/* The line: secondary text, no box. Only as wide as its words, so the hover and
   the click land on what was read. */
.t-tool-step__line {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 100%;
  margin: 0;
  padding: 2px 0;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  text-align: left;
  transition: color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
button.t-tool-step__line { cursor: pointer; }
button.t-tool-step__line:hover { color: var(--tnzi-ai-text); }
button.t-tool-step__line:focus-visible {
  outline: 2px solid var(--tnzi-ai-accent);
  outline-offset: 2px;
}
.t-tool-step__mark {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  font-size: 15px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-tool-step--failed .t-tool-step__mark { color: var(--tnzi-ai-danger); }
/* The words for the action give way last: the subject truncates first. */
.t-tool-step__label {
  flex: 0 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-tool-step__subject {
  flex: 0 100 auto;
  min-width: 2em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text);
}
.t-tool-step__subject.is-mono {
  font-family: var(--tnzi-ai-font-mono);
  font-size: 13px;
}
.t-tool-step__meta {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
  white-space: nowrap;
}
.t-tool-step__chevron {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-tool-step.is-open .t-tool-step__chevron { transform: rotate(90deg); }
.t-tool-step__body {
  align-self: stretch;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
  margin: 4px 0 6px;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  color: var(--tnzi-ai-text);
  font-size: 13px;
}
.t-tool-step__after {
  align-self: stretch;
  min-width: 0;
  margin: 4px 0 2px;
}

/* A row of a run's box: the line spans the row, the body opens in place without a
   box of its own (the run's box already frames it). */
.t-tool-step.is-row { align-items: stretch; }
.t-tool-step.is-row > .t-tool-step__line {
  min-height: 36px;
  padding: 6px 12px;
  border-radius: 0;
}
.t-tool-step.is-row > .t-tool-step__line:focus-visible { outline-offset: -2px; }
.t-tool-step.is-row > .t-tool-step__body {
  margin: 0;
  padding: 0 12px 12px 34px;
  border: 0;
  border-radius: 0;
}
</style>
