<script setup lang="ts">
/**
 * `TFilePreviewModal` - inline file preview for the Storage file manager.
 *
 * Replaces the old `window.open` preview with an in-page modal: images render
 * in an `NImage` lightbox (zoom/rotate toolbar), PDFs in an iframe, audio/video
 * in native players, and any other type falls back to a download prompt.
 *
 * URLs arrive already resolved: most stored files are private, so their URL
 * carries a short-lived signed token that the parent has to fetch. Passing
 * builder functions would force this component to be async for no reason.
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
  /** Ready-to-use inline preview URL for `file` (empty while it resolves). */
  previewSrc?: string | null
  /** Ready-to-use download URL for `file` (empty while it resolves). */
  downloadSrc?: string | null
}>()

const emit = defineEmits<{ (e: 'update:show', v: boolean): void }>()

// Locals so the template never has to repeat the nullish fallback; the props
// are null while the parent is still minting the signed URL.
const src = computed(() => props.previewSrc ?? '')
const downloadHref = computed(() => props.downloadSrc ?? '')

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
        :src="src"
        :alt="file.originalName"
        object-fit="contain"
        class="t-file-preview__image"
      />
      <iframe
        v-else-if="kind === 'pdf'"
        :src="src"
        class="t-file-preview__frame"
        :title="file.originalName"
      />
      <video
        v-else-if="kind === 'video'"
        :src="src"
        controls
        class="t-file-preview__media"
      />
      <audio
        v-else-if="kind === 'audio'"
        :src="src"
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
        <a :href="downloadHref" :download="file?.originalName" class="t-file-preview__dl">
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
