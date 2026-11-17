<script setup lang="ts">
/**
 * @experimental
 * TThreadComposer - the package's single chat composer.
 *
 * Supports text + voice input, file attachments (paperclip / drag-drop /
 * paste-image), and declarative extra toolbar buttons via `composerActions`,
 * the answer to "the input bar should allow placing more buttons". All
 * built-ins are opt-in/opt-out so the composer stays as light or as rich as
 * the consumer needs. Extra clusters can still go in `#left` / `#right`.
 *
 * One primary button ends the toolbar, and what it does follows the composer
 * (`composerPrimaryAction`): stop while `running`, end the dictation while
 * listening, send when there is text or an attachment, the microphone when the
 * box is empty and speech input works here, otherwise a disabled send. It is
 * the same button in the same place in every state, so a state change never
 * moves another control. `#send` replaces it (and the Stop beside a
 * queue button) and gets the decided `action` with `send` / `queue` /
 * `canQueue` / `stop` / `toggleVoice`. `#notice` sits at the top of the
 * box, above the text (a usage-limit notice, say), so it is read before typing.
 *
 * Above the box there are two places. `#above` floats and takes no room (a
 * "scroll to the latest" button), so the box does not move when it comes and
 * goes. `#top` is in the layout, as wide as the box, 8px above it: content the
 * reader has to answer before going on (an approval, a question, a plan), kept
 * in view with the box by a sticky composer. It scrolls on its own past 40% of
 * the window's height, so a long plan cannot push the conversation out of
 * reach, and with no element in it the slot takes no room. `#above` floats
 * over `#top` when both are there.
 *
 * Two layouts, same component: `sticky` (default) pins it to the bottom of a
 * scrolling thread, and `:sticky="false"` renders it as a static block, which
 * is what TLandingPage embeds for its hero composer. Keeping one
 * implementation is the point - the landing page used to carry a near-copy
 * that had already started to drift.
 *
 * Fires `send(text, files)` on the built-in send button (when there is text
 * or at least one attachment) or on Enter without Shift (IME-safe).
 *
 * Queueing while a turn runs is opt-in (`queueWhileRunning`): with text or an
 * attachment in the box the primary button becomes `queue` and Stop moves to a
 * second button just before it; the button or Enter fires `queue(text, files)`
 * with the same arguments `send` would carry. The composer clears its
 * attachments as it emits either event; the text is the parent's (v-model) to
 * clear, as after `send`. With the box empty the primary button stays Stop.
 *
 * Exposes `addAttachments(files)` so a host can place files in the composer
 * from outside (a capture button, files handed over by the operating system).
 * They meet the same `accept` / `maxFileSize` checks and the same
 * `attachments-rejected` event as a drop, and wait in the composer for the
 * user's text. Null when attachments are off.
 *
 * Suggestion menus (slash commands, mentions): the `#suggestions` slot sits
 * just above the box, as wide as it, and gets a `ComposerSuggestionContext`
 * (text, caret, the token before the caret, `replaceRange` / `setText` /
 * `focus`). Every key press is emitted as `input-keydown` before the composer
 * acts on it; an open menu handles its navigation keys there and calls
 * `preventDefault()`, and the composer leaves the key alone (Enter then picks,
 * not sends). `inputAttrs` lands on the textarea for the combobox ARIA wiring
 * (`aria-controls`, `aria-activedescendant`, `aria-expanded`). The same
 * operations are exposed (`getSelection`, `replaceRange`, `setText`,
 * `inputElement`) for a menu rendered elsewhere.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { formatFileSize, isImeComposing } from '@tnzi/core/utils'
import { useAiI18n, formatAiMessage, type AiLocaleMessages } from '../../i18n/index'
import { useVoiceInput, type VoiceInputErrorCode } from '../../headless/useVoiceInput'
import { useComposerAttachments } from '../../headless/useComposerAttachments'
import type { AddAttachmentsResult, RejectedAttachment } from '../../headless/useComposerAttachments'
import { useAutoGrowTextarea } from '../../headless/useAutoGrowTextarea'
import { fileIconForName } from '../../utils/file-icon'
import type { ComposerAction } from './composer-types'
import { DEFAULT_COMPOSER_ACCEPT, DEFAULT_SEND_ICON } from './composer-types'
import { replaceTextRange, tokenBeforeCaret, type ComposerSuggestionContext } from './composer-suggestions'
import { canQueueContent, composerPrimaryAction } from './composer-primary-action'

const props = withDefaults(
  defineProps<{
    /** Two-way bound text value. */
    modelValue: string
    /** Placeholder text for the textarea. */
    placeholder?: string
    /** Disable sending regardless of content. */
    disabled?: boolean
    /**
     * A turn is running: the primary button becomes stop (emitting `stop`) and
     * neither the button nor Enter sends. Text in the box stays there, unless
     * `queueWhileRunning` lets it be queued.
     */
    running?: boolean
    /**
     * While `running`, a message with text or attachments can be queued: the
     * primary button (and Enter) emits `queue(text, files)` instead of doing
     * nothing, and Stop is a second button beside it. What happens to a queued
     * message (keeping it, sending it once the turn ends) is the parent's.
     * Default false: while running the primary button is Stop, as without it.
     */
    queueWhileRunning?: boolean
    /**
     * With `queueWhileRunning`: whether a queued message may carry attachments. Default true. False
     * for an app's own queue that holds words only: with attachments in the box the primary button
     * stays Stop, and they go once the turn has ended.
     */
    queueAttachments?: boolean
    /** Number of textarea rows (auto-grows up to max-height). */
    rows?: number
    /** Declarative extra toolbar buttons. */
    composerActions?: ReadonlyArray<ComposerAction>
    /** Built-in voice (speech-to-text): the primary button is the microphone
     *  while the box is empty. Default true. Never shown where the browser or
     *  the page cannot use speech input. */
    enableVoice?: boolean
    /** Built-in attachment button + drag/paste. Default false. When off,
     *  paste and drop are inert too - not just the button hidden - so no
     *  chip can appear that the transport would then have to drop. */
    enableAttachments?: boolean
    /** Accepted file types. */
    accept?: string
    /** Max attachment size in bytes. */
    maxFileSize?: number
    /** Voice recognition language (BCP-47). */
    voiceLang?: string
    /** Pin to the bottom of the scrolling thread. Set false for a static
     *  block (landing hero). */
    sticky?: boolean
    /** Render the send button in its ready state even with an empty input,
     *  when the button is send (an empty box with speech input shows the
     *  microphone instead). Purely visual: sending still requires text or an
     *  attachment. */
    alwaysShowSend?: boolean
    /** Iconify name of the primary button while it sends (a brand's own mark).
     *  Stop, voice and end-dictation keep theirs. Default `lucide:arrow-up`. */
    sendIcon?: string
    /** Max width of the composer box in pixels. Unbounded when omitted. */
    maxWidth?: number
    /**
     * Phone layout: tighter padding, 16px input text (iOS zooms the page into
     * a smaller field), and room for the home indicator below a sticky
     * composer. A sticky (in-conversation) composer is also one line tall
     * whatever `rows` says (it still grows with the text up to its maximum);
     * a static one (the landing hero) keeps its `rows`, so the first screen
     * still offers a two-line box. `TChatApp` sets it below its narrow-screen
     * breakpoint.
     */
    compact?: boolean
    /**
     * One row: the left cluster, the text and the right cluster side by side in
     * a pill-shaped box (22px corners), for a composer placed in a card or a
     * header rather than under a conversation. The text still grows downwards
     * as it gets longer, the clusters staying on its last line; attachments
     * and refusal notices take a line of their own. 16px text on a phone.
     */
    inline?: boolean
    /**
     * Attributes for the textarea, bound before the composer's own (which
     * win): the ARIA a suggestion menu needs (`role="combobox"`,
     * `aria-controls`, `aria-expanded`, `aria-activedescendant`), `data-*`.
     */
    inputAttrs?: Readonly<Record<string, string | number | boolean | undefined>>
  }>(),
  {
    placeholder: 'Send a message',
    disabled: false,
    running: false,
    queueWhileRunning: false,
    queueAttachments: true,
    rows: 1,
    composerActions: () => [],
    enableVoice: true,
    enableAttachments: false,
    accept: DEFAULT_COMPOSER_ACCEPT,
    maxFileSize: 10 * 1024 * 1024,
    voiceLang: 'en-US',
    sticky: true,
    alwaysShowSend: false,
    sendIcon: DEFAULT_SEND_ICON,
    maxWidth: undefined,
    compact: false,
    inline: false,
    inputAttrs: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  send: [text: string, files: File[]]
  /** Stop was pressed while `running` (the primary button, or the one beside `queue`). */
  stop: []
  /**
   * A message was queued while `running` (`queueWhileRunning`): the primary
   * button or Enter. Same arguments as `send`; the attachments are cleared.
   */
  queue: [text: string, files: File[]]
  action: [id: string]
  /** One or more files were refused: over `maxFileSize`, or not matched by
   *  `accept`. Fires for every way a file arrives (picker, drop, paste,
   *  `addAttachments`). */
  'attachments-rejected': [rejected: readonly RejectedAttachment[]]
  /**
   * A key pressed in the input, before the composer acts on it. Call
   * `preventDefault()` to keep the composer from handling it (an open
   * suggestion menu taking Enter, Tab, the arrows and Escape).
   */
  'input-keydown': [event: KeyboardEvent]
}>()

