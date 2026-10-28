<script setup lang="ts">
/**
 * @experimental
 * TThreadMessage - one turn in a Manus-style thread.
 *
 * The package ships **two** message renderers because they are two different
 * layouts, not two skins:
 *
 *   - `TChatMessage` - classic transcript. Avatar on both sides, bubble on
 *     both sides. Reads as a conversation between two parties.
 *   - `TThreadMessage` (this one) - Manus/agent layout. The user gets a right
 *     aligned bubble with no avatar; the assistant gets a left byline row
 *     (optional: `showAgentByline`) and **no bubble at all**, so its answer
 *     reads as document body rather than as chat. That asymmetry is the whole
 *     point: it gives long agent output the full column width.
 *
 * The action row under an answer (copy, regenerate, ratings) shows while the
 * turn is hovered or something in it has keyboard focus; on a device without
 * hover it is always shown.
 *
 * Extracted from TChatApp, which rendered it inline. Anyone building a custom
 * shell can now get the same thread visuals without adopting the whole
 * TChatApp.
 *
 * State stays outside: `copied` is a prop and the clipboard write happens in
 * the consumer, so a thread of N messages keeps one reset timer instead of N.
 */
import { Icon } from '@iconify/vue'
import TMessageResponse from './TMessageResponse.vue'
import TMessageAttachments from './TMessageAttachments.vue'
import TReasoningStage from '../reasoning/TReasoningStage.vue'
import TToolRun from '../reasoning/TToolRun.vue'
import { computed, nextTick, ref, useSlots } from 'vue'
import { useAiI18n } from '../../i18n/index'
import { formatCompactNumber } from '../../utils/format'
import TUsageLimitNotice from './TUsageLimitNotice.vue'
import { USAGE_LIMIT_REACHED_ERROR_CODE } from '../../headless/usage-limits'
import { isImeComposing } from '@tnzi/core/utils'
import { useAutoGrowTextarea } from '../../headless/useAutoGrowTextarea'
import type { ChatMessage } from '../../headless/useChat'
import { replyParts, segmentReply, type ReplyPart, type SegmentedReply } from './reply-segments'

const props = withDefaults(
  defineProps<{
    message: ChatMessage
    /** Shown when the message carries no `agentName` of its own. Defaults to
     *  the current locale's. */
    agentName?: string
    /** Small tag after the agent name (e.g. "Pro", "Lite"). */
    agentLabel?: string
    /**
     * The agent's mark at the start of the byline: an Iconify icon name, drawn
     * in the byline's tile. The `agent-mark` slot replaces the tile instead.
     */
    agentIcon?: string
    /**
     * The mark while this answer is still being written (an animated variant of
     * `agentIcon`, say). Unset, the mark does not change while streaming. With
     * the byline off it is also the "still writing" mark, in place of the
     * pulsing dot.
     */
    agentStreamingIcon?: string
    /**
     * The row above the answer: the agent's mark, name and label (or the `role`
     * slot). Off, the answer starts with its text; a reply with no text yet shows
     * a "still writing" mark where it will appear (a pulsing dot, or the
     * streaming mark when one is given), and the caret at the end of the text
     * takes over once words arrive.
     */
    showAgentByline?: boolean
    /** Renders the copy button in its confirmed state. */
    copied?: boolean
    /** Render the action row under assistant turns. */
    showActions?: boolean
    /** Render the tool-call cards the turn reports (`message.toolCalls`). */
    showToolCalls?: boolean
    /** Render the token line under the answer (`message.usage`). */
    showUsage?: boolean
    /**
     * Offer to edit this user message (inline). The edit is emitted; the consumer sends it
     * (`useChatThreads().editMessage`).
     */
    enableEdit?: boolean
    /**
     * The edit action is unavailable for now (a reply is streaming). Its row keeps its
     * place: taking it away moved every message below it, and the whole transcript
     * jumped when a reply started and again when it ended.
     */
    editDisabled?: boolean
  }>(),
  {
    agentLabel: '',
    agentIcon: 'lucide:sparkles',
    agentStreamingIcon: undefined,
    showAgentByline: true,
    copied: false,
    showActions: true,
    showToolCalls: true,
    showUsage: false,
    enableEdit: false,
    editDisabled: false,
  },
)

const emit = defineEmits<{
  copy: [messageId: string]
  regenerate: [messageId: string]
  feedback: [messageId: string, type: 'positive' | 'negative']
  /** The user edited this message: the new text, to send in its place. */
  edit: [messageId: string, content: string]
}>()

const t = useAiI18n()

