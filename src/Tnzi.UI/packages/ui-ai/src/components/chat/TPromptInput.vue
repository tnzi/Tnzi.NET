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

import { NButton } from 'naive-ui';
import { ref, computed, watch } from 'vue';
import { Icon } from '@iconify/vue';
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
    class="relative rounded-xl border border-tnzi-border bg-tnzi-container transition-colors"
    :class="{ 'border-primary/50 ring-1 ring-primary/20': isDragOver }"
    @drop="handleDrop"
    @dragover="handleDragOver"
    @dragleave="handleDragLeave"
  >
    <!-- Oversized files are refused; say so instead of dropping them silently. -->
    <div v-if="rejectedMessage" class="t-prompt-input__reject" role="status">
      <Icon icon="lucide:triangle-alert" class="t-prompt-input__reject-icon" />
      <span class="t-prompt-input__reject-text">{{ rejectedMessage }}</span>
      <NButton quaternary size="tiny" :aria-label="t.composer.dismiss" @click="clearRejected">
        <template #icon><Icon icon="lucide:x" /></template>
      </NButton>
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
        <NButton quaternary size="tiny" @click="removeFile(index)">
          <template #icon><Icon icon="lucide:x" /></template>
        </NButton>
      </div>
    </div>

    <!-- Input area -->
    <div class="flex items-end gap-2 p-2">
      <!-- Prefix slot -->
      <slot name="prefix">
        <NButton
          v-if="enableAttachments"
          quaternary
          size="small"
          class="mb-0.5 shrink-0"
          :aria-label="t.composer.attach"
          @click="openFileDialog"
        >
          <template #icon><Icon icon="lucide:paperclip" /></template>
        </NButton>
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
        <NButton
          size="small"
          class="mb-0.5 shrink-0"
          :type="loading ? 'error' : canSend ? 'primary' : 'default'"
          :disabled="!loading && !canSend"
          @click="handleSubmit"
        >
          <template #icon>
            <Icon v-if="loading" icon="lucide:square" />
            <Icon v-else icon="lucide:arrow-up" />
          </template>
        </NButton>
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
