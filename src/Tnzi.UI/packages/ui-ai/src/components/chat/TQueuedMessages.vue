<script setup lang="ts">
/**
 * TQueuedMessages - a conversation's queued messages (a `MessageQueue`), over the composer in its
 * sticky band so they stay in view while the answer scrolls. Each one shows its attachments and can
 * be edited in place, removed, or sent now. While the turn runs, "Send now" hands it to the agent
 * without stopping anything when the turn can take messages; when it cannot (or the message carries
 * files the agent opens with its tools) the button says "Stop and send", because that is what it
 * does. A message handed to the agent says so and has no actions; once the agent takes it in it is a
 * message of the conversation and leaves the list (`MessageQueue.items` does not hold it). A held
 * message says why it waits (the turn was stopped, its turn was refused, the agent did not take it)
 * and goes only through "Send now". The list goes away when nothing is queued, so it folds itself
 * once the messages went out.
 *
 * `folded` starts it as one line with a Show button (a phone, while answer cards take the room);
 * the user can open it again.
 *
 * Keyboard: in the editor Enter saves (not while an input method composes) and Escape cancels;
 * focus goes back to the message's Edit button either way.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import type { QueuedMessageDto } from '@tnzi/core/services/ai'
import { isImeComposing } from '@tnzi/core/utils'
import { useAiI18n, formatAiMessage } from '../../i18n/index'
import TButton from '../controls/TButton.vue'
import TIconButton from '../controls/TIconButton.vue'
import { vTooltip } from '../../headless/tooltip'
import { vScrollHint } from '../../headless/scroll-hint'
import { isChangeable, queuedLine, queuedText, type MessageQueue } from '../../headless/message-queue'

const props = withDefaults(
  defineProps<{
    queue: MessageQueue
    /** Start folded to its title line (opened again with Show). */
    folded?: boolean
  }>(),
  { folded: false },
)

const t = useAiI18n()

const editing = ref<string | null>(null)
const draft = ref('')
const saving = ref(false)
const editor = ref<HTMLTextAreaElement[] | null>(null)
const root = ref<HTMLElement | null>(null)
const open = ref(!props.folded)
watch(() => props.folded, (folded) => (open.value = !folded))

const items = computed(() => props.queue.items.value)
const running = computed(() => props.queue.running.value)
const waiting = computed(() => items.value.filter((m) => m.state !== 'Held').length)
/* What the hint beside the title says while the turn runs: the agent reads them as it works, unless
   one of those waiting would have to stop the turn (it goes as the next turn, with the ones after it). */
const toAgent = computed(() => items.value.every((m) => !isChangeable(m) || m.sendNowStopsTurn === false))
const busy = (id: string) => props.queue.busy.value.has(id)

/* An edited message that left (it went out, or was removed elsewhere) ends its editing. */
watch(items, (list) => {
  if (editing.value && !list.some((m) => m.id === editing.value)) cancelEdit(false)
})

function focusEditButton(id: string): void {
  void nextTick(() => {
    root.value?.querySelector<HTMLElement>(`[data-queued-id="${CSS.escape(id)}"] [data-queued-edit]`)?.focus()
  })
}

function startEdit(item: QueuedMessageDto): void {
  editing.value = item.id
  draft.value = item.text
  void nextTick(() => {
    const box = editor.value?.[0]
    if (!box) return
    box.focus()
    box.setSelectionRange(box.value.length, box.value.length)
  })
}

function cancelEdit(refocus = true): void {
  const id = editing.value
  editing.value = null
  draft.value = ''
  if (refocus && id) focusEditButton(id)
}

async function save(id: string): Promise<void> {
  const text = queuedText(draft.value)
  if (!text || saving.value) return
  saving.value = true
  try {
    if (await props.queue.edit(id, text)) cancelEdit()
  } finally {
    saving.value = false
  }
}

function onEditorKeydown(event: KeyboardEvent, id: string): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    cancelEdit()
    return
  }
  if (event.key === 'Enter' && !event.shiftKey && !isImeComposing(event)) {
    event.preventDefault()
    void save(id)
  }
}