const displayAgentName = computed(
  () => props.message.agentName || props.agentName || t.value.chat.agentName,
)

const isStreaming = computed(() => props.message.isStreaming ?? false)

/* The byline's mark: the streaming variant while the answer is being written,
   when there is one. */
const markIcon = computed(() =>
  isStreaming.value && props.agentStreamingIcon ? props.agentStreamingIcon : props.agentIcon,
)

/* The rating the message carries (from the thread's history, or written by the
   consumer when a click lands), so the pressed thumb shows. */
const ratedPositive = computed(() => props.message.feedbackRating === true)
const ratedNegative = computed(() => props.message.feedbackRating === false)

/* The answer and its tool cards in reading order: a card that says where in the
   text it happened (external CLI agents) sits there; one that does not leads. */
const reply = computed<SegmentedReply>(() =>
  props.showToolCalls
    ? segmentReply(props.message.content, props.message.toolCalls)
    : { lead: [], segments: [{ text: props.message.content, toolCalls: [] }] },
)

/* The reply as text and runs of tool calls: calls made back to back, with no text
   between them, read as one line (`TToolRun`). */
const parts = computed<ReplyPart[]>(() => replyParts(reply.value))

/* The run still going: the answer streams and nothing has been written after it
   (the text after it is still blank). */
const activeRunIndex = computed(() => {
  if (!props.message.isStreaming) return -1
  const list = parts.value
  const tail = list[list.length - 1]
  const before = list[list.length - 2]
  return tail?.kind === 'text' && !tail.text.trim() && before?.kind === 'run' ? list.length - 2 : -1
})

/* The last piece of text still renders while the answer streams (its caret), and
   for an answer with no text that did not fail (as before the cards moved in). */
const showFinalResponse = computed(
  () =>
    (props.message.isStreaming ?? false)
    || (!props.message.content && props.message.status !== 'error' && props.message.status !== 'usage_limit'),
)

/* With the byline off nothing else says "still writing" before the first words
   (the caret lives at the end of the text) or while the agent works between two
   pieces of text (a tool card is the last thing on screen). */
const showPendingMark = computed(() => {
  if (props.showAgentByline || !props.message.isStreaming) return false
  const last = reply.value.segments[reply.value.segments.length - 1]
  return !last || !last.text.trim()
})

/* That mark is the product's own working mark when it gave one (the
   `agent-mark` slot, else `agentStreamingIcon`), otherwise the pulsing dot.
   `agentIcon` alone does not count: it is the idle mark, and a still one would
   not say "working". */
const slots = useSlots()
const pendingUsesMark = computed(() => Boolean(slots['agent-mark'] || props.agentStreamingIcon))

/* While a run of tool calls is the last thing on screen, the mark moves onto the
   start of the run's line instead of sitting under it: one sign of work, never two. */
const markInRun = computed(() => showPendingMark.value && activeRunIndex.value >= 0)

/* The usage-limit outcome: whose limit it was (this deployment's limit for the
   user, or the external CLI's own account), and when it resets. */
const usageLimitScope = computed<'user' | 'account'>(() =>
  props.message.errorCode === USAGE_LIMIT_REACHED_ERROR_CODE ? 'user' : 'account',
)

/* Inline edit of a user message. */
const editing = ref(false)
const draft = ref('')
const editor = ref<HTMLTextAreaElement | null>(null)

/* The field grows with the text, so the whole message is in view while it is
   edited; past this it scrolls. */
const EDITOR_MAX_HEIGHT = 480
const resizeEditor = useAutoGrowTextarea(editor, draft, EDITOR_MAX_HEIGHT)

function startEdit(): void {
  draft.value = props.message.content
  editing.value = true
  void nextTick(() => {
    resizeEditor()
    const el = editor.value
    if (!el) return
    el.focus()
    el.setSelectionRange(el.value.length, el.value.length)
  })
}

function cancelEdit(): void {
  editing.value = false
}

function submitEdit(): void {
  const content = draft.value.trim()
  if (!content) return
  editing.value = false
  if (content !== props.message.content.trim()) emit('edit', props.message.id, content)
}

function onEditKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    cancelEdit()
  } else if (event.key === 'Enter' && !event.shiftKey && !isImeComposing(event)) {
    event.preventDefault()
    submitEdit()
  }
}
</script>

