<script setup lang="ts">
/**
 * @experimental
 * TThreadComposer - the package's single chat composer.
 *
 * Supports text + voice input, file attachments (paperclip / drag-drop /
 * paste-image), and declarative extra toolbar buttons via `composerActions`,
 * the answer to "the input bar should allow placing more buttons". All
 * built-ins are opt-in/opt-out so the composer stays as light or as rich as
 * the consumer needs. Extra clusters can still go in `#left` / `#right`, and
 * the send button is replaceable via `#send`.
 *
 * Two layouts, same component: `sticky` (default) pins it to the bottom of a
 * scrolling thread, and `:sticky="false"` renders it as a static block, which
 * is what TLandingPage embeds for its hero composer. Keeping one
 * implementation is the point - the landing page used to carry a near-copy
 * that had already started to drift.
 *
 * Fires `send(text, files)` on the built-in send button (when there is text
 * or at least one attachment) or on Enter without Shift (IME-safe).
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { formatFileSize } from '@tnzi/core'
import { useAiI18n, formatAiMessage } from '../../locale/index'
import { useVoiceInput } from '../../composables/useVoiceInput'
import { useComposerAttachments } from '../../composables/useComposerAttachments'
import type { RejectedAttachment } from '../../composables/useComposerAttachments'
import { useAutoGrowTextarea } from '../../composables/useAutoGrowTextarea'
import { fileIconForName } from '../../lib/fileIcon'
import type { ComposerAction } from './composer-types'
import { DEFAULT_COMPOSER_ACCEPT } from './composer-types'

const props = withDefaults(
  defineProps<{
    /** Two-way bound text value. */
    modelValue: string
    /** Placeholder text for the textarea. */
    placeholder?: string
    /** Disable sending regardless of content. */
    disabled?: boolean
    /** Number of textarea rows (auto-grows up to max-height). */
    rows?: number
    /** Declarative extra toolbar buttons. */
    composerActions?: ReadonlyArray<ComposerAction>
    /** Built-in voice (speech-to-text) mic button. Default true. */
    enableVoice?: boolean
    /** Built-in attachment button + drag/paste. Default false. */
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
    /** Render the send button in its ready state even with an empty input.
     *  Purely visual: sending still requires text or an attachment. */
    alwaysShowSend?: boolean
    /** Max width of the composer box in pixels. Unbounded when omitted. */
    maxWidth?: number
  }>(),
  {
    placeholder: 'Send a message',
    disabled: false,
    rows: 1,
    composerActions: () => [],
    enableVoice: true,
    enableAttachments: false,
    accept: DEFAULT_COMPOSER_ACCEPT,
    maxFileSize: 10 * 1024 * 1024,
    voiceLang: 'en-US',
    sticky: true,
    alwaysShowSend: false,
    maxWidth: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  send: [text: string, files: File[]]
  action: [id: string]
  /** One or more dropped/selected files were refused (currently only for
   *  exceeding `maxFileSize`). */
  'attachments-rejected': [rejected: readonly RejectedAttachment[]]
}>()

const t = useAiI18n()

const fileInputRef = ref<HTMLInputElement | null>(null)
const textareaRef = ref<HTMLTextAreaElement | null>(null)
useAutoGrowTextarea(textareaRef, computed(() => props.modelValue), 200)

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
  onReject: (list) => emit('attachments-rejected', list),
})

const rejectedMessage = computed(() =>
  rejected.value.length === 0
    ? ''
    : formatAiMessage(t.value.composer.filesTooLarge, {
        size: formatFileSize(props.maxFileSize),
        names: rejected.value.map((r) => r.file.name).join(', '),
      }),
)

let voiceBase = ''
const { isListening, isSupported, start: startVoice, stop: stopVoice } = useVoiceInput({
  lang: props.voiceLang,
  onResult: (transcript) => {
    emit('update:modelValue', voiceBase ? `${voiceBase} ${transcript}` : transcript)
  },
})

const canSend = computed(
  () => !props.disabled && (props.modelValue.trim().length > 0 || files.value.length > 0),
)