const t = useAiI18n()

const fileInputRef = ref<HTMLInputElement | null>(null)
const textareaRef = ref<HTMLTextAreaElement | null>(null)
const resizeInput = useAutoGrowTextarea(textareaRef, computed(() => props.modelValue), 200)
const effectiveRows = computed(() => (props.compact && props.sticky ? 1 : props.rows))
/* The auto-grown height is set inline from the content's height at `rows`;
   switching layout (a phone rotated across the breakpoint) re-measures. */
watch(effectiveRows, () => void nextTick(resizeInput))

const {
  files,
  rejected,
  isDragOver,
  addFiles,
  removeFile,
  clearFiles,
  clearRejected,
  getPreviewUrl,
  isImageFile,
  onPaste,
  onDrop,
  onDragOver,
  onDragLeave,
} = useComposerAttachments({
  maxFileSize: props.maxFileSize,
  accept: () => props.accept,
  enabled: () => props.enableAttachments,
  onReject: (list) => emit('attachments-rejected', list),
})

// Turning attachments off with chips already collected must not leave them
// to be sent on the next Enter.
watch(
  () => props.enableAttachments,
  (enabled) => {
    if (!enabled) clearFiles()
  },
)

/* One sentence per reason: a single "not attached" line naming every file
   would leave the user guessing which ones were too big and which ones the
   composer does not take. */
