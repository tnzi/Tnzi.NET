<script setup lang="ts">
/**
 * `TFileExplorer` - Finder-style icon grid for the Storage file manager.
 *
 * Renders the current directory's sub-folders and files as tiles:
 *  - folder tile: double-click to drill in, drop target for move/upload
 *  - file tile: image thumbnail or type glyph, checkbox for batch selection,
 *    double-click to preview
 *  - right-click anywhere on a tile opens a context menu (handled by parent)
 *  - drag a file/folder onto a folder tile → move; drop OS files onto a folder
 *    tile or the empty grid → upload (handled by parent via `upload-drop`)
 *
 * The list view stays in the parent (it reuses the existing data table); this
 * component owns the grid presentation + interactions only.
 */
import { ref, computed } from 'vue'
import { NCheckbox, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatFileSize } from '@tnzi/core'
import TEmpty from '../../../components/data/TEmpty.vue'
import { fileGlyph, isImageType, FOLDER_GLYPH } from '../file-icons'
import type { FileFolderDto, FileRecordDto } from '@tnzi/core/services/storage'

const props = defineProps<{
  folders: FileFolderDto[]
  files: FileRecordDto[]
  selectedFileIds: string[]
  loading?: boolean
  translate: (key: string, params?: Record<string, unknown>) => string
  /**
   * Resolve a file's inline preview URL (deployment-prefix aware). Supplied by
   * the parent (from the storage bridge) so the component never hardcodes
   * `/api/...`. Omitted → no image thumbnails (glyph fallback only).
   */
  previewUrl?: (id: string) => string
}>()

const emit = defineEmits<{
  (e: 'open-folder', id: string): void
  (e: 'preview-file', file: FileRecordDto): void
  (e: 'context-folder', payload: { folder: FileFolderDto; x: number; y: number }): void
  (e: 'context-file', payload: { file: FileRecordDto; x: number; y: number }): void
  (e: 'update:selectedFileIds', ids: string[]): void
  (e: 'move-file', payload: { fileId: string; folderId: string }): void
  (e: 'move-folder', payload: { folderId: string; newParentId: string }): void
  (e: 'upload-drop', payload: { files: File[]; folderId: string | null }): void
}>()

const DRAG_FORMAT = 'application/x-tnzi-item'
const dropFolderId = ref<string | null>(null)
const rootDropActive = ref(false)

const selectedSet = computed(() => new Set(props.selectedFileIds))
const isEmpty = computed(() => !props.folders.length && !props.files.length)

function thumbUrl(file: FileRecordDto): string | null {
  // There is no `thumbnailUrl` on the wire: the backend's FileRecordDto never
  // carried one, so the branch that used to check it here could never be taken
  // and thumbnails silently never rendered. The preview endpoint is the only
  // real source.
  if (isImageType(file.contentType) && props.previewUrl) return props.previewUrl(file.id)
  return null
}

function toggleFile(file: FileRecordDto): void {
  const set = new Set(props.selectedFileIds)
  if (set.has(file.id)) set.delete(file.id)
  else set.add(file.id)
  emit('update:selectedFileIds', [...set])
}

// ---- drag source ----
function onItemDragStart(e: DragEvent, type: 'file' | 'folder', id: string): void {
  if (!e.dataTransfer) return
  e.dataTransfer.setData(DRAG_FORMAT, JSON.stringify({ type, id }))
  e.dataTransfer.effectAllowed = 'move'
}

// ---- folder as drop target ----
function onFolderDragOver(e: DragEvent, folder: FileFolderDto): void {
  e.preventDefault()
  dropFolderId.value = folder.id
}
function onFolderDragLeave(folder: FileFolderDto): void {
  if (dropFolderId.value === folder.id) dropFolderId.value = null
}
function onFolderDrop(e: DragEvent, folder: FileFolderDto): void {
  e.preventDefault()
  e.stopPropagation()
  dropFolderId.value = null
  rootDropActive.value = false
  const osFiles = e.dataTransfer?.files
  if (osFiles && osFiles.length) {
    emit('upload-drop', { files: Array.from(osFiles), folderId: folder.id })
    return
  }
  const raw = e.dataTransfer?.getData(DRAG_FORMAT)
  if (!raw) return
  try {
    const payload = JSON.parse(raw) as { type: 'file' | 'folder'; id: string }
    if (payload.type === 'file') {
      emit('move-file', { fileId: payload.id, folderId: folder.id })
    } else if (payload.type === 'folder' && payload.id !== folder.id) {
      emit('move-folder', { folderId: payload.id, newParentId: folder.id })
    }
  } catch {
    // ignore malformed payload
  }
}

// ---- empty grid area as OS-file drop target (upload to current dir) ----
function onRootDragOver(e: DragEvent): void {
  if (e.dataTransfer?.types?.includes('Files')) {
    e.preventDefault()
    rootDropActive.value = true
  }
}
function onRootDragLeave(e: DragEvent): void {
  // Only clear when leaving the grid container, not when moving between tiles.
  if (e.currentTarget === e.target) rootDropActive.value = false
}
function onRootDrop(e: DragEvent): void {
  rootDropActive.value = false
  const osFiles = e.dataTransfer?.files
  if (osFiles && osFiles.length) {
    e.preventDefault()
    emit('upload-drop', { files: Array.from(osFiles), folderId: null })
  }
}
</script>

