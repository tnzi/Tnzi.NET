<script setup lang="ts">
import { ref, computed } from 'vue'
import { NModal, NButton, NSpin, NSpace } from 'naive-ui'

// ---------------------------------------------------------------------------
// Props / Emits
// ---------------------------------------------------------------------------

interface Props {
  modelValue?: string | null
  shape?: 'circle' | 'square'
  cropper?: boolean
  aspectRatio?: number
  accept?: string
  maxSizeMb?: number
  upload: (file: File | Blob) => Promise<{ id?: string; url: string }>
  disabled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: null,
  shape: 'circle',
  cropper: true,
  aspectRatio: 1,
  accept: 'image/*',
  maxSizeMb: 5,
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [url: string]
  'update:fileId': [id: string | undefined]
  change: [payload: { id?: string; url: string }]
  error: [message: string]
}>()

// ---------------------------------------------------------------------------
// Internal state
// ---------------------------------------------------------------------------

const fileInputRef = ref<HTMLInputElement | null>(null)
const loading = ref(false)

// Cropper dialog state
const cropperModalOpen = ref(false)
const cropperImageSrc = ref('')
const cropperContainerRef = ref<HTMLImageElement | null>(null)

// Cropper instance (set lazily after dynamic import)
// eslint-disable-next-line @typescript-eslint/no-explicit-any
let cropperInstance: any = null

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

const previewClass = computed(() => [
  't-image-upload__preview',
  `t-image-upload--${props.shape}`,
  { 't-image-upload--disabled': props.disabled },
])

// ---------------------------------------------------------------------------
// File validation
// ---------------------------------------------------------------------------

function isAcceptedType(file: File, accept: string): boolean {
  if (!accept || accept === '*' || accept === '*/*') return true

  const acceptedPatterns = accept.split(',').map((s) => s.trim().toLowerCase())
  const fileMime = file.type.toLowerCase()
  const fileExt = '.' + (file.name.split('.').pop() ?? '').toLowerCase()

  return acceptedPatterns.some((pattern) => {
    if (pattern === 'image/*') return fileMime.startsWith('image/')
    if (pattern === 'video/*') return fileMime.startsWith('video/')
    if (pattern === 'audio/*') return fileMime.startsWith('audio/')
    if (pattern.endsWith('/*')) {
      const typePrefix = pattern.slice(0, pattern.indexOf('/'))
      return fileMime.startsWith(typePrefix + '/')
    }
    if (pattern.startsWith('.')) return fileExt === pattern
    return fileMime === pattern
  })
}

// ---------------------------------------------------------------------------
// Preview area click
// ---------------------------------------------------------------------------

function handlePreviewClick(): void {
  if (props.disabled) return
  fileInputRef.value?.click()
}

// ---------------------------------------------------------------------------
// File input change
// ---------------------------------------------------------------------------

async function handleFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  // Reset input so re-selecting the same file triggers the event again
  input.value = ''

  if (!file) return

  // Validate type
  if (!isAcceptedType(file, props.accept)) {
    emit('error', `Invalid file type. Please select a file matching "${props.accept}".`)
    return
  }

  // Validate size
  const sizeMb = file.size / (1024 * 1024)
  if (sizeMb > props.maxSizeMb) {
    emit(
      'error',
      `File size (${sizeMb.toFixed(1)} MB) exceeds the maximum allowed size of ${props.maxSizeMb} MB.`,
    )
    return
  }

  if (props.cropper) {
    await openCropper(file)
  } else {
    await performUpload(file)
  }
}

// ---------------------------------------------------------------------------
// Cropper flow
// ---------------------------------------------------------------------------

async function openCropper(file: File): Promise<void> {
  cropperImageSrc.value = URL.createObjectURL(file)
  cropperModalOpen.value = true
}

async function initCropperOnMount(): Promise<void> {
  if (!cropperContainerRef.value) return

  // Lazy-load cropperjs CSS (ignore TS — Vite resolves CSS imports at runtime)
  // eslint-disable-next-line @typescript-eslint/ban-ts-comment
  // @ts-ignore
  await import('cropperjs/dist/cropper.css')
  const CropperModule = await import('cropperjs')
  const Cropper = CropperModule.default

  if (cropperInstance) {
    cropperInstance.destroy()
    cropperInstance = null
  }

  cropperInstance = new Cropper(cropperContainerRef.value, {
    aspectRatio: props.aspectRatio,
    viewMode: 1,
    dragMode: 'move',
    autoCropArea: 1,
    ...(props.shape === 'circle' ? { cropBoxResizable: false } : {}),
  })
}