const rejectedMessage = computed(() => {
  const namesFor = (reason: RejectedAttachment['reason']) =>
    rejected.value.filter((r) => r.reason === reason).map((r) => r.file.name).join(', ')
  const tooLarge = namesFor('too-large')
  const unsupported = namesFor('unsupported-type')
  return [
    tooLarge
      ? formatAiMessage(t.value.composer.filesTooLarge, { size: formatFileSize(props.maxFileSize), names: tooLarge })
      : '',
    unsupported ? formatAiMessage(t.value.composer.filesNotAccepted, { names: unsupported }) : '',
  ].filter(Boolean).join(' ')
})

let voiceBase = ''
/* Off once the message is sent mid-dictation: stopping the recogniser still
   delivers what it heard last, and that must not refill the emptied box. A
   dictation the user ends with the button keeps its last words. */
let acceptTranscripts = false
const {
  isListening,
  isSupported,
  error: voiceError,
  start: startVoice,
  stop: stopVoice,
} = useVoiceInput({
  // A getter: the language can change while the composer is mounted (the
  // product switched its interface language).
  lang: () => props.voiceLang,
  onResult: (transcript) => {
    if (!acceptTranscripts) return
    emit('update:modelValue', voiceBase ? `${voiceBase} ${transcript}` : transcript)
  },
})

/* Every way the recogniser can stop has its own sentence: what happened and
   what to do about it. Unsaid, the button just flips back and reads as broken -
   which is what every failure other than a refused microphone used to do. The
   notice is dismissed per attempt: pressing the mic again shows it again while
   the cause is still there. */
const VOICE_ERROR_KEYS = {
  'mic-blocked': 'voiceMicBlocked',
  'mic-not-granted': 'voiceMicNotGranted',
  'service-not-allowed': 'voiceServiceNotAllowed',
  'no-microphone': 'voiceNoMicrophone',
  network: 'voiceNetwork',
  'no-speech': 'voiceNoSpeech',
  'language-not-supported': 'voiceLanguageNotSupported',
  aborted: 'voiceAborted',
  unavailable: 'voiceUnavailable',
} as const satisfies Record<VoiceInputErrorCode, keyof AiLocaleMessages['composer']>

const voiceNoticeDismissed = ref(false)
const voiceNotice = computed(() =>
  voiceError.value != null && !voiceNoticeDismissed.value ? t.value.composer[VOICE_ERROR_KEYS[voiceError.value]] : '',
)

const hasContent = computed(() => props.modelValue.trim().length > 0 || files.value.length > 0)
const canSend = computed(() => !props.disabled && !props.running && hasContent.value)
const canQueue = computed(
  () => props.running && !props.disabled && canQueueContent({
    running: props.running,
    listening: false,
    hasContent: hasContent.value,
    voiceAvailable: false,
    disabled: props.disabled,
    queueWhileRunning: props.queueWhileRunning,
    hasFiles: files.value.length > 0,
    queueAttachments: props.queueAttachments,
  }),
)

const primary = computed(() =>
  composerPrimaryAction({
    running: props.running,
    listening: isListening.value,
    hasContent: hasContent.value,
    voiceAvailable: props.enableVoice && isSupported.value,
    disabled: props.disabled,
    queueWhileRunning: props.queueWhileRunning,
    hasFiles: files.value.length > 0,
    queueAttachments: props.queueAttachments,
  }),
)

/* Stop has to stay reachable while the primary button queues. */
const showsSideStop = computed(() => props.running && primary.value.kind !== 'stop')

/* `alwaysShowSend` only changes how the button looks, never whether an empty
   message can leave the composer. */
const sendLooksReady = computed(
  () => (primary.value.kind === 'queue' && primary.value.enabled) ||
    (primary.value.kind === 'send' && (primary.value.enabled || (props.alwaysShowSend && !props.disabled && !props.running))),
)

