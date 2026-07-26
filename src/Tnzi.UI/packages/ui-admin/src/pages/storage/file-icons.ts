/**
 * File-type → icon mapping for the Storage file manager grid/list.
 *
 * Maps a file's `contentType` (preferred) or `extension` (fallback) to an
 * Iconify mdi glyph + a tint colour, so the grid view can render a
 * recognisable tile for non-image files (images use their thumbnail instead).
 */

export interface FileGlyph {
  icon: string
  color: string
}

/** Folder tile glyph - Finder-style warm yellow. */
export const FOLDER_GLYPH: FileGlyph = { icon: 'mdi:folder', color: '#f6c244' }

const BY_PREFIX: Array<[string, FileGlyph]> = [
  ['image/', { icon: 'mdi:file-image', color: '#10b981' }],
  ['video/', { icon: 'mdi:file-video', color: '#8b5cf6' }],
  ['audio/', { icon: 'mdi:file-music', color: '#f59e0b' }],
  ['text/', { icon: 'mdi:file-document-outline', color: '#64748b' }],
]

const PDF_GLYPH: FileGlyph = { icon: 'mdi:file-pdf-box', color: '#ef4444' }
const ZIP_GLYPH: FileGlyph = { icon: 'mdi:folder-zip', color: '#f59e0b' }

const BY_EXT: Record<string, FileGlyph> = {
  pdf: PDF_GLYPH,
  doc: { icon: 'mdi:file-word', color: '#2563eb' },
  docx: { icon: 'mdi:file-word', color: '#2563eb' },
  xls: { icon: 'mdi:file-excel', color: '#16a34a' },
  xlsx: { icon: 'mdi:file-excel', color: '#16a34a' },
  csv: { icon: 'mdi:file-delimited-outline', color: '#16a34a' },
  ppt: { icon: 'mdi:file-powerpoint', color: '#ea580c' },
  pptx: { icon: 'mdi:file-powerpoint', color: '#ea580c' },
  zip: ZIP_GLYPH,
  rar: ZIP_GLYPH,
  '7z': ZIP_GLYPH,
  gz: ZIP_GLYPH,
  tar: ZIP_GLYPH,
  json: { icon: 'mdi:code-json', color: '#0ea5e9' },
  js: { icon: 'mdi:language-javascript', color: '#eab308' },
  ts: { icon: 'mdi:language-typescript', color: '#0ea5e9' },
  html: { icon: 'mdi:language-html5', color: '#ea580c' },
  css: { icon: 'mdi:language-css3', color: '#2563eb' },
  md: { icon: 'mdi:language-markdown', color: '#64748b' },
}

const DEFAULT_GLYPH: FileGlyph = { icon: 'mdi:file-outline', color: '#94a3b8' }

/** Whether a file should render as an inline image thumbnail. */
export function isImageType(contentType?: string | null): boolean {
  return !!contentType && contentType.toLowerCase().startsWith('image/')
}

/** Resolve the glyph for a file by content type, falling back to extension. */
export function fileGlyph(contentType?: string | null, extension?: string | null): FileGlyph {
  const ct = (contentType ?? '').toLowerCase()
  if (ct === 'application/pdf') return PDF_GLYPH
  if (ct === 'application/zip' || ct === 'application/x-zip-compressed') return ZIP_GLYPH
  for (const [prefix, glyph] of BY_PREFIX) {
    if (ct.startsWith(prefix)) return glyph
  }
  const ext = (extension ?? '').replace(/^\./, '').toLowerCase()
  const byExt = ext ? BY_EXT[ext] : undefined
  if (byExt) return byExt
  return DEFAULT_GLYPH
}
