<script setup lang="ts">
/**
 * @experimental
 * TLandingPage — empty-state hero for chat applications.
 *
 * Renders a serif display headline above a chat composer and a row of
 * suggestion chips. The composer supports text + voice input, file
 * attachments (paperclip / drag-drop / paste-image), and declarative extra
 * toolbar buttons via `composerActions`. All content is slot-driven so
 * consumers can tailor every region without forking the component.
 *
 * Slots:
 *   - plan       ........ content above the headline (plan badge, trial pill)
 *   - headline   ........ overrides the default serif greeting
 *   - subline    ........ optional subtitle beneath the headline
 *   - composer-left  .... left cluster of the composer toolbar (before built-ins)
 *   - composer-right ..... right cluster of the composer toolbar (before send)
 *   - chips      ........ overrides the default chip row
 *   - footer     ........ free area below everything
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { formatFileSize } from '@tnzi/core'
import { useVoiceInput } from '../composables/useVoiceInput'
import { useComposerAttachments } from '../composables/useComposerAttachments'
import { useAutoGrowTextarea } from '../composables/useAutoGrowTextarea'
import { fileIconForName } from '../lib/fileIcon'
import type { ComposerAction } from '../components/chat/composer-types'
import { DEFAULT_COMPOSER_ACCEPT } from '../components/chat/composer-types'

export interface LandingChip {
  readonly id?: string
  readonly icon?: string
  readonly label: string
  readonly prompt?: string
}

const props = withDefaults(
  defineProps<{
    /** Composer text (two-way bound). */
    modelValue?: string
    /** Main serif greeting text. Ignored if the `headline` slot is used. */
    greeting?: string
    /** Optional subtitle below the greeting. Ignored if the `subline` slot is used. */
    subline?: string
    /** Composer placeholder. */
    placeholder?: string
    /** Suggestion chips. Ignored if the `chips` slot is used. */
    chips?: readonly LandingChip[]
    /** Composer max width in pixels. */
    maxWidth?: number
    /** Show the send button even when the input is empty. */
    alwaysShowSend?: boolean
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
    modelValue: '',
    greeting: 'What can I do for you?',
    placeholder: 'Assign a task or ask anything',
    chips: () => [],
    maxWidth: 768,
    alwaysShowSend: false,
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
  submit: [value: string, files: File[]]
  'chip-click': [chip: LandingChip]
  action: [id: string]
}>()

const text = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})

const fileInputRef = ref<HTMLInputElement | null>(null)
const textareaRef = ref<HTMLTextAreaElement | null>(null)
useAutoGrowTextarea(textareaRef, text, 200)

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