async function confirmCrop(): Promise<void> {
  if (!cropperInstance) return

  const canvas = cropperInstance.getCroppedCanvas()
  const blob = await new Promise<Blob | null>((resolve) => {
    canvas.toBlob(resolve, 'image/jpeg', 0.9)
  })

  cropperModalOpen.value = false
  cropperInstance.destroy()
  cropperInstance = null

  if (blob) {
    await performUpload(blob)
  }

  if (cropperImageSrc.value.startsWith('blob:')) {
    URL.revokeObjectURL(cropperImageSrc.value)
    cropperImageSrc.value = ''
  }
}

function cancelCrop(): void {
  cropperModalOpen.value = false
  if (cropperInstance) {
    cropperInstance.destroy()
    cropperInstance = null
  }
  if (cropperImageSrc.value.startsWith('blob:')) {
    URL.revokeObjectURL(cropperImageSrc.value)
    cropperImageSrc.value = ''
  }
}

// ---------------------------------------------------------------------------
// Upload
// ---------------------------------------------------------------------------

async function performUpload(fileOrBlob: File | Blob): Promise<void> {
  loading.value = true
  try {
    const result = await props.upload(fileOrBlob)
    emit('update:modelValue', result.url)
    emit('update:fileId', result.id)
    emit('change', { id: result.id, url: result.url })
  } catch {
    emit('error', 'Upload failed. Please try again.')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="t-image-upload">
    <!-- Hidden file input -->
    <input
      ref="fileInputRef"
      type="file"
      :accept="accept"
      class="t-image-upload__input"
      @change="handleFileChange"
    />

    <!-- Preview area -->
    <NSpin :show="loading">
      <div
        :class="previewClass"
        data-testid="image-upload-preview"
        @click="handlePreviewClick"
      >
        <img
          v-if="modelValue"
          :src="modelValue"
          class="t-image-upload__img"
          alt="Preview"
        />
        <div v-else class="t-image-upload__placeholder">
          <span class="t-image-upload__plus">+</span>
        </div>
      </div>
    </NSpin>

    <!-- Cropper modal -->
    <NModal v-model:show="cropperModalOpen" :mask-closable="false">
      <div class="t-image-upload__cropper-dialog">
        <div class="t-image-upload__cropper-container">
          <img
            v-if="cropperModalOpen"
            ref="cropperContainerRef"
            :src="cropperImageSrc"
            class="t-image-upload__cropper-img"
            alt="Crop"
            @load="initCropperOnMount"
          />
        </div>
        <NSpace justify="end" class="t-image-upload__cropper-actions">
          <NButton @click="cancelCrop">Cancel</NButton>
          <NButton type="primary" @click="confirmCrop">Confirm</NButton>
        </NSpace>
      </div>
    </NModal>
  </div>
</template>

<style scoped>
.t-image-upload {
  display: inline-block;
}

.t-image-upload__input {
  display: none;
}

/* Preview container */
.t-image-upload__preview {
  width: 96px;
  height: 96px;
  overflow: hidden;
  cursor: pointer;
  border: 1px dashed var(--tnzi-border);
  display: flex;
  align-items: center;
  justify-content: center;
  background-color: var(--tnzi-container-bg);
  transition: border-color 0.2s;
}

.t-image-upload__preview:hover:not(.t-image-upload--disabled) {
  border-color: var(--tnzi-primary-500);
}

.t-image-upload--circle {
  border-radius: 50%;
}

.t-image-upload--square {
  border-radius: 4px;
}

.t-image-upload--disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.t-image-upload__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.t-image-upload__placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  color: var(--tnzi-base-text-muted);
}

.t-image-upload__plus {
  font-size: 24px;
  line-height: 1;
}

/* Cropper dialog */
.t-image-upload__cropper-dialog {
  background: var(--tnzi-container-bg);
  border-radius: 8px;
  padding: 16px;
  max-width: 480px;
  width: 100%;
}

.t-image-upload__cropper-container {
  width: 100%;
  height: 320px;
  overflow: hidden;
  background: var(--tnzi-cropper-canvas-bg, rgb(0 0 0 / 85%));
}

.t-image-upload__cropper-img {
  display: block;
  max-width: 100%;
}

.t-image-upload__cropper-actions {
  margin-top: 12px;
}
</style>
