import { ref, shallowRef, onScopeDispose, type Ref } from 'vue'

/**
 * Headless attachment state for chat composers - file selection, drag/drop,
 * image paste, blob-URL previews (revoked on removal/unmount), and size
 * validation. Shared by TThreadComposer + TLandingPage so the logic lives in
 * one place instead of being duplicated per composer.
 */
/** Why a file was refused. Only one reason exists today; typed as a union so
 *  adding MIME-type filtering later does not break consumers. */
export type AttachmentRejectionReason = 'too-large'

export interface RejectedAttachment {
  readonly file: File
  readonly reason: AttachmentRejectionReason
}

export interface UseComposerAttachmentsOptions {
  /** Max file size in bytes (default 10MB). */
  maxFileSize?: number
  /**
   * Called with the files that were refused by the most recent `addFiles`
   * call. Use it to surface a toast; the `rejected` ref carries the same list
   * for inline rendering.
   */
  onReject?: (rejected: readonly RejectedAttachment[]) => void
}

export interface UseComposerAttachmentsReturn {
  files: Ref<File[]>
  /** Files refused by the most recent `addFiles` call. Replaced (not appended
   *  to) on each call, and emptied by `clearRejected` / `clearFiles`. */
  rejected: Ref<RejectedAttachment[]>
  isDragOver: Ref<boolean>
  addFiles: (incoming: FileList | File[]) => void
  removeFile: (index: number) => void
  clearFiles: () => void
  clearRejected: () => void
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
  /* shallowRef, not ref: a deep ref hands back a reactive proxy for each File,
     and a proxy is not the same key as the raw File. `previewUrls` lookups from
     the template (`getPreviewUrl(f)` inside `v-for`) then missed, so image
     chips rendered with an empty src and `removeFile` never revoked the object
     URL. Files are opaque anyway, so there is nothing to track inside them and
     the array is always replaced wholesale. */
  const files = shallowRef<File[]>([])
  const rejected = shallowRef<RejectedAttachment[]>([])
  const isDragOver = ref(false)
  const previewUrls = new Map<File, string>()

  const isImageFile = (file: File): boolean => file.type.startsWith('image/')

  function addFiles(incoming: FileList | File[]): void {
    const valid: File[] = []
    const refused: RejectedAttachment[] = []
    for (const f of Array.from(incoming)) {
      if (f.size <= maxFileSize) valid.push(f)
      // Dropping oversized files without a word leaves the user staring at a
      // composer that quietly ignored their attachment.
      else refused.push({ file: f, reason: 'too-large' })
    }
    for (const f of valid) {
      if (isImageFile(f)) previewUrls.set(f, URL.createObjectURL(f))
    }
    files.value = [...files.value, ...valid]
    rejected.value = refused
    if (refused.length > 0) options.onReject?.(refused)
  }

  function clearRejected(): void {
    if (rejected.value.length > 0) rejected.value = []
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
    clearRejected()
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

  onScopeDispose(clearFiles, true)

  return {
    files,
    rejected,
    isDragOver,
    addFiles,
    removeFile,
    clearFiles,
    clearRejected,
    getPreviewUrl,
    isImageFile,
    onPaste,
    onDrop,
    onDragOver,
    onDragLeave,
  }
}