const isReady = computed(
  () => text.value.trim().length > 0 || files.value.length > 0 || props.alwaysShowSend,
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

function onSubmit(): void {
  if (!isReady.value) return
  emit('submit', text.value, [...files.value])
  clearFiles()
}

function onKeydown(ev: KeyboardEvent): void {
  if (ev.key === 'Enter' && !ev.shiftKey && !ev.isComposing) {
    ev.preventDefault()
    onSubmit()
  }
}

function onChipClick(chip: LandingChip): void {
  emit('chip-click', chip)
  if (chip.prompt != null) {
    text.value = chip.prompt
  }
}

function toggleVoice(): void {
  if (isListening.value) {
    stopVoice()
  } else {
    voiceBase = text.value.trim()
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
  <section class="t-landing">
    <div v-if="$slots.plan" class="t-landing__plan">
      <slot name="plan" />
    </div>

    <h1 class="t-landing__headline">
      <slot name="headline">{{ greeting }}</slot>
    </h1>

    <div v-if="subline || $slots.subline" class="t-landing__subline">
      <slot name="subline">{{ subline }}</slot>
    </div>

    <div
      class="t-landing__composer"
      :class="{ 'is-dragover': isDragOver }"
      :style="{ maxWidth: `${maxWidth}px` }"
      @drop="onDrop"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
    >
      <div v-if="files.length > 0" class="t-landing__files">
        <div v-for="(f, i) in files" :key="i" class="t-landing__chip">
          <img
            v-if="isImageFile(f)"
            :src="getPreviewUrl(f)"
            :alt="f.name"
            class="t-landing__chip-img"
          />
          <Icon v-else :icon="fileIconForName(f.name)" class="t-landing__chip-icon" />
          <span class="t-landing__chip-name">{{ f.name }}</span>
          <span class="t-landing__chip-size">{{ formatFileSize(f.size) }}</span>
          <button
            type="button"
            class="t-landing__chip-x"
            aria-label="Remove attachment"
            @click="removeFile(i)"
          >
            <Icon icon="lucide:x" />
          </button>
        </div>
      </div>

      <textarea
        ref="textareaRef"
        v-model="text"
        class="t-landing__input"
        rows="2"
        :placeholder="placeholder"
        @keydown="onKeydown"
        @paste="onPaste"
      />
      <div class="t-landing__composer-bar">
        <div class="t-landing__composer-left">
          <slot name="composer-left" />
          <button
            v-for="a in leftActions"
            :key="a.id"
            type="button"
            class="t-landing__act"
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
            class="t-landing__act"
            title="Attach files"
            aria-label="Attach files"
            @click="openFile"
          >
            <Icon icon="lucide:paperclip" />
          </button>
          <button
            v-if="enableVoice && isSupported"
            type="button"
            class="t-landing__act"
            :class="{ 'is-recording': isListening }"
            :title="isListening ? 'Stop recording' : 'Voice input'"
            :aria-label="isListening ? 'Stop recording' : 'Voice input'"
            @click="toggleVoice"
          >
            <Icon :icon="isListening ? 'lucide:square' : 'lucide:mic'" />
          </button>
        </div>
        <div class="t-landing__composer-right">
          <button
            v-for="a in rightActions"
            :key="a.id"
            type="button"
            class="t-landing__act"
            :class="{ 'is-active': a.active }"
            :disabled="a.disabled"
            :title="a.tooltip"
            :aria-label="a.tooltip || a.id"
            @click="emit('action', a.id)"
          >
            <Icon :icon="a.icon" />
          </button>
          <slot name="composer-right" />
          <button
            type="button"
            class="t-landing__send"
            :class="{ 'is-ready': isReady }"
            :disabled="!isReady"
            aria-label="Send"
            @click="onSubmit"
          >
            <Icon icon="lucide:arrow-up" />
          </button>
        </div>
      </div>

      <input
        ref="fileInputRef"
        type="file"
        class="t-landing__file-input"
        :accept="accept"
        multiple
        @change="onFileChange"
      />
    </div>

    <div v-if="$slots.chips || (chips && chips.length > 0)" class="t-landing__chips">
      <slot name="chips">
        <button
          v-for="(chip, i) in chips"
          :key="chip.id ?? chip.label ?? i"
          type="button"
          class="t-landing__suggestion"
          @click="onChipClick(chip)"
        >
          <Icon v-if="chip.icon" :icon="chip.icon" />
          <span>{{ chip.label }}</span>
        </button>
      </slot>
    </div>

    <div v-if="$slots.footer" class="t-landing__footer">
      <slot name="footer" />
    </div>
  </section>
</template>

<style scoped>
.t-landing {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px 80px;
  min-height: 0;
  overflow-y: auto;
}

.t-landing__plan {
  display: inline-flex;
  align-items: center;
  margin-bottom: 22px;
}

.t-landing__headline {
  margin: 0 0 28px;
  padding: 0;
  font-family: var(--tnzi-ai-font-display, 'Libre Baskerville', Georgia, serif);
  font-size: 36px;
  line-height: 1.5;
  font-weight: 400;
  color: var(--tnzi-ai-text, #34322d);
  text-align: center;
  letter-spacing: normal;
}

.t-landing__subline {
  font-size: 16px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.48));
  margin: -16px 0 28px;
  text-align: center;
}

.t-landing__composer {
  width: 100%;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border-strong, rgba(0, 0, 0, 0.2));
  border-radius: var(--tnzi-ai-composer-radius, 22px);
  box-shadow: var(--tnzi-ai-composer-shadow, 0 12px 32px rgba(0, 0, 0, 0.02));
  padding: 14px 4px 10px;
  transition: border-color 120ms var(--tnzi-ai-easing, ease), box-shadow 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__composer:focus-within {
  border-color: var(--tnzi-ai-accent, #0d9488);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
}
.t-landing__composer.is-dragover {
  border-color: var(--tnzi-ai-accent, #0d9488);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
}

.t-landing__files {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 0 14px 8px;
}
.t-landing__chip {
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
.t-landing__chip-img {
  width: 28px;
  height: 28px;
  border-radius: 4px;
  object-fit: cover;
}
.t-landing__chip-icon {
  font-size: 16px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
}
.t-landing__chip-name {
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-landing__chip-size {
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  font-size: 11px;
}
.t-landing__chip-x {
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
.t-landing__chip-x:hover {
  background: var(--tnzi-ai-press, rgba(0, 0, 0, 0.06));
  color: var(--tnzi-ai-text, #34322d);
}

.t-landing__input {
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  font-family: inherit;
  font-size: 15px;
  line-height: 22px;
  background: transparent;
  color: var(--tnzi-ai-text, #34322d);
  caret-color: var(--tnzi-ai-accent, #0d9488);
  padding: 0 18px;
  min-height: 22px;
  max-height: 200px;
}
.t-landing__input::placeholder {
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
}

.t-landing__composer-bar {
  display: flex;
  align-items: center;
  padding: 8px 10px 0;
}
.t-landing__composer-left,
.t-landing__composer-right {
  display: flex;
  align-items: center;
  gap: 2px;
}
.t-landing__composer-right { margin-left: auto; }

.t-landing__act {
  width: 32px;
  height: 32px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  transition: background 120ms var(--tnzi-ai-easing, ease), color 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__act:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text, #34322d);
}
.t-landing__act:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.t-landing__act.is-active {
  color: var(--tnzi-ai-accent, #0d9488);
  background: var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.12));
}
.t-landing__act.is-recording {
  color: #fff;
  background: var(--tnzi-ai-accent, #0d9488);
  animation: t-landing-pulse 1.4s ease-in-out infinite;
}
@keyframes t-landing-pulse {
  0%, 100% { box-shadow: 0 0 0 0 var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.4)); }
  50% { box-shadow: 0 0 0 5px transparent; }
}

.t-landing__send {
  width: 32px;
  height: 32px;
  border: none;
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  margin-left: 4px;
  transition: background 120ms var(--tnzi-ai-easing, ease),
              color 120ms var(--tnzi-ai-easing, ease),
              box-shadow 120ms var(--tnzi-ai-easing, ease),
              transform 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__send:disabled { cursor: not-allowed; }
.t-landing__send.is-ready {
  background: var(--tnzi-ai-accent, #0d9488);
  color: #fff;
  box-shadow: 0 8px 20px var(--tnzi-ai-accent-glow, rgba(13, 148, 136, 0.32));
}
.t-landing__send.is-ready:hover {
  transform: translateY(-1px);
}

.t-landing__file-input { display: none; }

.t-landing__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: center;
  margin-top: 18px;
  max-width: 768px;
}
.t-landing__suggestion {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 14px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  background: var(--tnzi-ai-surface, #ffffff);
  color: var(--tnzi-ai-text, #34322d);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
  transition: background 120ms var(--tnzi-ai-easing, ease), border-color 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__suggestion:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  border-color: var(--tnzi-ai-accent, #0d9488);
}
.t-landing__suggestion > .iconify {
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 15px;
}

.t-landing__footer {
  margin-top: 24px;
  width: 100%;
  max-width: 768px;
  display: flex;
  justify-content: center;
}
</style>
