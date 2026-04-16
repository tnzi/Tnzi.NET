<script lang="ts">
export interface ChunkUploader {
  initUpload: (fileMeta: { name: string; size: number; chunkCount: number }) => Promise<{ uploadId: string }>
  uploadChunk: (uploadId: string, chunkIndex: number, chunk: Blob) => Promise<void>
  completeUpload: (uploadId: string) => Promise<{ url: string }>
}
</script>

<script setup lang="ts">
import { ref } from 'vue'

const props = withDefaults(defineProps<{
  uploader: ChunkUploader
  chunkSize?: number
  translate?: (key: string) => string
}>(), {
  chunkSize: 1024 * 1024,
  translate: (key: string) => key,
})

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
    <label class="t-chunk-file-upload__field">
      <input type="file" :disabled="uploading" @change="onFileChange" />
      <span v-if="!uploading">{{ props.translate('admin.upload.choose') }}</span>
      <span v-else>{{ props.translate('admin.upload.uploading') }} ({{ progress }}%)</span>
    </label>
    <div v-if="uploading" class="t-chunk-file-upload__bar">
      <div class="t-chunk-file-upload__bar-fill" :style="{ width: progress + '%' }" />
    </div>
  </div>
</template>

<style scoped>
.t-chunk-file-upload {
  display: flex;
  flex-direction: column;
  gap: var(--tnzi-spacing-sm, 8px);
}

.t-chunk-file-upload__field {
  display: inline-flex;
  align-items: center;
  gap: var(--tnzi-spacing-sm, 8px);
  color: var(--tnzi-color-text, #333);
  font-size: var(--tnzi-font-size-base, 14px);
}

.t-chunk-file-upload__bar {
  width: 100%;
  height: var(--tnzi-progress-height, 6px);
  background: var(--tnzi-color-bg-muted, #eee);
  border-radius: var(--tnzi-radius-sm, 3px);
  overflow: hidden;
}

.t-chunk-file-upload__bar-fill {
  height: 100%;
  background: var(--tnzi-color-primary, #1976d2);
  transition: width 0.2s ease;
}
</style>
