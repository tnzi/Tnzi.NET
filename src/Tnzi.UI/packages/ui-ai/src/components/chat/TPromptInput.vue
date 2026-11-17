<script setup lang="ts">
/**
 * TPromptInput - Advanced chat input
 *
 * Auto-resizing textarea with file drag/drop, paste images,
 * keyboard shortcuts, and send/stop button.
 *
 * Attachment state comes from `useComposerAttachments`, shared with
 * `TThreadComposer`: this file used to carry its own copy of the same
 * select / paste / drop / preview-URL logic, and that copy had no way to be
 * switched off. `enableAttachments` gates the paperclip AND paste / drop,
 * so a widget whose transport cannot carry files never shows a chip.
 */

import { ref, computed, watch } from 'vue';
import { Icon } from '@iconify/vue';
import TIconButton from '../controls/TIconButton.vue';
import { vTooltip } from '../../headless/tooltip';
import { useAiI18n, formatAiMessage } from '../../i18n/index';
import { formatFileSize, isImeComposing } from '@tnzi/core/utils';
import { useComposerAttachments } from '../../headless/useComposerAttachments';
import { fileIconForName } from '../../utils/file-icon';
const props = withDefaults(defineProps<{
  modelValue: string;
  placeholder?: string;
  disabled?: boolean;
  loading?: boolean;
  /** Max file size in bytes (default 10MB). */
  maxFileSize?: number;
  /** Accept file types (e.g. "image/*,.pdf"). */
  accept?: string;
  /** Paperclip + drag/drop + paste. Default true (this input always had them). */
  enableAttachments?: boolean;
}>(), {
  disabled: false,
  loading: false,
  maxFileSize: 10 * 1024 * 1024, // 10MB
  accept: 'image/*,.pdf,.txt,.csv,.json,.md',
  enableAttachments: true,
});

const emit = defineEmits<{
  'update:modelValue': [value: string];
  submit: [content: string, files: File[]];
  stop: [];
}>();

const t = useAiI18n();
const textareaRef = ref<HTMLTextAreaElement | null>(null);
const fileInputRef = ref<HTMLInputElement | null>(null);

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
  onPaste: handlePaste,
  onDrop: handleDrop,
  onDragOver: handleDragOver,
  onDragLeave: handleDragLeave,
} = useComposerAttachments({
  maxFileSize: props.maxFileSize,
  enabled: () => props.enableAttachments,
});

watch(
  () => props.enableAttachments,
  (enabled) => {
    if (!enabled) clearFiles();
  },
);

// An oversized file is refused at selection; say so instead of dropping it
// without a word.
const rejectedMessage = computed(() =>
  rejected.value.length === 0
    ? ''
    : formatAiMessage(t.value.composer.filesTooLarge, {
        size: formatFileSize(props.maxFileSize),
        names: rejected.value.map((r) => r.file.name).join(', '),
      }),
);

const canSend = computed(() => {
  return (props.modelValue.trim().length > 0 || files.value.length > 0) && !props.disabled;
});

const placeholderText = computed(() => props.placeholder ?? t.value.chat.placeholder);

// The one button at the end is Stop while an answer streams, Send otherwise.
const submitLabel = computed(() => (props.loading ? t.value.chat.stop : t.value.composer.send));

function updateValue(event: Event): void {
  const target = event.target as HTMLTextAreaElement;
  emit('update:modelValue', target.value);
}

/**
 * Enter sends; it never stops. While an answer streams the button turns into
 * Stop, but the keyboard is where the user is already typing the next
 * question, and an Enter that aborted the running answer threw it away. The
 * Enter that commits an IME candidate is not a send either.
 */
function handleKeyDown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey) {
    if (isImeComposing(event)) return;
    event.preventDefault();
    if (!props.loading) handleSubmit();
    return;
  }

  // Backspace on empty input removes last file
  if (event.key === 'Backspace' && !props.modelValue && files.value.length > 0) {
    removeFile(files.value.length - 1);
  }
}

function handleSubmit(): void {
  if (props.loading) {
    emit('stop');
    return;
  }
  if (!canSend.value) return;

  emit('submit', props.modelValue, [...files.value]);
  emit('update:modelValue', '');
  clearFiles();
}

function openFileDialog(): void {
  fileInputRef.value?.click();
}

function handleFileInput(event: Event): void {
  const target = event.target as HTMLInputElement;
  if (target.files) {
    addFiles(target.files);
    target.value = '';
  }
}

// Focus textarea on mount
watch(textareaRef, (el) => {
  el?.focus();
}, { once: true });
</script>

