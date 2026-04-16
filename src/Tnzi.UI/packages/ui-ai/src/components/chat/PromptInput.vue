<script setup lang="ts">
/**
 * PromptInput — Advanced chat input
 *
 * Auto-resizing textarea with file drag/drop, paste images,
 * keyboard shortcuts, and send/stop button.
 */

import { NButton } from 'naive-ui';
import { ref, computed, watch, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { formatFileSize } from '@tnzi/core';
const props = withDefaults(defineProps<{
  modelValue: string;
  placeholder?: string;
  disabled?: boolean;
  loading?: boolean;
  /** Max file size in bytes (default 10MB). */
  maxFileSize?: number;
  /** Accept file types (e.g. "image/*,.pdf"). */
  accept?: string;
}>(), {
  disabled: false,
  loading: false,
  maxFileSize: 10 * 1024 * 1024, // 10MB
  accept: 'image/*,.pdf,.txt,.csv,.json,.md',
});

const emit = defineEmits<{
  'update:modelValue': [value: string];
  submit: [content: string, files: File[]];
  stop: [];
}>();

const t = useAiI18n();
const textareaRef = ref<HTMLTextAreaElement | null>(null);
const fileInputRef = ref<HTMLInputElement | null>(null);
const files = ref<File[]>([]);
const isDragOver = ref(false);

/** Map of File → blob URL for image previews. Revoked on removal and unmount. */
const previewUrls = new Map<File, string>();

const canSend = computed(() => {
  return (props.modelValue.trim().length > 0 || files.value.length > 0) && !props.disabled;
});

const placeholderText = computed(() => props.placeholder ?? t.value.chat.placeholder);

function updateValue(event: Event): void {
  const target = event.target as HTMLTextAreaElement;
  emit('update:modelValue', target.value);
}

function handleKeyDown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    handleSubmit();
    return;
  }

  // Backspace on empty input removes last file
  if (event.key === 'Backspace' && !props.modelValue && files.value.length > 0) {
    files.value = files.value.slice(0, -1);
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
  revokeAllUrls();
  files.value = [];
}

function addFiles(newFiles: FileList | File[]): void {
  const validFiles = Array.from(newFiles).filter((file) => {
    if (file.size > props.maxFileSize) return false;
    return true;
  });
  // Create blob URLs for image previews
  for (const file of validFiles) {
    if (isImageFile(file)) {
      previewUrls.set(file, URL.createObjectURL(file));
    }
  }
  files.value = [...files.value, ...validFiles];
}

function removeFile(index: number): void {
  const file = files.value[index] as File | undefined;
  if (file) {
    // Revoke blob URL to avoid memory leak
    const url = previewUrls.get(file);
    if (url) {
      URL.revokeObjectURL(url);
      previewUrls.delete(file);
    }
  }
  files.value = [...files.value.slice(0, index), ...files.value.slice(index + 1)];
}

function revokeAllUrls(): void {
  for (const url of previewUrls.values()) {
    URL.revokeObjectURL(url);
  }
  previewUrls.clear();
}

function getPreviewUrl(file: File): string {
  return previewUrls.get(file) ?? '';
}

function handlePaste(event: ClipboardEvent): void {
  const items = event.clipboardData?.items;
  if (!items) return;

  const imageFiles: File[] = [];
  for (const item of items) {
    if (item.type.startsWith('image/')) {
      const file = item.getAsFile();
      if (file) imageFiles.push(file);
    }
  }

  if (imageFiles.length > 0) {
    addFiles(imageFiles);
  }
}

function handleDrop(event: DragEvent): void {
  event.preventDefault();
  isDragOver.value = false;
  const droppedFiles = event.dataTransfer?.files;
  if (droppedFiles) addFiles(droppedFiles);
}

function handleDragOver(event: DragEvent): void {
  event.preventDefault();
  isDragOver.value = true;
}

function handleDragLeave(): void {
  isDragOver.value = false;
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

function isImageFile(file: File): boolean {
  return file.type.startsWith('image/');
}

// Focus textarea on mount
watch(textareaRef, (el) => {
  el?.focus();
}, { once: true });

onBeforeUnmount(() => {
  revokeAllUrls();
});
</script>

<template>
  <div
    class="relative rounded-xl border border-border bg-background transition-colors"
    :class="{ 'border-primary/50 ring-1 ring-primary/20': isDragOver }"
    @drop="handleDrop"
    @dragover="handleDragOver"
    @dragleave="handleDragLeave"
  >
    <!-- File previews -->
    <div
      v-if="files.length > 0"
      class="flex flex-wrap gap-2 border-b border-border/50 p-2"
    >
      <div
        v-for="(file, index) in files"
        :key="index"
        class="group/file relative flex items-center gap-1.5 rounded-md bg-muted px-2 py-1"
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
          icon="lucide:file"
          class="size-4 text-muted-foreground"
        />

        <span class="max-w-[120px] truncate text-xs">{{ file.name }}</span>
        <span class="text-[10px] text-muted-foreground">{{ formatFileSize(file.size) }}</span>

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
        <NButton quaternary size="small" class="mb-0.5 shrink-0" @click="openFileDialog">
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
        class="min-h-[36px] max-h-[200px] flex-1 resize-none bg-transparent py-2 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none disabled:opacity-50"
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