/* `alwaysShowSend` only changes how the button looks, never whether an empty
   message can leave the composer. */
const sendLooksReady = computed(() => canSend.value || (props.alwaysShowSend && !props.disabled))

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
}

function doSend(): void {
  if (!canSend.value) return
  emit('send', props.modelValue.trim(), [...files.value])
  clearFiles()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
    e.preventDefault()
    doSend()
  }
}

function toggleVoice(): void {
  if (isListening.value) {
    stopVoice()
  } else {
    voiceBase = props.modelValue.trim()
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
</script>

<template>
  <div class="t-thread-composer-wrap" :class="{ 'is-sticky': sticky }">
    <div
      class="t-thread-composer"
      :class="{ 'is-dragover': isDragOver }"
      :style="boxStyle"
      @drop="onDrop"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
    >
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
        ref="textareaRef"
        class="t-thread-composer__input"
        :value="modelValue"
        :placeholder="placeholder"
        :rows="rows"
        @input="onInput"
        @keydown="onKeydown"
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
          <button
            v-if="enableVoice && isSupported"
            type="button"
            class="t-thread-composer__act"
            :class="{ 'is-recording': isListening }"
            :title="isListening ? t.composer.stopRecording : t.composer.voiceInput"
            :aria-label="isListening ? t.composer.stopRecording : t.composer.voiceInput"
            @click="toggleVoice"
          >
            <Icon :icon="isListening ? 'lucide:square' : 'lucide:mic'" />
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
          <slot name="send" :can-send="canSend" :send="doSend">
            <button
              type="button"
              class="t-thread-composer__send"
              :class="{ 'is-ready': sendLooksReady }"
              :disabled="!canSend"
              :aria-label="t.composer.send"
              @click="doSend"
            >
              <Icon icon="lucide:arrow-up" />
            </button>
          </slot>
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
  </div>
</template>

<style scoped>
.t-thread-composer-wrap {
  padding-top: 24px;
  display: flex;
  justify-content: center;
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
    var(--tnzi-ai-bg, #f8f8f7) 24px
  );
}
.t-thread-composer {
  width: 100%;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border-strong, #d4d4d4);
  border-radius: var(--tnzi-ai-composer-radius, 16px);
  box-shadow: var(--tnzi-ai-composer-shadow, 0 1px 2px rgba(0, 0, 0, 0.04));
  padding: 14px 4px 10px;
  transition: border-color 150ms cubic-bezier(0.4, 0, 0.2, 1), box-shadow 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-thread-composer:focus-within {
  border-color: var(--tnzi-ai-accent, #0d9488);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
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

.t-thread-composer__input {
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  font-family: inherit;
  font-size: 15px;
  line-height: 22px;
  background: transparent;
  color: var(--tnzi-ai-text, #1a1a1a);
  caret-color: var(--tnzi-ai-accent, #0d9488);
  padding: 0 18px;
  min-height: 22px;
  max-height: 200px;
}
.t-thread-composer__input::placeholder {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-thread-composer__bar {
  display: flex;
  align-items: center;
  padding: 8px 10px 0;
}
.t-thread-composer__left,
.t-thread-composer__right {
  display: flex;
  align-items: center;
  gap: 2px;
}
.t-thread-composer__right {
  margin-left: auto;
}

/* toolbar action buttons (extra buttons + attach + voice) */
.t-thread-composer__act {
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
  font-size: 18px;
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
  color: var(--tnzi-ai-accent, #0d9488);
  background: var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
}
.t-thread-composer__act.is-recording {
  color: var(--tnzi-ai-on-accent);
  background: var(--tnzi-ai-accent, #0d9488);
  animation: t-composer-pulse 1.4s ease-in-out infinite;
}
@keyframes t-composer-pulse {
  0%, 100% { box-shadow: 0 0 0 0 var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.4)); }
  50% { box-shadow: 0 0 0 5px transparent; }
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
.t-thread-composer__send:disabled {
  cursor: not-allowed;
}

.t-thread-composer__file-input {
  display: none;
}
</style>