const primaryIcon = computed(() => {
  switch (primary.value.kind) {
    case 'stop':
    case 'end-dictation':
      return 'lucide:square'
    case 'voice':
      return 'lucide:mic'
    default:
      return props.sendIcon || DEFAULT_SEND_ICON
  }
})

const primaryLabel = computed(() => {
  switch (primary.value.kind) {
    case 'stop':
      return t.value.chat.stop
    case 'queue':
      return t.value.composer.queue
    case 'end-dictation':
      return t.value.composer.stopRecording
    case 'voice':
      return t.value.composer.voiceInput
    default:
      return t.value.composer.send
  }
})

function onPrimary(): void {
  switch (primary.value.kind) {
    case 'stop':
      emit('stop')
      return
    case 'queue':
      doQueue()
      // The click left focus on the button: the next message is typed into the box, as
      // after Enter, not into a button that drops the keystrokes.
      focus()
      return
    case 'end-dictation':
    case 'voice':
      toggleVoice()
      return
    default:
      doSend()
  }
}

/* Turning speech input off, or a turn starting (from elsewhere than this
   composer), ends a dictation the button could no longer reach. */
watch(
  () => props.running || !props.enableVoice,
  (stopNow) => {
    if (stopNow && isListening.value) stopVoice()
  },
)

const boxStyle = computed(() =>
  props.maxWidth == null ? undefined : { maxWidth: `${props.maxWidth}px` },
)

const leftActions = computed(() =>
  props.composerActions
    .filter((a) => (a.side ?? 'left') === 'left')
    .slice()
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
)
const rightActions = computed(() =>
  props.composerActions
    .filter((a) => a.side === 'right')
    .slice()
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
)

function onInput(e: Event): void {
  emit('update:modelValue', (e.target as HTMLTextAreaElement).value)
  syncSelection()
}

/* Where the caret is. The textarea has no reactive selection, so it is read
   back after everything that can move it (typing, clicks, arrow keys, a
   selection, focus); `selectionchange` does not fire on textareas everywhere. */
const selection = ref({ start: 0, end: 0 })
function syncSelection(): void {
  const el = textareaRef.value
  if (el) selection.value = { start: el.selectionStart, end: el.selectionEnd }
}

function getSelection(): { start: number; end: number } {
  syncSelection()
  return { ...selection.value }
}

/* A text the parent sets goes through v-model, so the caret is placed after
   the DOM has the new value. */
function placeCaret(caret: number): void {
  void nextTick(() => {
    const el = textareaRef.value
    if (!el) return
    el.focus()
    el.setSelectionRange(caret, caret)
    syncSelection()
    resizeInput()
  })
}

function setText(text: string, caret?: number): void {
  emit('update:modelValue', text)
  placeCaret(caret == null ? text.length : Math.min(Math.max(caret, 0), text.length))
}

function replaceRange(start: number, end: number, insert: string): void {
  const next = replaceTextRange(props.modelValue, start, end, insert)
  setText(next.text, next.caret)
}

const suggestionContext = computed<ComposerSuggestionContext>(() => ({
  text: props.modelValue,
  selectionStart: selection.value.start,
  selectionEnd: selection.value.end,
  token: tokenBeforeCaret(props.modelValue, selection.value.start, selection.value.end),
  replaceRange,
  setText,
  focus: () => textareaRef.value?.focus(),
}))

function doSend(): void {
  if (!canSend.value) return
  if (isListening.value) {
    acceptTranscripts = false
    stopVoice()
  }
  emit('send', props.modelValue.trim(), [...files.value])
  clearFiles()
}

function doQueue(): void {
  if (!canQueue.value) return
  emit('queue', props.modelValue.trim(), [...files.value])
  clearFiles()
}

/* `isImeComposing` rather than `isComposing` alone: Safari fires the Enter
   that commits an IME candidate with `isComposing` false and keyCode 229. */
function onKeydown(e: KeyboardEvent): void {
  emit('input-keydown', e)
  if (e.defaultPrevented) return
  if (e.key === 'Enter' && !e.shiftKey && !isImeComposing(e)) {
    e.preventDefault()
    if (canQueue.value) doQueue()
    else doSend()
  }
}

function toggleVoice(): void {
  if (isListening.value) {
    stopVoice()
  } else {
    voiceBase = props.modelValue.trim()
    acceptTranscripts = true
    voiceNoticeDismissed.value = false
    startVoice()
  }
}

function openFile(): void {
  fileInputRef.value?.click()
}

function onFileChange(e: Event): void {
  const target = e.target as HTMLInputElement
  if (target.files) {
    addFiles(target.files)
    target.value = ''
  }
}

/**
 * Focus the textarea, caret at the end.
 *
 * The caret placement is the point. Suggestion prompts are deliberately
 * half-written ("Help me plan my day around ") so the user continues the
 * sentence; dropping the caret at position 0 - or leaving focus on the chip -
 * makes them click into the box and press End before they can type.
 */
