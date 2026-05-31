import { ref, onUnmounted, type Ref } from 'vue'

/**
 * Headless attachment state for chat composers — file selection, drag/drop,
 * image paste, blob-URL previews (revoked on removal/unmount), and size
 * validation. Shared by TThreadComposer + TLandingPage so the logic lives in
 * one place instead of being duplicated per composer.
 */
export interface UseComposerAttachmentsOptions {
  /** Max file size in bytes (default 10MB). */
  maxFileSize?: number
}

export interface UseComposerAttachmentsReturn {
  files: Ref<File[]>
  isDragOver: Ref<boolean>
  addFiles: (incoming: FileList | File[]) => void
  removeFile: (index: number) => void
  clearFiles: () => void
  getPreviewUrl: (file: File) => string
  isImageFile: (file: File) => boolean
  onPaste: (e: ClipboardEvent) => void
  onDrop: (e: DragEvent) => void
  onDragOver: (e: DragEvent) => void
  onDragLeave: (e: DragEvent) => void
}

const DEFAULT_MAX = 10 * 1024 * 1024

export function useComposerAttachments(
  options: UseComposerAttachmentsOptions = {},
): UseComposerAttachmentsReturn {
  const maxFileSize = options.maxFileSize ?? DEFAULT_MAX
  const files = ref<File[]>([])
  const isDragOver = ref(false)
  const previewUrls = new Map<File, string>()

  const isImageFile = (file: File): boolean => file.type.startsWith('image/')

  function addFiles(incoming: FileList | File[]): void {
    const valid = Array.from(incoming).filter((f) => f.size <= maxFileSize)
    for (const f of valid) {
      if (isImageFile(f)) previewUrls.set(f, URL.createObjectURL(f))
    }
    files.value = [...files.value, ...valid]
  }

  function removeFile(index: number): void {
    const f = files.value[index]
    if (f) {
      const url = previewUrls.get(f)
      if (url) {
        URL.revokeObjectURL(url)
        previewUrls.delete(f)
      }
    }
    files.value = [...files.value.slice(0, index), ...files.value.slice(index + 1)]
  }

  function clearFiles(): void {
    for (const url of previewUrls.values()) URL.revokeObjectURL(url)
    previewUrls.clear()
    files.value = []
  }

  const getPreviewUrl = (file: File): string => previewUrls.get(file) ?? ''

  function onPaste(e: ClipboardEvent): void {
    const items = e.clipboardData?.items
    if (!items) return
    const imgs: File[] = []
    for (const item of items) {
      if (item.type.startsWith('image/')) {
        const f = item.getAsFile()
        if (f) imgs.push(f)
      }
    }
    if (imgs.length > 0) addFiles(imgs)
  }

  function onDrop(e: DragEvent): void {
    e.preventDefault()
    isDragOver.value = false
    if (e.dataTransfer?.files) addFiles(e.dataTransfer.files)
  }

  function onDragOver(e: DragEvent): void {
    e.preventDefault()
    isDragOver.value = true
  }

  function onDragLeave(e: DragEvent): void {
    // Ignore dragleave fired when moving over a child element (prevents flicker).
    const current = e.currentTarget as HTMLElement | null
    const related = e.relatedTarget as Node | null
    if (current && related && current.contains(related)) return
    isDragOver.value = false
  }

  onUnmounted(() => clearFiles())

  return {
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
  }
}