<template>
  <NSpin :show="loading" class="t-file-explorer">
    <div
      class="t-file-explorer__grid"
      :class="{ 't-file-explorer__grid--drop': rootDropActive }"
      @dragover="onRootDragOver"
      @dragleave="onRootDragLeave"
      @drop="onRootDrop"
    >
      <!-- folders first -->
      <button
        v-for="folder in folders"
        :key="`folder-${folder.id}`"
        type="button"
        class="t-file-tile t-file-tile--folder"
        :class="{ 't-file-tile--drop': dropFolderId === folder.id }"
        draggable="true"
        :title="folder.name"
        @dblclick="emit('open-folder', folder.id)"
        @contextmenu.prevent="emit('context-folder', { folder, x: $event.clientX, y: $event.clientY })"
        @dragstart="onItemDragStart($event, 'folder', folder.id)"
        @dragover="onFolderDragOver($event, folder)"
        @dragleave="onFolderDragLeave(folder)"
        @drop="onFolderDrop($event, folder)"
      >
        <span class="t-file-tile__thumb">
          <TSvgIcon :icon="FOLDER_GLYPH.icon" :size="42" :style="{ color: FOLDER_GLYPH.color }" />
        </span>
        <span class="t-file-tile__name">{{ folder.name }}</span>
        <span class="t-file-tile__meta">{{ translate('itemCount', { n: folder.fileCount }) }}</span>
      </button>

      <!-- files -->
      <div
        v-for="file in files"
        :key="`file-${file.id}`"
        class="t-file-tile t-file-tile--file"
        :class="{ 'is-selected': selectedSet.has(file.id) }"
        draggable="true"
        :title="file.originalName"
        @dblclick="emit('preview-file', file)"
        @contextmenu.prevent="emit('context-file', { file, x: $event.clientX, y: $event.clientY })"
        @dragstart="onItemDragStart($event, 'file', file.id)"
      >
        <NCheckbox
          class="t-file-tile__check"
          :checked="selectedSet.has(file.id)"
          @update:checked="toggleFile(file)"
          @click.stop
        />
        <span class="t-file-tile__thumb">
          <img
            v-if="thumbUrl(file)"
            :src="thumbUrl(file)!"
            class="t-file-tile__img"
            loading="lazy"
            alt=""
          />
          <TSvgIcon
            v-else
            :icon="fileGlyph(file.contentType, file.extension).icon"
            :size="42"
            :style="{ color: fileGlyph(file.contentType, file.extension).color }"
          />
        </span>
        <span class="t-file-tile__name">{{ file.originalName }}</span>
        <span class="t-file-tile__meta">{{ formatFileSize(file.size) }}</span>
      </div>

      <div v-if="isEmpty && !loading" class="t-file-explorer__empty">
        <TEmpty :text="translate('emptyDir')" />
        <p class="t-file-explorer__hint">{{ translate('dropHint') }}</p>
      </div>
    </div>
  </NSpin>
</template>

<style scoped>
.t-file-explorer {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-file-explorer :deep(.n-spin-container),
.t-file-explorer :deep(.n-spin-content) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-file-explorer__grid {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(116px, 1fr));
  gap: 8px;
  align-content: start;
  padding: 4px;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  transition: background 0.15s ease, box-shadow 0.15s ease;
}
.t-file-explorer__grid--drop {
  background: rgb(var(--tnzi-primary-rgb) / 0.06);
  box-shadow: inset 0 0 0 2px var(--tnzi-primary);
}
.t-file-tile {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 12px 8px 10px;
  border: 1px solid transparent;
  border-radius: var(--tnzi-admin-radius-md, 6px);
  background: transparent;
  cursor: pointer;
  text-align: center;
  user-select: none;
  font: inherit;
  color: var(--tnzi-base-text);
}
.t-file-tile:hover {
  background: var(--tnzi-layout-bg);
}
.t-file-tile:focus-visible {
  outline: 2px solid var(--tnzi-primary);
}
.t-file-tile--file.is-selected {
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
  border-color: rgb(var(--tnzi-primary-rgb) / 0.4);
}
.t-file-tile--drop {
  background: rgb(var(--tnzi-primary-rgb) / 0.14);
  border-color: var(--tnzi-primary);
}
.t-file-tile__check {
  position: absolute;
  top: 6px;
  left: 6px;
  opacity: 0;
  transition: opacity 0.12s ease;
}
.t-file-tile--file:hover .t-file-tile__check,
.t-file-tile--file.is-selected .t-file-tile__check {
  opacity: 1;
}
.t-file-tile__thumb {
  width: 64px;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--tnzi-admin-radius-md, 6px);
  overflow: hidden;
}
.t-file-tile__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  background: var(--tnzi-layout-bg);
}
.t-file-tile__name {
  width: 100%;
  font-size: 12.5px;
  line-height: 1.3;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.t-file-tile__meta {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-file-explorer__empty {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 32px 8px;
}
.t-file-explorer__hint {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
</style>
