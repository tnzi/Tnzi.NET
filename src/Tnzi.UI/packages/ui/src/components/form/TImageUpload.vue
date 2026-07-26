<script setup lang="ts">
import { ref, computed } from 'vue'
import { NModal, NButton, NSpin, NSpace } from 'naive-ui'
import type CropperJs from 'cropperjs'

// ---------------------------------------------------------------------------
// Props / Emits
// ---------------------------------------------------------------------------

interface Props {
  modelValue?: string | null
  /** Outline / border-radius preset. `square` = 4px, `rounded` = 8px, `circle` = 50%. */
  shape?: 'circle' | 'square' | 'rounded'
  /** Preview box width. Number = px; string = any CSS length (e.g. `'200px'`). Default 96. */
  width?: number | string
  /** Preview box height. Number = px; string = any CSS length. Default 96 (rectangle when ≠ width). */
  height?: number | string
  /** How the loaded image fills the box. `cover` (default, fills + crops) or `contain` (fits whole image). */
  objectFit?: 'cover' | 'contain'
  cropper?: boolean
  aspectRatio?: number
  accept?: string
  maxSizeMb?: number
  upload: (file: File | Blob) => Promise<{ id?: string; url: string }>
  disabled?: boolean
  /** Simple centered placeholder text shown when empty. Overridden by the `#placeholder` slot. */
  placeholder?: string
  /** Native tooltip shown on hover over the upload area (e.g. accepted formats / size). */
  title?: string
  /** Show a corner control to clear the current image (only rendered when a value is set). */
  removable?: boolean
  /** Accessible label + tooltip for the remove control. */
  removeLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: null,
  shape: 'circle',
  width: 96,
  height: 96,
  objectFit: 'cover',
  cropper: true,
  aspectRatio: 1,
  accept: 'image/*',
  maxSizeMb: 5,
  disabled: false,
  placeholder: undefined,
  title: undefined,
  removable: false,
  removeLabel: 'Remove',
})

