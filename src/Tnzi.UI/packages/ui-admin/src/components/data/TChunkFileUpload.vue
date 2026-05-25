<script lang="ts">
export interface ChunkUploader {
  initUpload: (fileMeta: { name: string; size: number; chunkCount: number }) => Promise<{ uploadId: string }>
  uploadChunk: (uploadId: string, chunkIndex: number, chunk: Blob) => Promise<void>
  completeUpload: (uploadId: string) => Promise<{ url: string }>
}
</script>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

const props = withDefaults(defineProps<{
  uploader: ChunkUploader
  chunkSize?: number
  translate?: (key: string) => string
}>(), {
  chunkSize: 1024 * 1024,
  translate: (key: string) => key,
})

const fileInputRef = ref<HTMLInputElement | null>(null)
function triggerFilePicker(): void {
  fileInputRef.value?.click()
}

const emit = defineEmits<{
  progress: [percent: number]
  success: [result: { url: string }]
  error: [error: Error]
}>()

const uploading = ref(false)
const progress = ref(0)

async function onFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return

  uploading.value = true
  progress.value = 0

  try {
    const chunkSize = props.chunkSize
    const chunkCount = Math.ceil(file.size / chunkSize)
    const { uploadId } = await props.uploader.initUpload({
      name: file.name,
      size: file.size,
      chunkCount,
    })

    for (let i = 0; i < chunkCount; i++) {
      const start = i * chunkSize
      const end = Math.min(start + chunkSize, file.size)
      const chunk = file.slice(start, end)
      await props.uploader.uploadChunk(uploadId, i, chunk)
      progress.value = Math.round(((i + 1) / chunkCount) * 100)
      emit('progress', progress.value)
    }

    const result = await props.uploader.completeUpload(uploadId)
    emit('success', result)
  } catch (err) {
    emit('error', err instanceof Error ? err : new Error(String(err)))
  } finally {
    uploading.value = false
    target.value = ''
  }
}
</script>

<template>
  <div class="t-chunk-file-upload">
    <!-- Hidden native input — the browser's default "Choose File / No file
         chosen" widget is visually inconsistent with the rest of the admin
         shell. We render an NButton instead and forward its click to the
         hidden input. The input keeps `display: none` so screen readers
         still announce the file picker correctly. -->
    <input
      ref="fileInputRef"
      type="file"
      class="t-chunk-file-upload__native"
      :disabled="uploading"
      @change="onFileChange"
    />
    <NButton
      type="primary"
      size="small"
      :loading="uploading"
      :disabled="uploading"
      @click="triggerFilePicker"
    >
      <template #icon>
        <TSvgIcon icon="mdi:cloud-upload-outline" :size="16" />
      </template>
      <span v-if="!uploading">{{ props.translate('admin.upload.choose') }}</span>
      <span v-else>{{ props.translate('admin.upload.uploading') }} ({{ progress }}%)</span>
    </NButton>
    <div v-if="uploading" class="t-chunk-file-upload__bar">
      <div class="t-chunk-file-upload__bar-fill" :style="{ width: progress + '%' }" />
    </div>
  </div>
</template>

<style scoped>
.t-chunk-file-upload {
  display: inline-flex;
  flex-direction: column;
  gap: var(--tnzi-spacing-sm, 8px);
  min-width: 0;
}
.t-chunk-file-upload__native {
  /* Visually hidden but still focusable (Safari requires a non-zero size). */
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
.t-chunk-file-upload__bar {
  width: 100%;
  min-width: 160px;
  height: var(--tnzi-progress-height, 6px);
  background: var(--tnzi-layout-bg);
  border-radius: var(--tnzi-radius-sm, 3px);
  overflow: hidden;
}
.t-chunk-file-upload__bar-fill {
  height: 100%;
  background: var(--tnzi-primary);
  transition: width 0.2s ease;
}
</style>