<template>
  <div
    class="t-thread-message"
    :class="`t-thread-message--${message.role}`"
    :aria-busy="message.role !== 'user' && message.isStreaming ? 'true' : undefined"
  >
    <template v-if="message.role === 'user'">
      <TMessageAttachments
        v-if="message.attachments?.length"
        :attachments="message.attachments"
        class="t-thread-message__attachments"
      />
      <div v-if="editing" class="t-thread-message__editor">
        <textarea
          ref="editor"
          v-model="draft"
          class="t-thread-message__editor-input"
          rows="1"
          :aria-label="t.chat.editMessage"
          @keydown="onEditKeydown"
        />
        <div class="t-thread-message__editor-actions">
          <button type="button" class="t-thread-message__editor-btn" @click="cancelEdit">
            {{ t.chat.cancelEdit }}
          </button>
          <button
            type="button"
            class="t-thread-message__editor-btn t-thread-message__editor-btn--primary"
            :disabled="!draft.trim()"
            @click="submitEdit"
          >
            {{ t.chat.saveEdit }}
          </button>
        </div>
      </div>
      <template v-else>
        <div v-if="message.content" class="t-thread-message__bubble t-thread-message__bubble--user">
          {{ message.content }}
        </div>
        <div
          v-if="enableEdit"
          class="t-thread-message__user-actions"
          :class="{ 'is-disabled': editDisabled }"
        >
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="t.chat.editMessage"
            :disabled="editDisabled"
            @click="startEdit"
          >
            <Icon icon="lucide:pencil" />
          </button>
        </div>
      </template>
    </template>

    <template v-else>
      <slot v-if="showAgentByline" name="role" :message="message">
        <div class="t-thread-message__role">
          <slot name="agent-mark" :message="message" :streaming="isStreaming" placement="byline">
            <span class="t-thread-message__brand" aria-hidden="true">
              <Icon :icon="markIcon" />
            </span>
          </slot>
          <strong>{{ displayAgentName }}</strong>
          <span v-if="agentLabel" class="t-thread-message__tag">{{ agentLabel }}</span>
          <span
            v-if="message.isStreaming"
            class="t-thread-message__streaming"
            :aria-label="t.chat.streaming"
          >●</span>
        </div>
      </slot>

      <TReasoningStage
        v-if="message.reasoning"
        :status="message.isStreaming ? 'running' : 'done'"
      >
        {{ message.reasoning }}
      </TReasoningStage>

      <!-- The answer in reading order: text, and the tool calls made between its
           pieces, each run of them one line where the agent made them (calls that
           do not say where, from built-in agents, lead). Whatever text arrived stays
           visible; a failure is said UNDER it rather than replacing it, so a stream
           that died halfway keeps its half. -->
      <template v-for="(part, p) in parts" :key="p">
        <TMessageResponse
          v-if="part.kind === 'text' && (part.text.trim() || (part.last && showFinalResponse && !showPendingMark))"
          class="t-thread-message__body"
          :content="part.text"
          :streaming="part.kind === 'text' && part.last && (message.isStreaming ?? false)"
        />
        <TToolRun
          v-else-if="part.kind === 'run'"
          class="t-thread-message__tools"
          :tool-calls="part.toolCalls"
          :streaming="message.isStreaming ?? false"
          :active="p === activeRunIndex"
        >
          <template v-if="markInRun && p === activeRunIndex" #mark>
            <span
              v-if="pendingUsesMark"
              class="t-thread-message__run-mark"
              role="status"
              :aria-label="t.chat.generating"
            >
              <slot name="agent-mark" :message="message" :streaming="isStreaming" placement="pending" :in-run="true">
                <Icon :icon="agentStreamingIcon ?? ''" aria-hidden="true" />
              </slot>
            </span>
            <span
              v-else
              class="t-thread-message__pending t-thread-message__pending--in-run"
              role="status"
              :aria-label="t.chat.generating"
            />
          </template>
        </TToolRun>
      </template>
      <span
        v-if="showPendingMark && !markInRun && pendingUsesMark"
        class="t-thread-message__pending-mark"
        role="status"
        :aria-label="t.chat.generating"
      >
        <slot name="agent-mark" :message="message" :streaming="isStreaming" placement="pending">
          <Icon :icon="agentStreamingIcon ?? ''" aria-hidden="true" />
        </slot>
      </span>
      <span
        v-else-if="showPendingMark && !markInRun"
        class="t-thread-message__pending"
        role="status"
        :aria-label="t.chat.generating"
      />
      <div v-if="message.status === 'error'" class="t-thread-message__error" role="alert">
        <Icon icon="lucide:circle-alert" class="t-thread-message__error-icon" />
        <span>{{ message.error || t.chat.errorGeneric }}</span>
        <button
          type="button"
          class="t-thread-message__error-retry"
          @click="emit('regenerate', message.id)"
        >
          <Icon icon="lucide:rotate-ccw" />
          {{ t.common.retry }}
        </button>
      </div>
      <TUsageLimitNotice
        v-else-if="message.status === 'usage_limit'"
        variant="inline"
        :scope="usageLimitScope"
        :window="message.usageLimitWindow ?? null"
        :resets-at="message.usageLimitResetsAt ?? null"
      />
      <div v-else-if="message.status === 'stopped'" class="t-thread-message__stopped">
        <span class="t-thread-message__stopped-mark" aria-hidden="true" />
        {{ t.chat.generationStopped }}
      </div>

      <div v-if="showUsage && message.usage && !message.isStreaming" class="t-thread-message__usage">
        <span>{{ t.token.input }} {{ formatCompactNumber(message.usage.inputTokens) }}</span>
        <span aria-hidden="true">·</span>
        <span>{{ t.token.output }} {{ formatCompactNumber(message.usage.outputTokens) }}</span>
      </div>

      <div
        v-if="showActions && !message.isStreaming"
        class="t-thread-message__actions"
      >
        <slot name="actions" :message="message" :copied="copied">
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="copied ? t.chat.copied : t.chat.copy"
            @click="emit('copy', message.id)"
          >
            <Icon :icon="copied ? 'lucide:check' : 'lucide:copy'" />
          </button>
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="t.chat.regenerate"
            @click="emit('regenerate', message.id)"
          >
            <Icon icon="lucide:refresh-ccw" />
          </button>
          <button
            type="button"
            class="t-thread-message__action"
            :class="{ 'is-active': ratedPositive }"
            :aria-label="t.chat.like"
            :aria-pressed="ratedPositive"
            @click="emit('feedback', message.id, 'positive')"
          >
            <Icon icon="lucide:thumbs-up" />
          </button>
          <button
            type="button"
            class="t-thread-message__action"
            :class="{ 'is-active': ratedNegative }"
            :aria-label="t.chat.dislike"
            :aria-pressed="ratedNegative"
            @click="emit('feedback', message.id, 'negative')"
          >
            <Icon icon="lucide:thumbs-down" />
          </button>
        </slot>
      </div>

      <slot name="after" :message="message" />
    </template>
  </div>