/** Why a held message did not go, in the product's words for the codes it knows. */
function reason(item: QueuedMessageDto): string {
  switch (item.failureCode) {
    case 'AI_MODEL_NOT_ALLOWED':
      return t.value.queue.reasonModel
    case 'AI_CHAT_AGENT_NOT_ALLOWED':
      return t.value.queue.reasonAgent
    case 'AI_USAGE_LIMIT_REACHED':
      return t.value.queue.reasonLimit
    case 'AI_QUEUED_MESSAGE_INTERRUPTED':
      return t.value.queue.reasonInterrupted
    case 'AI_QUEUED_MESSAGE_NOT_TAKEN':
      return t.value.queue.reasonNotTaken
    default:
      return item.failure ?? ''
  }
}

function lineText(item: QueuedMessageDto): string | null {
  switch (queuedLine(item)) {
    case 'sending':
      return t.value.queue.sending
    case 'next':
      return t.value.queue.next
    case 'handed':
      return t.value.queue.handed
    case 'stopped':
      return t.value.queue.stopped
    case 'failed':
      return formatAiMessage(t.value.queue.failed, { reason: reason(item) })
    default:
      return null
  }
}

/** Sending it now stops the turn going on: the server says so per message; one that does not say (an older server) does. */
function stopsTurn(item: QueuedMessageDto): boolean {
  return running.value && item.sendNowStopsTurn !== false
}

function sendLabel(item: QueuedMessageDto): string {
  if (!running.value) return t.value.queue.send
  return stopsTurn(item) ? t.value.queue.stopAndSend : t.value.queue.sendNow
}

function sendHint(item: QueuedMessageDto): string | undefined {
  if (!running.value) return undefined
  return stopsTurn(item) ? t.value.queue.stopAndSendHint : t.value.queue.sendNowHint
}

function attachmentName(attachment: { fileName?: string | null; mediaType: string }): string {
  return attachment.fileName || attachment.mediaType
}
</script>

<template>
  <section
    v-if="items.length > 0"
    ref="root"
    class="t-queued-messages"
    :class="{ 'is-folded': !open }"
    role="region"
    :aria-label="t.queue.region"
    data-queued-messages
  >
    <header class="t-queued-messages__head">
      <Icon icon="lucide:list-ordered" class="t-queued-messages__head-icon" aria-hidden="true" />
      <strong>{{ formatAiMessage(t.queue.title, { count: waiting > 0 ? waiting : items.length }) }}</strong>
      <span v-if="open && running && waiting > 0" class="t-queued-messages__hint">{{ toAgent ? t.queue.hintToAgent : t.queue.hint }}</span>
      <button
        type="button"
        class="t-queued-messages__toggle"
        :aria-expanded="open"
        data-queued-toggle
        @click="open = !open"
      >
        {{ open ? t.queue.hide : t.queue.show }}
        <Icon :icon="open ? 'lucide:chevron-down' : 'lucide:chevron-up'" aria-hidden="true" />
      </button>
    </header>
    <ol v-show="open" v-scroll-hint class="t-queued-messages__list t-ai-scroll" aria-live="polite">
      <li
        v-for="item in items"
        :key="item.id"
        class="t-queued-messages__item"
        :class="{ 'is-held': item.state === 'Held', 'is-failed': queuedLine(item) === 'failed', 'is-with-agent': !isChangeable(item) }"
        data-queued-message
        :data-queued-id="item.id"
        :data-state="item.state"
      >
        <template v-if="editing === item.id">
          <textarea
            ref="editor"
            v-model="draft"
            class="t-queued-messages__editor"
            rows="2"
            :aria-label="t.queue.editLabel"
            data-queued-editor
            @keydown="onEditorKeydown($event, item.id)"
          />
          <div class="t-queued-messages__edit-actions">
            <TButton variant="ghost" data-queued-cancel @click="cancelEdit()">{{ t.queue.cancel }}</TButton>
            <TButton variant="primary" :loading="saving" :disabled="!queuedText(draft)" data-queued-save @click="save(item.id)">
              {{ t.queue.save }}
            </TButton>
          </div>
        </template>
        <template v-else>
          <div class="t-queued-messages__body">
            <p v-if="item.text" class="t-queued-messages__text">{{ item.text }}</p>
            <ul
              v-if="item.attachments?.length"
              class="t-queued-messages__files"
              :aria-label="t.queue.attachments"
              data-queued-attachments
            >
              <li v-for="file in item.attachments" :key="file.id" class="t-queued-messages__file">
                <Icon :icon="file.mediaType.startsWith('image/') ? 'lucide:image' : 'lucide:paperclip'" aria-hidden="true" />
                <span>{{ attachmentName(file) }}</span>
              </li>
            </ul>
            <p v-if="lineText(item)" class="t-queued-messages__line" data-queued-line>{{ lineText(item) }}</p>
          </div>
          <div v-if="isChangeable(item)" class="t-queued-messages__actions">
            <TIconButton icon="lucide:pencil" :label="t.queue.edit" :disabled="busy(item.id)" data-queued-edit @click="startEdit(item)" />
            <TIconButton icon="lucide:x" :label="t.queue.remove" danger :disabled="busy(item.id)" data-queued-remove @click="queue.remove(item.id)" />
            <!-- The secondary (outlined) button in either theme: it is one action of the row, not the page's
                 main one, and it must not read as the composer's send button. -->
            <TButton
              variant="secondary"
              v-tooltip="sendHint(item) ? { text: sendHint(item)!, side: 'top' } : undefined"
              :icon="stopsTurn(item) ? 'lucide:square' : 'lucide:arrow-up'"
              :loading="busy(item.id)"
              :aria-description="sendHint(item)"
              :data-stops-turn="stopsTurn(item) || undefined"
              data-queued-send-now
              @click="queue.sendNow(item.id)"
            >
              {{ sendLabel(item) }}
            </TButton>
          </div>
        </template>
      </li>
    </ol>
  </section>
