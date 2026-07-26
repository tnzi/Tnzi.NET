/**
 * fileIconForName - map a filename to a lucide icon by extension.
 *
 * Pure and business-agnostic. Shared by composer attachment chips and message
 * attachment lists so the icon mapping lives in one place.
 */

const EXT_ICON: Record<string, string> = {
  // Documents
  pdf: 'lucide:file-text',
  doc: 'lucide:file-type',
  docx: 'lucide:file-type',
  rtf: 'lucide:file-text',
  txt: 'lucide:file-text',
  md: 'lucide:file-text',
  log: 'lucide:file-text',
  // Spreadsheets / data
  xls: 'lucide:table',
  xlsx: 'lucide:table',
  csv: 'lucide:table-2',
  // Presentations
  ppt: 'lucide:presentation',
  pptx: 'lucide:presentation',
  // Structured
  json: 'lucide:braces',
  xml: 'lucide:code',
  yaml: 'lucide:settings',
  yml: 'lucide:settings',
  // Web / styles
  html: 'lucide:code',
  htm: 'lucide:code',
  css: 'lucide:palette',
  scss: 'lucide:palette',
  // Code
  js: 'lucide:file-code',
  ts: 'lucide:file-code',
  jsx: 'lucide:file-code',
  tsx: 'lucide:file-code',
  vue: 'lucide:file-code',
  py: 'lucide:file-code',
  java: 'lucide:file-code',
  c: 'lucide:file-code',
  cpp: 'lucide:file-code',
  cs: 'lucide:file-code',
  go: 'lucide:file-code',
  rs: 'lucide:file-code',
  rb: 'lucide:file-code',
  php: 'lucide:file-code',
  swift: 'lucide:file-code',
  kt: 'lucide:file-code',
  sql: 'lucide:database',
  sh: 'lucide:terminal',
  bash: 'lucide:terminal',
  // Images
  png: 'lucide:image',
  jpg: 'lucide:image',
  jpeg: 'lucide:image',
  gif: 'lucide:image',
  svg: 'lucide:image',
  webp: 'lucide:image',
  bmp: 'lucide:image',
  // Archives
  zip: 'lucide:file-archive',
  rar: 'lucide:file-archive',
  '7z': 'lucide:file-archive',
  tar: 'lucide:file-archive',
  gz: 'lucide:file-archive',
  // Media
  mp3: 'lucide:file-audio',
  wav: 'lucide:file-audio',
  ogg: 'lucide:file-audio',
  mp4: 'lucide:file-video',
  mov: 'lucide:file-video',
  avi: 'lucide:file-video',
  webm: 'lucide:file-video',
}

export function fileIconForName(name: string): string {
  const ext = name.split('.').pop()?.toLowerCase() ?? ''
  return EXT_ICON[ext] ?? 'lucide:file'
}