</template>

<style scoped>
.t-thread-message {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-thread-message--user { align-items: flex-end; }
.t-thread-message__user-actions {
  display: flex;
  justify-content: flex-end;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-thread-message--user:hover .t-thread-message__user-actions,
.t-thread-message__user-actions:focus-within { opacity: 1; }
.t-thread-message__user-actions.is-disabled { visibility: hidden; }
.t-thread-message__editor {
  display: flex;
  flex-direction: column;
  gap: 8px;
  /* The whole reading column, not the bubble's width: the text being edited
     wraps as little as it can. */
  align-self: stretch;
  width: 100%;
}
.t-thread-message__editor-input {
  display: block;
  box-sizing: border-box;
  width: 100%;
  resize: none;
  padding: 12px 16px;
  border-radius: 14px;
  border: 1px solid var(--tnzi-ai-border-strong);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: var(--tnzi-ai-field-font-size, 15px);
  line-height: 1.6;
  outline: none;
  transition: border-color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease),
    box-shadow var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-thread-message__editor-input:focus {
  border-color: var(--tnzi-ai-accent);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft);
}
.t-thread-message__editor-actions { display: flex; justify-content: flex-end; gap: 8px; }
.t-thread-message__editor-btn {
  padding: 6px 14px;
  border-radius: 999px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-size: 13px;
  cursor: pointer;
}
.t-thread-message__editor-btn--primary {
  border-color: transparent;
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
}
.t-thread-message__editor-btn:disabled { opacity: 0.5; cursor: default; }
.t-thread-message__attachments { max-width: 75%; justify-content: flex-end; }
.t-thread-message__bubble--user {
  padding: 10px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 18px;
  max-width: 75%;
  font-size: 15px;
  line-height: 1.6;
  color: var(--tnzi-ai-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.t-thread-message__role {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-message__role strong {
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-thread-message__brand {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 6px;
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
  font-size: 13px;
  flex-shrink: 0;
}
.t-thread-message__tag {
  font-size: 10px;
  padding: 1px 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 4px;
  color: var(--tnzi-ai-text-tertiary);
  font-weight: 500;
  letter-spacing: 0.02em;
}
.t-thread-message__streaming {
  color: var(--tnzi-ai-accent-text);
  animation: t-thread-message-pulse 1s infinite;
}
@keyframes t-thread-message-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}
.t-thread-message__body {
  font-size: 15px;
  line-height: 1.6;
  word-break: break-word;
  color: var(--tnzi-ai-text);
}
/* An answer that opens with a heading starts flush with the turn, like one that
   opens with a paragraph: without a byline above it, the heading's top margin
   was a gap with nothing to separate. */
.t-thread-message__body > :deep(:first-child) { margin-top: 0; }
/* The "still writing" mark when there is no byline: the same dot, colour and
   pulse as the caret the markdown puts at the end of the text, so the signal
   does not change shape when the first words arrive. */
.t-thread-message__pending {
  width: 8px;
  height: 8px;
  margin: 7px 0;
  border-radius: 50%;
  background: var(--tnzi-ai-node-active, var(--tnzi-ai-accent));
  animation: t-thread-message-pulse 1s ease-in-out infinite;
}
/* The same signal in the product's own mark: one line of the answer tall (the
   body's 15px at line-height 1.6), so the first words take its place without
   the turn changing height. No pulse: the mark is expected to carry its own
   motion (an animated SVG), and pulsing it as well would be two animations. */
.t-thread-message__pending-mark {
  display: inline-flex;
  align-items: center;
  align-self: flex-start;
  height: 24px;
  font-size: 18px;
  line-height: 1;
  color: var(--tnzi-ai-text);
}
/* A run of tool calls sits between the pieces of text it was made between; its
   line is 32px tall, so the turn's 8px gap is enough on either side. */
.t-thread-message__tools { margin: -4px 0; }
/* The working mark at the start of a run's line: one line of the run (20px), the
   product's mark scaled to it. */
.t-thread-message__run-mark {
  display: inline-flex;
  align-items: center;
  height: 20px;
  font-size: 16px;
  line-height: 1;
  color: var(--tnzi-ai-text);
}
.t-thread-message__pending--in-run { margin: 0 4px; }
.t-thread-message__usage {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11.5px;
  color: var(--tnzi-ai-text-tertiary);
  user-select: none;
}
.t-thread-message__error {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-danger) 28%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-danger) 6%, var(--tnzi-ai-surface));
  border-radius: 10px;
  color: var(--tnzi-ai-danger);
  font-size: 14px;
}
.t-thread-message__error-icon {
  flex-shrink: 0;
  font-size: 16px;
}
.t-thread-message__error-retry {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: none;
  background: none;
  color: var(--tnzi-ai-danger);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 6px;
}
.t-thread-message__error-retry:hover {
  background: color-mix(in srgb, var(--tnzi-ai-danger) 10%, transparent);
}
.t-thread-message__stopped {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
  font-size: 11.5px;
  color: var(--tnzi-ai-text-tertiary);
  user-select: none;
}
.t-thread-message__stopped-mark {
  width: 9px;
  height: 9px;
  border-radius: 2px;
  background: currentColor;
  flex-shrink: 0;
}

/* Action buttons. Icon-only by default: a bare glyph in a 28x28 hit area, no
   box until the pointer is on it (then a faint wash); adding a label span next
   to the icon widens it via `--with-label`. The row starts 6px left of the
   text, so the first glyph lines up with the answer's left edge.
   The row keeps its space while hidden (opacity, not display), so hovering a
   turn moves nothing. It shows on hover of the whole turn and whenever
   something in the turn has keyboard focus: a keyboard user tabbing to the
   copy button sees it, while a mouse click does not leave the row pinned open
   by the focus it gave the button. */
.t-thread-message__actions {
  display: flex;
  align-items: center;
  gap: 2px;
  margin-top: 4px;
  margin-left: -6px;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-thread-message:hover .t-thread-message__actions { opacity: 1; }
.t-thread-message:has(:focus-visible) .t-thread-message__actions { opacity: 1; }
/* No hover to reveal it (touch screens): always there. */
@media (hover: none) {
  .t-thread-message__actions { opacity: 1; }
}
.t-thread-message__action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 6px;
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition:
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-message__action:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-thread-message__action:focus-visible {
  outline: 2px solid var(--tnzi-ai-accent);
  outline-offset: 1px;
}
/* A pressed rating is said by its colour alone: no box at rest, as for the rest. */
.t-thread-message__action.is-active { color: var(--tnzi-ai-accent-text); }
.t-thread-message__action > .iconify {
  font-size: 16px;
  color: inherit;
}
.t-thread-message__action--with-label {
  width: auto;
  padding: 0 8px;
}
</style>