const emit = defineEmits<{
  'update:modelValue': [url: string]
  'update:fileId': [id: string | undefined]
  change: [payload: { id?: string; url: string }]
  remove: []
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

// Cropper instance (set lazily after dynamic import; cropperjs v2 web-component API)
let cropperInstance: CropperJs | null = null

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

const previewClass = computed(() => [
  't-image-upload__preview',
  `t-image-upload--${props.shape}`,
  { 't-image-upload--disabled': props.disabled },
])

function toCssSize(value: number | string): string {
  return typeof value === 'number' ? `${value}px` : value
}

const previewStyle = computed(() => ({
  width: toCssSize(props.width),
  height: toCssSize(props.height),
}))

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
// Remove current image
// ---------------------------------------------------------------------------

function handleRemove(): void {
  if (props.disabled) return
  emit('update:modelValue', '')
  emit('update:fileId', undefined)
  emit('remove')
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

// Contain-fit the loaded image inside the crop canvas (centred) and place the
// selection box over it. Idempotent - safe to call again once the canvas
// reaches its final size (see onCropperModalEntered).
function fitCropperToCanvas(): void {
  if (!cropperInstance) return

  // v2 renders the <cropper-image> at its natural size anchored at the canvas
  // origin; without this a photo larger than the dialog shows only its
  // top-left corner and the crop box lands on the wrong region.
  cropperInstance.getCropperImage()?.$center('contain')

  // v2 configures the crop ratio + initial size on the selection web component;
  // the old `aspectRatio`/`viewMode`/`autoCropArea` constructor options are gone.
  // Reset AFTER centring so the box is placed over the now-visible image.
  const selection = cropperInstance.getCropperSelection()
  if (selection) {
    selection.aspectRatio = props.aspectRatio
    selection.initialCoverage = 0.9
    selection.$reset()
  }
}

async function initCropperOnMount(): Promise<void> {
  if (!cropperContainerRef.value) return

  // cropperjs v2 ships its styles inside the web components' shadow DOM,
  // so there is no separate stylesheet to import anymore.
  const { default: Cropper } = await import('cropperjs')

  if (cropperInstance) {
    cropperInstance.destroy()
    cropperInstance = null
  }

  const cropper = new Cropper(cropperContainerRef.value)
  cropperInstance = cropper

  // Wait for the internal <cropper-image> to finish loading before the first fit.
  const image = cropper.getCropperImage()
  await image?.$ready()
  // The dialog may have been cancelled/confirmed while the image was loading.
  if (cropperInstance !== cropper) return
  fitCropperToCanvas()
}

// The modal's open transition animates/scales its box, so a fit computed while
// it plays is measured against a transformed, not-yet-final canvas and lands
// off-centre. Re-fit once the transition settles and the canvas is at its final,
// stable size - this is the authoritative centring for the fast-image case.
function onCropperModalEntered(): void {
  fitCropperToCanvas()
}

async function confirmCrop(): Promise<void> {
  if (!cropperInstance) return

  // v2: render the current selection to a real canvas (async), then export a JPEG blob.
  const selection = cropperInstance.getCropperSelection()
  const blob = selection
    ? await new Promise<Blob | null>((resolve) => {
        selection
          .$toCanvas()
          .then((canvas) => canvas.toBlob(resolve, 'image/jpeg', 0.9))
          .catch(() => resolve(null))
      })
    : null

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
        :style="previewStyle"
        :title="title"
        data-testid="image-upload-preview"
        @click="handlePreviewClick"
      >
        <img
          v-if="modelValue"
          :src="modelValue"
          class="t-image-upload__img"
          :style="{ objectFit }"
          alt="Preview"
        />
        <div v-else class="t-image-upload__placeholder">
          <!-- Consumers can fully replace the empty-state content (icon + text)
               via the #placeholder slot; otherwise a `placeholder` text prop, or
               finally the default `+` glyph, is shown. -->
          <slot name="placeholder">
            <span v-if="placeholder" class="t-image-upload__placeholder-text">{{ placeholder }}</span>
            <span v-else class="t-image-upload__plus">+</span>
          </slot>
        </div>
      </div>
    </NSpin>

    <!-- Remove control (only when a value is set and removing is enabled) -->
    <button
      v-if="removable && modelValue && !disabled"
      type="button"
      class="t-image-upload__remove"
      :title="removeLabel"
      :aria-label="removeLabel"
      @click.stop="handleRemove"
    >
      <svg viewBox="0 0 24 24" width="10" height="10" fill="none" aria-hidden="true">
        <path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
      </svg>
    </button>

    <!-- Cropper modal -->
    <NModal v-model:show="cropperModalOpen" :mask-closable="false" @after-enter="onCropperModalEntered">
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
  position: relative;
}

/* Corner remove control - a soft floating chip tucked onto the top-right edge.
   Hidden at rest, it fades in when the avatar is hovered (or focused for a11y)
   so it never clutters the resting state. */
.t-image-upload__remove {
  position: absolute;
  top: -3px;
  right: -3px;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  padding: 0;
  border: none;
  border-radius: 50%;
  background: var(--tnzi-container-bg, #fff);
  color: var(--tnzi-base-text-muted, #8a8a8a);
  box-shadow: 0 1px 4px rgb(0 0 0 / 20%);
  cursor: pointer;
  opacity: 0;
  transform: scale(0.75);
  transition:
    opacity 0.15s ease,
    transform 0.15s ease,
    color 0.15s ease,
    background-color 0.15s ease;
}

.t-image-upload:hover .t-image-upload__remove,
.t-image-upload__remove:focus-visible {
  opacity: 1;
  transform: scale(1);
}

.t-image-upload__remove:hover {
  color: #fff;
  background: var(--tnzi-error, #e64340);
}

.t-image-upload__input {
  display: none;
}

/* Preview container - size comes from the `width`/`height` props (inline style). */
.t-image-upload__preview {
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

.t-image-upload--rounded {
  border-radius: 8px;
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

.t-image-upload__placeholder-text {
  padding: 0 8px;
  font-size: 13px;
  line-height: 1.4;
  text-align: center;
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

/* The dynamically-inserted <cropper-canvas> defaults to `min-height: 100px`
   with no `height`, so it collapses well short of the 320px dialog. Force it
   to fill the container so the crop area uses the full height. */
.t-image-upload__cropper-container :deep(cropper-canvas) {
  width: 100%;
  height: 100%;
}

.t-image-upload__cropper-img {
  display: block;
  max-width: 100%;
}

.t-image-upload__cropper-actions {
  margin-top: 12px;
}
</style>
