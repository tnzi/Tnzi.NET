import { ref, shallowRef, onScopeDispose, type Ref } from 'vue'

/**
 * Headless attachment state for chat composers - file selection, drag/drop,
 * image paste, blob-URL previews (revoked on removal/unmount), and size and
 * type validation. Shared by TThreadComposer + TLandingPage so the logic lives
 * in one place instead of being duplicated per composer.
 *
 * Every way a file reaches the composer goes through `addFiles`: the file
 * picker, a drop, a pasted image, and a host handing files over
 * (`TThreadComposer.addAttachments`). So all of them meet the same checks and
 * the same rejection report.
 */
/** Why a file was refused. `too-large`: over `maxFileSize`. `unsupported-type`:
 *  not matched by the composer's `accept` list. */
export type AttachmentRejectionReason = 'too-large' | 'unsupported-type'

export interface RejectedAttachment {
  readonly file: File
  readonly reason: AttachmentRejectionReason
}

/** What one `addFiles` call did. */
export interface AddAttachmentsResult {
  /** The files that joined the composer. */
  readonly added: readonly File[]
  /** The files that were refused, with the reason (also reported through `onReject`). */
  readonly rejected: readonly RejectedAttachment[]
}

export interface UseComposerAttachmentsOptions {
  /** Max file size in bytes (default 10MB). */
  maxFileSize?: number
  /**
   * The accepted types, in the syntax of `<input accept>`: comma-separated
   * extensions (`.pdf`), MIME types (`application/pdf`) and wildcards
   * (`image/*`). Read on every entry, so a prop can drive it. Empty or
   * omitted accepts every type.
   *
   * The file picker applies `accept` itself, but a drop, a paste or a host
   * handing a file over does not; checking it here is what keeps those paths
   * from attaching a file the picker would never have offered.
   */
  accept?: () => string | null | undefined
  /**
   * Whether attachments are accepted at all, read on every entry (so a prop
   * can drive it). When it returns false, paste / drop / drag-over / addFiles
   * are no-ops. Gating only the paperclip button is not enough: a pasted
   * screenshot or a dropped file still became a chip that the composer then
   * handed to a transport the consumer had turned attachments off for.
   * Default: always enabled.
   */
  enabled?: () => boolean
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
  /** Add files through the size and type checks. Null when attachments are off
   *  (`enabled` returned false): nothing was considered, so nothing was refused. */
  addFiles: (incoming: FileList | readonly File[]) => AddAttachmentsResult | null
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

/**
 * Whether `file` matches an `accept` list (`<input accept>` syntax). An empty
 * list accepts everything. Matching is case-insensitive. A file without a MIME
 * type (browsers leave it empty for types they do not know) can only match an
 * extension, exactly as in the browser's own picker.
 */
export function isAcceptedFile(file: File, accept: string | null | undefined): boolean {
  const tokens = (accept ?? '')
    .split(',')
    .map((t) => t.trim().toLowerCase())
    .filter(Boolean)
  if (tokens.length === 0) return true
  const name = file.name.toLowerCase()
  const type = (file.type ?? '').toLowerCase()
  return tokens.some((token) => {
    if (token.startsWith('.')) return name.endsWith(token)
    if (!type) return false
    if (token.endsWith('/*')) return type.startsWith(token.slice(0, -1))
    return type === token
  })
}

export function useComposerAttachments(
  options: UseComposerAttachmentsOptions = {},
): UseComposerAttachmentsReturn {
  const maxFileSize = options.maxFileSize ?? DEFAULT_MAX
  const isEnabled = () => options.enabled?.() ?? true
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

  function addFiles(incoming: FileList | readonly File[]): AddAttachmentsResult | null {
    if (!isEnabled()) return null
    const accept = options.accept?.()
    const valid: File[] = []
    const refused: RejectedAttachment[] = []
    for (const f of Array.from(incoming)) {
      // Type first: a file of a type the composer does not take is refused for
      // that reason whatever its size.
      if (!isAcceptedFile(f, accept)) refused.push({ file: f, reason: 'unsupported-type' })
      // Dropping oversized files without a word leaves the user staring at a
      // composer that quietly ignored their attachment.
      else if (f.size > maxFileSize) refused.push({ file: f, reason: 'too-large' })
      else valid.push(f)
    }
    for (const f of valid) {
      if (isImageFile(f)) previewUrls.set(f, URL.createObjectURL(f))
    }
    files.value = [...files.value, ...valid]
    rejected.value = refused
    if (refused.length > 0) options.onReject?.(refused)
    return { added: valid, rejected: refused }
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
    if (!isEnabled()) return
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
    if (!isEnabled()) return
    e.preventDefault()
    isDragOver.value = false
    if (e.dataTransfer?.files) addFiles(e.dataTransfer.files)
  }

  function onDragOver(e: DragEvent): void {
    if (!isEnabled()) return
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