function focus(): void {
  const el = textareaRef.value
  if (!el) return
  el.focus()
  el.setSelectionRange(el.value.length, el.value.length)
}

/**
 * Place files in the composer from outside (a capture button, files handed
 * over by the operating system). They go through the same checks and the same
 * `attachments-rejected` event as a drop, and wait for the user's text; nothing
 * is sent. Returns what was added and what was refused, or null when
 * attachments are off (`enableAttachments` false).
 */
function addAttachments(incoming: FileList | readonly File[]): AddAttachmentsResult | null {
  return addFiles(incoming)
}

defineExpose({ focus, addAttachments, getSelection, replaceRange, setText, inputElement: textareaRef })
</script>

<template>
  <div
    class="t-thread-composer-wrap"
    :class="{ 'is-sticky': sticky, 'has-footer': !!$slots.footer, 'is-compact': compact, 'is-inline': inline }"
  >
    <!-- Floats just above the box (a "scroll to the latest" button); takes no
         room, so the box does not move when it comes and goes. -->
    <div v-if="$slots.above" class="t-thread-composer__above">
      <slot name="above" />
    </div>
    <!-- In the layout, just above the box and as wide as it: what the reader
         must see before typing (a confirmation, a question, a plan to
         approve). Inside the sticky wrapper, so it stays in view with the box. -->
    <div v-if="$slots.top" class="t-thread-composer__top t-ai-scroll" :style="boxStyle">
      <slot name="top" />
    </div>
    <div
      class="t-thread-composer"
      :class="{ 'is-dragover': isDragOver }"
      :style="boxStyle"
      @drop="onDrop"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
    >
      <!-- A suggestion menu (slash commands, mentions), just above the box and as wide as it. -->
      <div v-if="$slots.suggestions" class="t-thread-composer__suggestions">
        <slot name="suggestions" v-bind="suggestionContext" />
      </div>
      <slot name="notice" />
      <!-- Oversized files are refused; say so instead of dropping them silently. -->
      <div v-if="rejectedMessage" class="t-thread-composer__reject" role="status">
        <Icon icon="lucide:triangle-alert" class="t-thread-composer__reject-icon" />
        <span class="t-thread-composer__reject-text">{{ rejectedMessage }}</span>
        <button
          type="button"
          class="t-thread-composer__chip-x"
          :aria-label="t.composer.dismiss"
          @click="clearRejected"
        >
          <Icon icon="lucide:x" />
        </button>
      </div>

      <div v-if="voiceNotice" class="t-thread-composer__reject t-thread-composer__reject--voice" role="alert">
        <Icon icon="lucide:triangle-alert" class="t-thread-composer__reject-icon" />
        <span class="t-thread-composer__reject-text">{{ voiceNotice }}</span>
        <button
          type="button"
          class="t-thread-composer__chip-x"
          :aria-label="t.composer.dismiss"
          @click="voiceNoticeDismissed = true"
        >
          <Icon icon="lucide:x" />
        </button>
      </div>

      <!-- attachment chips -->
      <div v-if="files.length > 0" class="t-thread-composer__files">
        <div v-for="(f, i) in files" :key="`${f.name}:${f.size}:${f.lastModified}:${i}`" class="t-thread-composer__chip">
          <img
            v-if="isImageFile(f)"
            :src="getPreviewUrl(f)"
            :alt="f.name"
            class="t-thread-composer__chip-img"
          />
          <Icon v-else :icon="fileIconForName(f.name)" class="t-thread-composer__chip-icon" />
          <span class="t-thread-composer__chip-name">{{ f.name }}</span>
          <span class="t-thread-composer__chip-size">{{ formatFileSize(f.size) }}</span>
          <button
            type="button"
            class="t-thread-composer__chip-x"
            :aria-label="t.composer.removeAttachment"
            @click="removeFile(i)"
          >
            <Icon icon="lucide:x" />
          </button>
        </div>
      </div>

      <textarea
        v-bind="inputAttrs"
        ref="textareaRef"
        class="t-thread-composer__input"
        :value="modelValue"
        :placeholder="isListening ? t.composer.listening : placeholder"
        :rows="effectiveRows"
        @input="onInput"
        @keydown="onKeydown"
        @keyup="syncSelection"
        @click="syncSelection"
        @select="syncSelection"
        @focus="syncSelection"
        @paste="onPaste"
      />
      <div class="t-thread-composer__bar">
        <div class="t-thread-composer__left">
          <slot name="left" />
          <button
            v-for="a in leftActions"
            :key="a.id"
            type="button"
            class="t-thread-composer__act"
            :class="{ 'is-active': a.active }"
            :disabled="a.disabled"
            :title="a.tooltip"
            :aria-label="a.tooltip || a.id"
            @click="emit('action', a.id)"
          >
            <Icon :icon="a.icon" />
          </button>
          <button
            v-if="enableAttachments"
            type="button"
            class="t-thread-composer__act"
            :title="t.composer.attach"
            :aria-label="t.composer.attach"
            @click="openFile"
          >
            <Icon icon="lucide:paperclip" />
          </button>
        </div>
        <div class="t-thread-composer__right">
          <button
            v-for="a in rightActions"
            :key="a.id"
            type="button"
            class="t-thread-composer__act"
            :class="{ 'is-active': a.active }"
            :disabled="a.disabled"
            :title="a.tooltip"
            :aria-label="a.tooltip || a.id"
            @click="emit('action', a.id)"
          >
            <Icon :icon="a.icon" />
          </button>
          <slot name="right" />
          <slot
            name="send"
            :can-send="canSend"
            :send="doSend"
            :action="primary"
            :can-queue="canQueue"
            :queue="doQueue"
            :stop="() => emit('stop')"
            :toggle-voice="toggleVoice"
          >
            <!-- Queueing while a turn runs: Stop moves here, before the primary
                 button, so the turn can still be ended. -->
            <button
              v-if="showsSideStop"
              type="button"
              class="t-thread-composer__send is-stop t-thread-composer__side-stop"
              data-action="stop"
              :title="t.chat.stop"
              :aria-label="t.chat.stop"
              @click="emit('stop')"
            >
              <Icon icon="lucide:square" />
            </button>
            <!-- One button for stop / queue / end dictation / send / microphone, so
                 the toolbar keeps its shape whatever state the composer is in. -->
            <button
              type="button"
              class="t-thread-composer__send"
              :class="{
                'is-ready': sendLooksReady,
                'is-stop': primary.kind === 'stop',
                'is-queue': primary.kind === 'queue',
                'is-voice': primary.kind === 'voice',
                'is-recording': primary.kind === 'end-dictation',
              }"
              :data-action="primary.kind"
              :disabled="!primary.enabled"
              :title="primaryLabel"
              :aria-label="primaryLabel"
              @click="onPrimary"
            >
              <Icon :icon="primaryIcon" />
            </button>
          </slot>
          <!-- Says that dictation is on to a screen reader; the button shows it. -->
          <span class="t-thread-composer__sr-only" role="status" aria-live="polite">{{
            isListening ? t.composer.listening : ''
          }}</span>
        </div>
      </div>

      <input
        ref="fileInputRef"
        type="file"
        class="t-thread-composer__file-input"
        :accept="accept"
        multiple
        @change="onFileChange"
      />
    </div>

    <!-- Fine print (model disclaimer, token count). Inside the sticky wrapper
         on purpose: a disclaimer that scrolls away with the messages is not a
         disclaimer. It also inherits the wrapper's fade, so messages dissolve
         behind it the same way they do behind the box. -->
    <div v-if="$slots.footer" class="t-thread-composer__foot" :style="boxStyle">
      <slot name="footer" />
    </div>
  </div>
