<script setup lang="ts">
/**
 * `TFilePreviewModal` - inline file preview for the Storage file manager.
 *
 * Replaces the old `window.open` preview with an in-page modal: images render
 * in an `NImage` lightbox (zoom/rotate toolbar), PDFs in an iframe, audio/video
 * in native players, and any other type falls back to a download prompt. The
 * preview/download URLs are resolved by the parent (via the storage bridge) and
 * passed in as functions so this component never hardcodes `/api/...`.
 */
import { computed } from 'vue'
import { NModal, NImage, NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { fileGlyph, isImageType } from '../file-icons'
import type { FileRecordDto } from '@tnzi/core/services/storage'

const props = defineProps<{
  show: boolean
  file: FileRecordDto | null
  translate: (key: string, params?: Record<string, unknown>) => string
  /** Resolve a file's inline preview URL (deployment-prefix aware). */
  previewUrl: (id: string) => string
  /** Resolve a file's direct download URL (deployment-prefix aware). */
  downloadUrl: (id: string) => string
}>()

const emit = defineEmits<{ (e: 'update:show', v: boolean): void }>()

const previewSrc = computed(() => (props.file ? props.previewUrl(props.file.id) : ''))
const downloadSrc = computed(() => (props.file ? props.downloadUrl(props.file.id) : ''))

type Kind = 'image' | 'pdf' | 'video' | 'audio' | 'other'
const kind = computed<Kind>(() => {
  const ct = (props.file?.contentType ?? '').toLowerCase()
  if (isImageType(ct)) return 'image'
  if (ct === 'application/pdf') return 'pdf'
  if (ct.startsWith('video/')) return 'video'
  if (ct.startsWith('audio/')) return 'audio'
  return 'other'
})

function close(): void {
  emit('update:show', false)
}
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    :title="file?.originalName ?? translate('preview.title')"
    :style="{ width: 'min(960px, 94vw)' }"
    :bordered="false"
    @update:show="emit('update:show', $event)"
  >
    <div v-if="file" class="t-file-preview">
      <NImage
        v-if="kind === 'image'"
        :src="previewSrc"
        :alt="file.originalName"
        object-fit="contain"
        class="t-file-preview__image"
      />
      <iframe
        v-else-if="kind === 'pdf'"
        :src="previewSrc"
        class="t-file-preview__frame"
        :title="file.originalName"
      />
      <video
        v-else-if="kind === 'video'"
        :src="previewSrc"
        controls
        class="t-file-preview__media"
      />
      <audio
        v-else-if="kind === 'audio'"
        :src="previewSrc"
        controls
        class="t-file-preview__audio"
      />
      <div v-else class="t-file-preview__fallback">
        <TSvgIcon
          :icon="fileGlyph(file.contentType, file.extension).icon"
          :size="64"
          :style="{ color: fileGlyph(file.contentType, file.extension).color }"
        />
        <p class="t-file-preview__fallback-text">{{ translate('preview.unsupportedInline') }}</p>
      </div>
    </div>

    <template #footer>
      <div class="t-file-preview__footer">
        <a :href="downloadSrc" :download="file?.originalName" class="t-file-preview__dl">
          <NButton type="primary" ghost size="small">
            <template #icon><TSvgIcon icon="mdi:download" :size="16" /></template>
            {{ translate('actions.download') }}
          </NButton>
        </a>
        <NButton size="small" @click="close">{{ translate('close') }}</NButton>
      </div>
    </template>
  </NModal>
</template>

<style scoped>
.t-file-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 240px;
  max-height: 72vh;
}
.t-file-preview__image {
  max-width: 100%;
  max-height: 72vh;
}
.t-file-preview__image :deep(img) {
  max-width: 100%;
  max-height: 72vh;
  object-fit: contain;
}
.t-file-preview__frame {
  width: 100%;
  height: 72vh;
  border: none;
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
.t-file-preview__media {
  max-width: 100%;
  max-height: 72vh;
}
.t-file-preview__audio {
  width: 100%;
}
.t-file-preview__fallback {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-file-preview__fallback-text {
  margin: 0;
  font-size: 13px;
}
.t-file-preview__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.t-file-preview__dl {
  text-decoration: none;
}
</style>