<template>
  <div
    class="t-prompt-input relative rounded-xl border border-tnzi-border bg-tnzi-container transition-colors"
    :class="{ 't-prompt-input--drag-over': isDragOver }"
    @drop="handleDrop"
    @dragover="handleDragOver"
    @dragleave="handleDragLeave"
  >
    <!-- Oversized files are refused; say so instead of dropping them silently. -->
    <div v-if="rejectedMessage" class="t-prompt-input__reject" role="status">
      <Icon icon="lucide:triangle-alert" class="t-prompt-input__reject-icon" />
      <span class="t-prompt-input__reject-text">{{ rejectedMessage }}</span>
      <TIconButton icon="lucide:x" size="xs" :label="t.composer.dismiss" @click="clearRejected" />
    </div>

    <!-- File previews -->
    <div
      v-if="files.length > 0"
      class="flex flex-wrap gap-2 border-b border-tnzi-border/50 p-2"
    >
      <div
        v-for="(file, index) in files"
        :key="`${file.name}:${file.size}:${file.lastModified}:${index}`"
        class="relative flex items-center gap-1.5 rounded-md bg-tnzi-layout px-2 py-1"
      >
        <!-- Image preview -->
        <img
          v-if="isImageFile(file)"
          :src="getPreviewUrl(file)"
          :alt="file.name"
          class="h-8 w-8 rounded object-cover"
        />
        <Icon
          v-else
          :icon="fileIconForName(file.name)"
          class="size-4 text-tnzi-muted"
        />

        <span class="max-w-[120px] truncate text-xs">{{ file.name }}</span>
        <span class="text-[10px] text-tnzi-muted">{{ formatFileSize(file.size) }}</span>

        <!-- Remove button -->
        <TIconButton icon="lucide:x" size="xs" :label="t.composer.removeAttachment" @click="removeFile(index)" />
      </div>
    </div>

    <!-- Input area -->
    <div class="flex items-end gap-2 p-2">
      <!-- Prefix slot -->
      <slot name="prefix">
        <TIconButton
          v-if="enableAttachments"
          icon="lucide:paperclip"
          class="t-prompt-input__attach"
          :label="t.composer.attach"
          @click="openFileDialog"
        />
      </slot>

      <!-- Textarea -->
      <textarea
        ref="textareaRef"
        :value="modelValue"
        :placeholder="placeholderText"
        :disabled="disabled"
        rows="1"
        class="min-h-[36px] max-h-[200px] flex-1 resize-none bg-transparent py-2 text-sm text-tnzi-base placeholder:text-tnzi-muted focus:outline-none disabled:opacity-50"
        style="field-sizing: content"
        @input="updateValue"
        @keydown="handleKeyDown"
        @paste="handlePaste"
      />

      <!-- Suffix slot -->
      <slot name="suffix" />

      <!-- Actions -->
      <slot name="actions">
        <!-- Drawn as TThreadComposer's primary button: quiet while there is
             nothing to send, the accent once there is, the text colour as Stop. -->
        <button
          v-tooltip="submitLabel"
          type="button"
          class="t-prompt-input__send"
          :class="{ 'is-ready': !loading && canSend, 'is-stop': loading }"
          :data-action="loading ? 'stop' : 'send'"
          :aria-label="submitLabel"
          :disabled="!loading && !canSend"
          @click="handleSubmit"
        >
          <Icon :icon="loading ? 'lucide:square' : 'lucide:arrow-up'" aria-hidden="true" />
        </button>
      </slot>
    </div>

    <!-- Hidden file input -->
    <input
      ref="fileInputRef"
      type="file"
      class="hidden"
      :accept="accept"
      multiple
      @change="handleFileInput"
    />
  </div>
</template>

<style scoped>
/* The package's one focus style for a field (`--tnzi-ai-focus-*`). */
.t-prompt-input:focus-within {
  border-color: var(--tnzi-ai-focus-border);
  box-shadow: var(--tnzi-ai-focus-ring);
}
.t-prompt-input--drag-over {
  border-color: var(--tnzi-ai-accent-text);
  background: var(--tnzi-ai-accent-soft);
}
.t-prompt-input__attach {
  margin-bottom: 3px;
}
.t-prompt-input__send {
  flex-shrink: 0;
  width: 30px;
  height: 30px;
  margin-bottom: 3px;
  border: none;
  border-radius: 999px;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-tertiary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  cursor: pointer;
  transition:
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-prompt-input__send.is-ready {
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
}
.t-prompt-input__send.is-stop {
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-surface);
  font-size: 12px;
}
.t-prompt-input__send.is-stop:hover {
  opacity: 0.85;
}
.t-prompt-input__send:disabled {
  cursor: not-allowed;
}
.t-prompt-input__send:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-prompt-input__reject {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 8px 8px 0;
  padding: 2px 2px 2px 10px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-warning) 32%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-warning) 10%, transparent);
  border-radius: 8px;
  font-size: 12px;
}
.t-prompt-input__reject-icon {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-warning);
}
.t-prompt-input__reject-text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