</template>

<style scoped>
/* Column, not row: the box and the optional footer stack. `align-items:center`
   keeps a max-width'd box centred exactly as `justify-content:center` did when
   the box was the only child. */
.t-thread-composer-wrap {
  position: relative;
  padding-top: 16px;
  display: flex;
  flex-direction: column;
  align-items: center;
}
.t-thread-composer__above {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 100%;
  display: flex;
  justify-content: center;
  pointer-events: none;
}
.t-thread-composer__above > * { pointer-events: auto; }
/* `#top`: in the flow, the box's width, 8px above it. A slot whose content
   rendered nothing (a `v-if` that is false) leaves the element empty, and the
   empty element takes no room, so it adds no gap. Past 40% of the window it
   scrolls on its own: the wrapper is sticky, and content taller than the view
   would leave the conversation behind it out of reach. */
.t-thread-composer__top {
  width: 100%;
  margin-bottom: 8px;
  max-height: 40vh;
  max-height: 40dvh;
  overflow-y: auto;
  overscroll-behavior: contain;
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-thread-composer__top:not(:has(> *)) { display: none; }
.t-thread-composer__foot {
  width: 100%;
  padding: 8px 4px 10px;
  text-align: center;
  font-size: 12px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
/* Sticky variant: pinned to the bottom of a scrolling thread, with a fade so
   messages dissolve into the canvas behind it. The landing hero opts out
   (`:sticky="false"`) and renders as a plain centred block. */
.t-thread-composer-wrap.is-sticky {
  margin-top: auto;
  position: sticky;
  bottom: 0;
  background: linear-gradient(
    to bottom,
    transparent 0%,
    var(--tnzi-ai-bg, #f8f8f7) 16px
  );
  /* The wrapper owns the gap below itself, because `bottom: 0` pins it to the
     scroller's PADDING box, not its visible edge. With the padding on the
     scroller instead, messages scrolled through that strip and showed under
     the composer (measured: a 24px uncovered band). Scrollers hosting a
     sticky composer must therefore set `padding-bottom: 0`.
     The gap is kept small: the block belongs to the bottom edge. It was 24px
     under the box plus 10px under the fine print, a 34px empty band between
     the disclaimer and the window edge. */
  padding-bottom: 12px;
}
/* With fine print the print carries the box-to-edge spacing: 6px above it,
   8px between it and the edge. */
.t-thread-composer-wrap.is-sticky.has-footer { padding-bottom: 8px; }
/* Compact (phone): the space above the box and inside it was sized for a
   desktop window and costs a phone several lines of conversation. Below a
   sticky box the gap includes the home indicator's safe area, so the box is
   not drawn under it. */
.t-thread-composer-wrap.is-compact { padding-top: 8px; }
.t-thread-composer-wrap.is-sticky.is-compact,
.t-thread-composer-wrap.is-sticky.is-compact.has-footer {
  padding-bottom: calc(8px + env(safe-area-inset-bottom, 0px));
}
.t-thread-composer-wrap.is-compact .t-thread-composer { padding: 8px 2px 4px; }
.t-thread-composer-wrap.is-compact .t-thread-composer__input {
  padding: 0 14px;
  font-size: 16px;
  line-height: 24px;
}
.t-thread-composer-wrap.is-compact .t-thread-composer__bar { padding: 2px 4px 0; }
/* A phone is driven by a finger: the toolbar's buttons grow to 40px targets
   while the spacing around them shrinks, so the box is no taller than before. */
.t-thread-composer-wrap.is-compact .t-thread-composer__act,
.t-thread-composer-wrap.is-compact .t-thread-composer__send {
  width: 40px;
  height: 40px;
}
.t-thread-composer-wrap.is-sticky .t-thread-composer__foot { padding: 6px 4px 0; }
/* Inline: one row in a pill. The toolbar steps aside (`display: contents`) so
   its two clusters are the box's own flex items, ordered around the text and
   kept on its last line (`align-items: flex-end`). Notices and attachment
   chips take a full line each, above the row. Declared after the compact
   rules, at the same weight or more, so an inline box on a phone stays one row. */
.t-thread-composer-wrap.is-inline { padding-top: 0; }
.t-thread-composer-wrap.is-inline .t-thread-composer {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  padding: 3px 5px;
  border-radius: 22px;
}
.t-thread-composer-wrap.is-inline .t-thread-composer__bar { display: contents; }
/* Everything else in the box (the `#notice` slot's content too) is a line of
   its own. The suggestion menu is positioned and the file input not drawn, so
   neither takes part. */
.t-thread-composer-wrap.is-inline .t-thread-composer > :deep(:not(.t-thread-composer__bar, .t-thread-composer__input)) {
  flex: 1 1 100%;
}
.t-thread-composer-wrap.is-inline .t-thread-composer__left { order: 1; }
.t-thread-composer-wrap.is-inline .t-thread-composer__input {
  order: 2;
  flex: 1 1 0;
  width: auto;
  min-width: 0;
  padding: 6px;
}
.t-thread-composer-wrap.is-inline .t-thread-composer__right {
  order: 3;
  margin-left: 0;
}
.t-thread-composer-wrap.is-inline .t-thread-composer__files,
.t-thread-composer-wrap.is-inline .t-thread-composer__reject {
  flex: 1 1 100%;
  margin: 4px 6px 6px;
  padding-inline: 0;
}
.t-thread-composer-wrap.is-inline .t-thread-composer__reject { padding: 6px 6px 6px 10px; }
@media (max-width: 640px) {
  .t-thread-composer-wrap.is-inline .t-thread-composer__input { font-size: 16px; }
}
.t-thread-composer {
  position: relative;
  width: 100%;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border-strong, #d4d4d4);
  border-radius: var(--tnzi-ai-composer-radius, 16px);
  box-shadow: var(--tnzi-ai-composer-shadow, 0 1px 2px rgba(0, 0, 0, 0.04));
  padding: 10px 4px 6px;
  transition: border-color 150ms cubic-bezier(0.4, 0, 0.2, 1), box-shadow 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-thread-composer:focus-within {
  border-color: var(--tnzi-ai-focus-border);
  box-shadow: var(--tnzi-ai-focus-ring);
}
.t-thread-composer.is-dragover {
  border-color: var(--tnzi-ai-accent, #0d9488);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
}

/* rejected-attachment notice */
.t-thread-composer__reject {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 14px 8px;
  padding: 6px 6px 6px 10px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-warning) 32%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-warning) 10%, transparent);
  border-radius: 8px;
  font-size: 12px;
  color: var(--tnzi-ai-text);
}
.t-thread-composer__reject-icon {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-warning);
}
.t-thread-composer__reject-text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* A voice notice says what to do, so it wraps instead of being cut off. */
.t-thread-composer__reject--voice { align-items: flex-start; }
.t-thread-composer__reject--voice .t-thread-composer__reject-icon { margin-top: 2px; }
.t-thread-composer__reject--voice .t-thread-composer__reject-text {
  white-space: normal;
  line-height: 1.5;
}

/* attachment chips */
.t-thread-composer__files {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 0 14px 8px;
}
.t-thread-composer__chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  max-width: 220px;
  padding: 4px 6px 4px 8px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  border-radius: 8px;
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  font-size: 12px;
  color: var(--tnzi-ai-text, #34322d);
}
.t-thread-composer__chip-img {
  width: 28px;
  height: 28px;
  border-radius: 4px;
  object-fit: cover;
}
.t-thread-composer__chip-icon {
  font-size: 16px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
}
.t-thread-composer__chip-name {
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-thread-composer__chip-size {
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  font-size: 11px;
}
.t-thread-composer__chip-x {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  border-radius: 4px;
  cursor: pointer;
}
.t-thread-composer__chip-x:hover {
  background: var(--tnzi-ai-press, rgba(0, 0, 0, 0.06));
  color: var(--tnzi-ai-text, #34322d);
}

/* Anchored to the box: a menu the slot renders opens upwards from its top edge. */
.t-thread-composer__suggestions {
  position: absolute;
  left: 0;
  right: 0;
  bottom: calc(100% + 6px);
  z-index: var(--tnzi-ai-z-popover);
}
.t-thread-composer__suggestions:empty { display: none; }
.t-thread-composer__input {
  /* Block, not the textarea's default inline: inline it sits on a text
     baseline and leaves a descender gap of a few pixels under itself. */
  display: block;
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  font-family: inherit;
  font-size: 15px;
  line-height: 22px;
  background: transparent;
  color: var(--tnzi-ai-text, #1a1a1a);
  caret-color: var(--tnzi-ai-accent-text);
  padding: 0 14px;
  min-height: 22px;
  max-height: 200px;
}
.t-thread-composer__input::placeholder {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
/* 10px between the text and the toolbar; a phone keeps its tighter bar (compact). */
.t-thread-composer__bar {
  display: flex;
  align-items: center;
  padding: 10px 6px 0;
}
.t-thread-composer__left,
.t-thread-composer__right {
  display: flex;
  align-items: center;
  gap: 2px;
}
/* A narrowing box gives way on the left: pickers there shrink (down to their
   icon, when they cut their own label), while the right cluster (model, send)
   and every icon button keep their size. */
.t-thread-composer__left {
  flex: 0 1 auto;
  min-width: 0;
}
.t-thread-composer__right {
  flex: none;
  margin-left: auto;
}

/* toolbar action buttons (extra buttons + attach): 16px icons, the size of a
   picker's icon beside them */
.t-thread-composer__act {
  flex: none;
  width: 32px;
  height: 32px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  transition: background 150ms cubic-bezier(0.4, 0, 0.2, 1), color 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-thread-composer__act:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-thread-composer__act:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.t-thread-composer__act.is-active {
  color: var(--tnzi-ai-accent-text);
  background: var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
}

.t-thread-composer__send {
  width: 32px;
  height: 32px;
  border: none;
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  margin-left: 4px;
  transition:
    background 150ms cubic-bezier(0.4, 0, 0.2, 1),
    color 150ms cubic-bezier(0.4, 0, 0.2, 1),
    box-shadow 150ms cubic-bezier(0.4, 0, 0.2, 1),
    transform 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-thread-composer__send.is-ready {
  background: var(--tnzi-ai-accent, #0d9488);
  color: var(--tnzi-ai-on-accent);
  box-shadow: 0 8px 20px var(--tnzi-ai-accent-glow, rgba(13, 148, 136, 0.32));
}
.t-thread-composer__send.is-ready:hover {
  transform: translateY(-1px);
}
/* Empty box with speech input: the microphone, quiet like the toolbar's
   other buttons until hovered. */
.t-thread-composer__send.is-voice {
  background: transparent;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  font-size: 18px;
}
.t-thread-composer__send.is-voice:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text, #1a1a1a);
}
/* Dictating: filled with the accent and pulsing, so it reads as "recording"
   and not as the neutral stop of a running turn. */
.t-thread-composer__send.is-recording {
  color: var(--tnzi-ai-on-accent);
  background: var(--tnzi-ai-accent, #0d9488);
  animation: t-composer-pulse 1.4s ease-in-out infinite;
}
@keyframes t-composer-pulse {
  0%, 100% { box-shadow: 0 0 0 0 var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.4)); }
  50% { box-shadow: 0 0 0 5px transparent; }
}
@media (prefers-reduced-motion: reduce) {
  .t-thread-composer__send.is-recording {
    animation: none;
    box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.4));
  }
}
/* A running turn: the stop square on the text colour. */
.t-thread-composer__send.is-stop {
  background: var(--tnzi-ai-text, #1a1a1a);
  color: var(--tnzi-ai-surface, #ffffff);
  font-size: 12px;
}
.t-thread-composer__send.is-stop:hover { opacity: 0.85; }
.t-thread-composer__send:disabled {
  cursor: not-allowed;
}
.t-thread-composer__sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
  border: 0;
}

.t-thread-composer__file-input {
  display: none;
}
</style>