</template>

<style scoped>
.t-queued-messages {
  width: 100%;
  box-sizing: border-box;
  flex-shrink: 0;
  padding: 8px 10px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  text-align: left;
  font-size: 13px;
  line-height: 1.5;
}
.t-queued-messages__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 8px;
  padding: 2px 4px 6px;
  color: var(--tnzi-ai-text-secondary);
}
.t-queued-messages.is-folded .t-queued-messages__head {
  padding-bottom: 2px;
}
.t-queued-messages__head strong {
  color: var(--tnzi-ai-text);
  font-weight: 600;
}
.t-queued-messages__head-icon {
  flex-shrink: 0;
}
.t-queued-messages__toggle {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  margin-left: auto;
  padding: 2px 6px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
}
.t-queued-messages__toggle:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-queued-messages__toggle:focus-visible {
  outline: none;
  box-shadow: var(--tnzi-ai-focus-ring);
}
.t-queued-messages__list {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
  /* Many queued messages scroll among themselves: the composer's band must never outgrow the screen. */
  max-height: 32dvh;
  overflow-y: auto;
  overscroll-behavior: contain;
}
.t-queued-messages__list[data-more-below] {
  mask-image: linear-gradient(to bottom, #000 calc(100% - 28px), transparent);
}
.t-queued-messages__item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 8px;
  padding: 6px 4px 6px 10px;
  border-radius: 10px;
  background: var(--tnzi-ai-hover);
}
.t-queued-messages__body {
  flex: 1 1 200px;
  min-width: 0;
}
.t-queued-messages__text {
  display: -webkit-box;
  margin: 0;
  overflow: hidden;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
}
.t-queued-messages__line {
  margin: 2px 0 0;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
}
.t-queued-messages__item.is-failed .t-queued-messages__line {
  color: var(--tnzi-ai-danger);
}
.t-queued-messages__files {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  margin: 2px 0 0;
  padding: 0;
  list-style: none;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
}
.t-queued-messages__file {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  overflow-wrap: anywhere;
}
.t-queued-messages__item.is-with-agent .t-queued-messages__line {
  color: var(--tnzi-ai-text-secondary);
}
.t-queued-messages__item.is-held .t-queued-messages__text {
  color: var(--tnzi-ai-text-secondary);
}
.t-queued-messages__actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 2px;
  margin-left: auto;
}
.t-queued-messages__actions :deep(.t-button) {
  margin-left: 4px;
}
.t-queued-messages__editor {
  flex: 1 1 100%;
  box-sizing: border-box;
  min-height: 56px;
  max-height: 160px;
  padding: 6px 8px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: inherit;
  font: inherit;
  /* 16px on a phone: smaller text makes iOS zoom the page on focus. */
  font-size: 16px;
  resize: vertical;
}
.t-queued-messages__editor:focus {
  border-color: var(--tnzi-ai-focus-border);
  outline: none;
  box-shadow: var(--tnzi-ai-focus-ring);
}
.t-queued-messages__edit-actions {
  display: flex;
  flex: 1 1 100%;
  justify-content: flex-end;
  gap: 6px;
}
@media (min-width: 600px) {
  .t-queued-messages__editor {
    font-size: 13px;
  }
}
</style>
