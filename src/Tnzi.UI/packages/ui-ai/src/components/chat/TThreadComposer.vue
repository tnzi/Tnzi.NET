<script setup lang="ts">
/**
 * @experimental
 * TThreadComposer — Manus-style sticky-bottom composer for in-progress
 * conversations.
 *
 * Supports text + voice input, file attachments (paperclip / drag-drop /
 * paste-image), and declarative extra toolbar buttons via `composerActions`
 * — the answer to "the input bar should allow placing more buttons". All
 * built-ins are opt-in/opt-out so the composer stays as light or as rich as
 * the consumer needs. Extra clusters can still go in `#left` / `#right`, and
 * the send button is replaceable via `#send`.
 *
 * Fires `send(text, files)` on the built-in send button (when there is text
 * or at least one attachment) or on Enter without Shift (IME-safe).
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { formatFileSize } from '@tnzi/core'
import { useVoiceInput } from '../../composables/useVoiceInput'
import { useComposerAttachments } from '../../composables/useComposerAttachments'
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
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  send: [text: string, files: File[]]
  action: [id: string]
}>()

const fileInputRef = ref<HTMLInputElement | null>(null)
const textareaRef = ref<HTMLTextAreaElement | null>(null)
useAutoGrowTextarea(textareaRef, computed(() => props.modelValue), 200)

const {
  files,
  isDragOver,
  addFiles,
  removeFile,
  clearFiles,
  getPreviewUrl,
  isImageFile,
  onPaste,
  onDrop,
  onDragOver,
  onDragLeave,
} = useComposerAttachments({ maxFileSize: props.maxFileSize })

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
  <div class="t-thread-composer-wrap">
    <div
      class="t-thread-composer"
      :class="{ 'is-dragover': isDragOver }"
      @drop="onDrop"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
    >
      <!-- attachment chips -->
      <div v-if="files.length > 0" class="t-thread-composer__files">
        <div v-for="(f, i) in files" :key="i" class="t-thread-composer__chip">
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
            aria-label="Remove attachment"
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
            title="Attach files"
            aria-label="Attach files"
            @click="openFile"
          >
            <Icon icon="lucide:paperclip" />
          </button>
          <button
            v-if="enableVoice && isSupported"
            type="button"
            class="t-thread-composer__act"
            :class="{ 'is-recording': isListening }"
            :title="isListening ? 'Stop recording' : 'Voice input'"
            :aria-label="isListening ? 'Stop recording' : 'Voice input'"
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
              :class="{ 'is-ready': canSend }"
              :disabled="!canSend"
              aria-label="Send"
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
  margin-top: auto;
  padding-top: 24px;
  position: sticky;
  bottom: 0;
  background: linear-gradient(
    to bottom,
    transparent 0%,
    var(--tnzi-ai-bg, #f8f8f7) 24px
  );
  display: flex;
  justify-content: center;
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
  color: #fff;
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
  color: #fff;
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
