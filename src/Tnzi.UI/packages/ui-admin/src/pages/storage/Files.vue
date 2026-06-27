<template>
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NButtonGroup size="small">
        <NButton :type="viewMode === 'grid' ? 'primary' : 'default'" @click="viewMode = 'grid'">
          <template #icon><TSvgIcon icon="mdi:view-grid-outline" :size="14" /></template>
        </NButton>
        <NButton :type="viewMode === 'list' ? 'primary' : 'default'" @click="viewMode = 'list'">
          <template #icon><TSvgIcon icon="mdi:format-list-bulleted" :size="14" /></template>
        </NButton>
      </NButtonGroup>
      <NButton size="small" tertiary @click="openCreateFolder(currentFolderId)">
        <template #icon><TSvgIcon icon="mdi:folder-plus-outline" :size="14" /></template>
        {{ t('newFolder') }}
      </NButton>
      <NButton size="small" type="primary" :loading="uploading" @click="triggerUpload">
        <template #icon><TSvgIcon icon="mdi:upload" :size="14" /></template>
        {{ t('upload') }}
      </NButton>
      <NButton size="small" @click="reload">{{ t('refresh') }}</NButton>
      <input
        ref="fileInput"
        type="file"
        multiple
        class="t-storage-file-page__file-input"
        @change="onFileInputChange"
      />
    </template>

    <div class="t-storage-file-page__layout">
      <!-- Full-width main: breadcrumb + toolbar + grid/list -->
      <section class="t-storage-file-page__main">
        <header class="t-storage-file-page__bar">
          <nav class="t-storage-file-page__breadcrumb">
            <button
              type="button"
              class="t-storage-file-page__crumb"
              :class="{ 'is-active': !currentFolderId && !isSearching }"
              @click="goRoot"
            >
              {{ t('root') }}
            </button>
            <template v-for="(f, idx) in breadcrumb" :key="f.id">
              <span class="t-storage-file-page__crumb-sep">/</span>
              <button
                type="button"
                class="t-storage-file-page__crumb"
                :class="{ 'is-active': idx === breadcrumb.length - 1 && !isSearching }"
                @click="openFolder(f.id)"
              >
                {{ f.name }}
              </button>
            </template>
            <template v-if="isSearching">
              <span class="t-storage-file-page__crumb-sep">/</span>
              <span class="t-storage-file-page__crumb is-active">{{ t('searchResults') }}</span>
            </template>
          </nav>

          <div class="t-storage-file-page__filters">
            <NInput
              v-model:value="search.originalName"
              :placeholder="t('search.originalName')"
              size="small"
              clearable
              class="t-storage-file-page__filter-name"
              @keyup.enter="applyFilters"
              @clear="applyFilters"
            />
            <NSelect
              v-model:value="search.contentType"
              :options="contentTypeOptions"
              :placeholder="t('search.contentType')"
              size="small"
              clearable
              class="t-storage-file-page__filter-type"
              @update:value="applyFilters"
            />
            <NButton size="small" type="primary" @click="applyFilters">
              <template #icon><TSvgIcon icon="mdi:magnify" :size="14" /></template>
            </NButton>
          </div>

          <NSpace
            v-if="currentFolder"
            size="small"
            class="t-storage-file-page__folder-actions"
          >
            <NButton size="small" tertiary @click="openRenameFolder(currentFolder)">
              <template #icon><TSvgIcon icon="mdi:pencil-outline" :size="14" /></template>
              {{ t('renameFolder') }}
            </NButton>
            <NPopconfirm
              :disabled="!canDeleteFolder(currentFolder)"
              @positive-click="deleteFolder(currentFolder)"
            >
              <template #trigger>
                <NButton
                  size="small"
                  type="error"
                  ghost
                  :disabled="!canDeleteFolder(currentFolder)"
                  :title="canDeleteFolder(currentFolder) ? '' : t('cannotDeleteNonEmpty')"
                >
                  <template #icon><TSvgIcon icon="mdi:trash-can-outline" :size="14" /></template>
                  {{ t('deleteFolder') }}
                </NButton>
              </template>
              {{ t('confirmDeleteFolder') }}
            </NPopconfirm>
          </NSpace>
        </header>

        <div v-if="selectedIds.length" class="t-storage-file-page__batch">
          <span>{{ t('selected', { n: selectedIds.length }) }}</span>
          <NSelect
            v-model:value="moveTarget"
            :options="moveTargetOptions"
            :placeholder="t('moveToPlaceholder')"
            size="small"
            clearable
            filterable
            class="w-260px max-w-full"
          />
          <NButton
            size="small"
            type="primary"
            :disabled="moveTarget === undefined"
            :loading="moving"
            @click="batchMoveFiles"
          >
            {{ t('moveButton') }}
          </NButton>
          <NPopconfirm @positive-click="batchDeleteFiles">
            <template #trigger>
              <NButton size="small" type="error" ghost :loading="deleting">
                {{ t('batchDelete') }}
              </NButton>
            </template>
            {{ t('confirmBatchDelete') }}
          </NPopconfirm>
        </div>

        <!-- Grid view -->
        <TFileExplorer
          v-if="viewMode === 'grid'"
          :folders="subfolders"
          :files="files"
          :selected-file-ids="selectedIds"
          :loading="filesLoading"
          :translate="t"
          @open-folder="openFolder"
          @preview-file="openPreview"
          @context-folder="onContextFolder"
          @context-file="onContextFile"
          @update:selected-file-ids="selectedIds = $event"
          @move-file="onMoveFile"
          @move-folder="onMoveFolder"
          @upload-drop="onUploadDrop"
        />

        <!-- List view — folders + files unified in one table (Explorer-style):
             folders sort first, double-click a folder row to drill in. -->
        <div
          v-else
          class="t-storage-file-page__list"
          :class="{ 't-storage-file-page__list--drop': listDropActive }"
          @dragover="onListDragOver"
          @dragleave="onListDragLeave"
          @drop="onListDrop"
        >
          <NSpin :show="filesLoading || foldersLoading" class="t-storage-file-page__table-spin">
            <TResponsiveTable
              mobile="scroll"
              class="t-storage-file-page__table"
              :data="tableRows"
              :columns="columns"
              :row-key="(r: ExplorerRow) => r.id"
              :row-props="rowProps"
              :checked-row-keys="selectedIds"
              :empty-text="t('emptyDir')"
              size="small"
              :bordered="false"
              :flex-height="true"
              :pagination="false"
              @update:checked-row-keys="onUpdateChecked"
            />
          </NSpin>
        </div>

        <div class="t-storage-file-page__pagination">
          <NPagination
            v-model:page="pageIndex"
            :item-count="totalFiles"
            :page-size="pageSize"
            :page-sizes="[20, 50, 100]"
            show-size-picker
            @update:page="loadFiles"
            @update:page-size="onPageSizeChange"
          />
        </div>
      </section>
    </div>

    <!-- Context menu (file + folder) -->
    <NDropdown
      trigger="manual"
      placement="bottom-start"
      :show="ctx.show"
      :x="ctx.x"
      :y="ctx.y"
      :options="ctxOptions"
      @select="onCtxSelect"
      @clickoutside="ctx.show = false"
    />

    <!-- Preview lightbox -->
    <TFilePreviewModal v-model:show="preview.show" :file="preview.file" :translate="t" />

    <!-- Create / rename folder modal -->
    <NModal
      v-model:show="folderModal.show"
      :title="folderModal.mode === 'rename' ? t('renameFolder') : t('newFolder')"
      preset="card"
      :style="modalStyle"
    >
      <NForm label-placement="left" label-width="100px">
        <NFormItem :label="t('folderFields.name')" required>
          <NInput v-model:value="folderModal.form.name" />
        </NFormItem>
        <NFormItem :label="t('folderFields.description')">
          <NInput v-model:value="folderModal.form.description" type="textarea" :rows="2" />
        </NFormItem>
        <NFormItem :label="t('folderFields.sortOrder')">
          <NInputNumber v-model:value="folderModal.form.sortOrder" :min="0" />
        </NFormItem>
      </NForm>
      <template #footer>
        <div class="flex justify-end gap-8px">
          <NButton @click="folderModal.show = false">{{ t('cancel') }}</NButton>
          <NButton
            type="primary"
            :disabled="!folderModal.form.name"
            :loading="folderSaving"
            @click="submitFolderModal"
          >
            {{ folderModal.mode === 'rename' ? t('save') : t('create') }}
          </NButton>
        </div>
      </template>
    </NModal>

    <!-- Tags modal -->
    <NModal v-model:show="tagsModal.show" :title="t('tags.title')" preset="card" :style="modalStyle">
      <p class="text-13px text-muted mb-8px">{{ tagsModal.fileName }}</p>
      <NDynamicTags v-model:value="tagsModal.tags" />
      <template #footer>
        <div class="flex justify-end gap-8px">
          <NButton @click="tagsModal.show = false">{{ t('cancel') }}</NButton>
          <NButton type="primary" :loading="tagsModal.saving" @click="saveTags">{{ t('save') }}</NButton>
        </div>
      </template>
    </NModal>

    <!-- Metadata modal -->
    <NModal v-model:show="metadataModal.show" :title="t('metadata.title')" preset="card" :style="modalStyle">
      <p class="text-13px text-muted mb-8px">{{ metadataModal.fileName }}</p>
      <NSpin :show="metadataModal.loading">
        <NEmpty v-if="!metadataModal.rows.length" :description="t('metadata.empty')" class="my-12px" />
        <NSpace v-else vertical size="small">
          <div v-for="(row, i) in metadataModal.rows" :key="i" class="flex items-center gap-8px">
            <NInput v-model:value="row.key" :placeholder="t('metadata.key')" size="small" class="flex-1" />
            <NInput v-model:value="row.value" :placeholder="t('metadata.value')" size="small" class="flex-1" />
            <NButton size="small" quaternary @click="removeMetadataRow(i)">
              <template #icon><TSvgIcon icon="mdi:close" :size="14" /></template>
            </NButton>
          </div>
        </NSpace>
        <NButton size="small" dashed class="mt-8px" @click="addMetadataRow">
          <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
          {{ t('metadata.add') }}
        </NButton>
      </NSpin>
      <template #footer>
        <div class="flex justify-end gap-8px">
          <NButton @click="metadataModal.show = false">{{ t('cancel') }}</NButton>
          <NButton type="primary" :loading="metadataModal.saving" @click="saveMetadata">{{ t('save') }}</NButton>
        </div>
      </template>
    </NModal>

    <!-- File detail drawer (right) — opened by a file row's "View" action. -->
    <NDrawer v-model:show="detailDrawer.show" :width="bp.isSm.value ? 320 : 420" placement="right">
      <NDrawerContent :title="t('detail.title')" closable>
        <template v-if="detailDrawer.file">
          <NDescriptions label-placement="left" :column="1" size="small" bordered>
            <NDescriptionsItem :label="t('detail.name')">
              <span class="break-all">{{ detailDrawer.file.originalName }}</span>
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.size')">{{ formatSize(detailDrawer.file.size) }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.type')">{{ detailDrawer.file.contentType }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.storage')">{{ detailDrawer.file.provider }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.uploader')">
              {{ detailDrawer.file.creatorName || detailDrawer.file.creatorId || '—' }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.createdAt')">
              {{ detailDrawer.file.creationTime ? new Date(detailDrawer.file.creationTime).toLocaleString() : '—' }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.hash')">
              <span class="break-all text-12px">{{ detailDrawer.file.md5Hash || '—' }}</span>
            </NDescriptionsItem>
          </NDescriptions>

          <div v-if="detailFileTags.length" class="mt-14px">
            <p class="text-13px font-600 mb-6px">{{ t('detail.tags') }}</p>
            <NSpace size="small">
              <NTag v-for="tag in detailFileTags" :key="tag" size="small">{{ tag }}</NTag>
            </NSpace>
          </div>

          <div class="mt-16px">
            <p class="text-13px font-600 mb-6px">{{ t('detail.references') }}</p>
            <NSpin :show="detailDrawer.loadingRefs" size="small">
              <NEmpty
                v-if="!detailDrawer.references.length && !detailDrawer.loadingRefs"
                :description="t('detail.noReferences')"
                size="small"
                class="my-8px"
              />
              <div v-else class="t-storage-file-page__refs">
                <div v-for="ref in detailDrawer.references" :key="ref.id" class="t-storage-file-page__ref">
                  <div class="text-13px font-500">{{ ref.entityType }}</div>
                  <div class="text-12px text-muted">{{ t('detail.field') }}: {{ ref.fieldName }}</div>
                  <div class="text-12px text-muted break-all">{{ t('detail.entityId') }}: {{ ref.entityId }}</div>
                </div>
              </div>
            </NSpin>
          </div>
        </template>
        <template #footer>
          <NButton type="primary" :disabled="!detailDrawer.file" @click="detailDrawer.file && downloadFile(detailDrawer.file)">
            <template #icon><TSvgIcon icon="mdi:download" :size="14" /></template>
            {{ t('detail.download') }}
          </NButton>
        </template>
      </NDrawerContent>
    </NDrawer>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, watch, onMounted } from 'vue'
import type { Component } from 'vue'
import type { DataTableColumns, DropdownOption } from 'naive-ui'
import {
  NButton, NButtonGroup, NInput, NSelect, NSpin, NPagination,
  NModal, NForm, NFormItem, NInputNumber, NPopconfirm,
  NDropdown, NDynamicTags, NEmpty, NSpace,
  NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem, NTag,
} from 'naive-ui'
import { useSafeMessage } from '../_shared/safeMessage'
import { useBreakpoint } from '../../headless/useBreakpoint'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TContentPage from '../../components/layout/TContentPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import type { RowAction } from '../../headless/rowActions'
import { TSvgIcon } from '@tnzi/ui'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import TFileExplorer from './components/TFileExplorer.vue'
import TFilePreviewModal from './components/TFilePreviewModal.vue'
import { FOLDER_GLYPH, fileGlyph } from './file-icons'
import type { FileRecordDto, FileFolderDto, FileReferenceDto } from '@tnzi/core/services/storage'

const bridge = createStorageBridge({ client: useAdminClient() })
const t = makePageTranslator('storage.files')
const message = useSafeMessage()
const bp = useBreakpoint()

const modalStyle = computed(() =>
  bp.isSm.value ? { width: '100vw', maxWidth: '100vw' } : { width: 'min(460px, 95vw)' },
)

// ---- view mode (persisted) ----
const VIEW_KEY = 'tnzi-admin:storage-view'
function readView(): 'grid' | 'list' {
  try {
    return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'grid'
  } catch {
    return 'grid'
  }
}
const viewMode = ref<'grid' | 'list'>(readView())
watch(viewMode, (v) => {
  try {
    localStorage.setItem(VIEW_KEY, v)
  } catch {
    // ignore storage errors
  }
})

// ---- state ----
const folders = ref<FileFolderDto[]>([])
const files = ref<FileRecordDto[]>([])
const totalFiles = ref(0)
/** Current directory; null = root (top folders + unfiled files). */
const currentFolderId = ref<string | null>(null)
const pageIndex = ref(1)
const pageSize = ref(20)
const foldersLoading = ref(false)
const filesLoading = ref(false)
const moving = ref(false)
const deleting = ref(false)
const folderSaving = ref(false)
const uploading = ref(false)
const selectedIds = ref<string[]>([])
const fileInput = ref<HTMLInputElement | null>(null)
const listDropActive = ref(false)

const ROOT_TARGET = '__ROOT__'
const moveTarget = ref<string | undefined>(undefined)

const search = reactive({ originalName: '', contentType: '' })

const folderModal = reactive({
  show: false,
  mode: 'create' as 'create' | 'rename',
  parentId: null as string | null,
  targetId: null as string | null,
  form: { name: '', description: '' as string | undefined, sortOrder: 0 },
})

const tagsModal = reactive({ show: false, fileId: '', fileName: '', tags: [] as string[], saving: false })
const metadataModal = reactive({
  show: false, fileId: '', fileName: '',
  rows: [] as Array<{ key: string; value: string }>, loading: false, saving: false,
})

const ctx = reactive({
  show: false, x: 0, y: 0,
  type: 'file' as 'file' | 'folder',
  file: null as FileRecordDto | null,
  folder: null as FileFolderDto | null,
})
const preview = reactive({ show: false, file: null as FileRecordDto | null })

/** Right-side detail drawer — opened from a file row's primary "View" action.
 *  Shows the file's metadata + where it is referenced (loaded async). */
const detailDrawer = reactive({
  show: false,
  file: null as FileRecordDto | null,
  references: [] as FileReferenceDto[],
  loadingRefs: false,
})
const detailFileTags = computed<string[]>(() => {
  const tags = (detailDrawer.file as { tags?: string[] } | null)?.tags
  return Array.isArray(tags) ? tags : []
})

// ---- derived ----
const flatFolders = computed(() => {
  const map = new Map<string, FileFolderDto>()
  const walk = (nodes: FileFolderDto[]) => {
    for (const n of nodes) {
      map.set(n.id, n)
      if (n.children?.length) walk(n.children)
    }
  }
  walk(folders.value)
  return map
})

const currentFolder = computed(() =>
  currentFolderId.value ? flatFolders.value.get(currentFolderId.value) ?? null : null,
)

const isSearching = computed(() => !!search.originalName.trim() || !!search.contentType)

/** Sub-folders shown in the main area for the current directory. */
const subfolders = computed<FileFolderDto[]>(() => {
  if (isSearching.value) return []
  if (!currentFolderId.value) return folders.value
  return currentFolder.value?.children ?? []
})

/** Ancestor chain (root → current) for the breadcrumb. */
const breadcrumb = computed<FileFolderDto[]>(() => {
  const chain: FileFolderDto[] = []
  let f = currentFolder.value
  while (f) {
    chain.unshift(f)
    f = f.parentId ? flatFolders.value.get(f.parentId) ?? null : null
  }
  return chain
})

function canDeleteFolder(f: FileFolderDto | null): boolean {
  if (!f) return false
  if ((f.fileCount ?? 0) > 0) return false
  if (f.children && f.children.length > 0) return false
  return true
}

/**
 * Unified list-view row model — sub-folders and files share one table so
 * folders appear as rows (Explorer-style) instead of a separate strip.
 * Folders sort first; double-click a folder row drills in (see `rowProps`).
 */
type ExplorerRow =
  | { kind: 'folder'; id: string; folder: FileFolderDto }
  | { kind: 'file'; id: string; file: FileRecordDto }

const tableRows = computed<ExplorerRow[]>(() => [
  ...subfolders.value.map((folder) => ({ kind: 'folder' as const, id: folder.id, folder })),
  ...files.value.map((file) => ({ kind: 'file' as const, id: file.id, file })),
])

const moveTargetOptions = computed(() => {
  const opts: Array<{ label: string; value: string }> = [{ label: t('root'), value: ROOT_TARGET }]
  for (const [id, f] of flatFolders.value) opts.push({ label: f.path || f.name, value: id })
  return opts
})

const contentTypeOptions = [
  { label: 'Image', value: 'image/' },
  { label: 'Video', value: 'video/' },
  { label: 'Audio', value: 'audio/' },
  { label: 'PDF', value: 'application/pdf' },
  { label: 'Text', value: 'text/' },
  { label: 'Archive', value: 'application/zip' },
]

function formatSize(bytes: number): string {
  if (!bytes) return '0 B'
  const u = ['B', 'KB', 'MB', 'GB']
  const i = Math.min(u.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)))
  return `${(bytes / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${u[i]}`
}

/** Name cell — icon (folder glyph or file-type glyph) + label. `min-w-0` on
 *  both the flex row and the label lets it truncate inside the table cell. The
 *  label is clickable (`onClick`): a file name previews, a folder name opens. */
function renderNameCell(icon: string, color: string, label: string, onClick: () => void) {
  return h('div', { class: 'flex items-center gap-8px min-w-0' }, [
    h(TSvgIcon, { icon, size: 18, class: 'shrink-0', style: { color } }),
    h(
      'span',
      {
        class: 't-storage-file-page__name-link truncate min-w-0',
        title: label,
        onClick: (e: MouseEvent) => { e.stopPropagation(); onClick() },
      },
      label,
    ),
  ])
}

/**
 * Operation-column actions per row kind, rendered through the framework's
 * declarative `TRowActions` (C5 convention): auto-collapse to one inline
 * primary action + a `More▾` overflow, destructive ops gated by a confirm.
 * Replaces the previous hand-rolled Download / 👁 / ⋯ button cluster.
 */
function rowActionList(row: ExplorerRow): RowAction<ExplorerRow>[] {
  if (row.kind === 'folder') {
    const folder = row.folder
    return [
      { key: 'open', label: 'open', icon: 'mdi:folder-open-outline', type: 'primary', onClick: () => openFolder(folder.id) },
      { key: 'newSub', label: 'newSubFolder', icon: 'mdi:folder-plus-outline', onClick: () => openCreateFolder(folder.id) },
      { key: 'rename', label: 'renameFolder', icon: 'mdi:pencil-outline', onClick: () => openRenameFolder(folder) },
      {
        key: 'delete', label: 'deleteFolder', icon: 'mdi:trash-can-outline', type: 'error',
        confirm: 'confirmDeleteFolder', disabled: () => !canDeleteFolder(folder), onClick: () => void deleteFolder(folder),
      },
    ]
  }
  const file = row.file
  return [
    { key: 'view', label: 'actions.view', icon: 'mdi:eye-outline', type: 'primary', onClick: () => openDetail(file) },
    { key: 'download', label: 'actions.download', icon: 'mdi:download', onClick: () => downloadFile(file) },
    { key: 'preview', label: 'actions.preview', icon: 'mdi:image-outline', onClick: () => openPreview(file) },
    { key: 'tags', label: 'actions.tags', icon: 'mdi:tag-outline', onClick: () => openTagsModal(file) },
    { key: 'metadata', label: 'actions.metadata', icon: 'mdi:information-outline', onClick: () => void openMetadataModal(file) },
    { key: 'delete', label: 'actions.delete', icon: 'mdi:trash-can-outline', type: 'error', confirm: 'confirmDeleteFile', onClick: () => void deleteSingleFile(file) },
  ]
}

const columns = computed<DataTableColumns<ExplorerRow>>(() => [
  { type: 'selection', disabled: (row: ExplorerRow) => row.kind === 'folder' },
  {
    key: 'name', title: t('columns.originalName'), minWidth: 220,
    render: (row) =>
      row.kind === 'folder'
        ? renderNameCell(FOLDER_GLYPH.icon, FOLDER_GLYPH.color, row.folder.name, () => openFolder(row.folder.id))
        : renderNameCell(
            fileGlyph(row.file.contentType, row.file.extension).icon,
            fileGlyph(row.file.contentType, row.file.extension).color,
            row.file.originalName,
            () => openPreview(row.file),
          ),
  },
  {
    key: 'size', title: t('columns.size'), width: 110,
    render: (row) => (row.kind === 'folder' ? t('itemCount', { n: row.folder.fileCount ?? 0 }) : formatSize(row.file.size)),
  },
  {
    key: 'contentType', title: t('columns.contentType'), minWidth: 140, ellipsis: { tooltip: true },
    render: (row) => (row.kind === 'folder' ? t('folder') : row.file.contentType),
  },
  {
    key: 'provider', title: t('columns.provider'), width: 100,
    render: (row) => (row.kind === 'folder' ? '' : (row.file.provider ?? '')),
  },
  {
    key: 'referenceCount', title: t('columns.referenceCount'), width: 90,
    render: (row) => (row.kind === 'folder' ? '' : String(row.file.referenceCount ?? 0)),
  },
  {
    key: 'creationTime', title: t('columns.creationTime'), width: 160,
    render: (row) =>
      row.kind === 'folder'
        ? ''
        : (row.file.creationTime ? new Date(row.file.creationTime).toLocaleString() : ''),
  },
  {
    key: 'actions', title: t('columns.actions'), width: 188, align: 'center',
    // TRowActions is generic on the row type; `h()` can't infer it, so render
    // through the loose `Component` type and pass the typed actions list.
    render: (row) => h(TRowActions as Component, { row, actions: rowActionList(row), translate: t }),
  },
])

/** Row interactions — double-click drills into a folder / previews a file;
 *  right-click opens the matching context menu. Only files are selectable. */
function rowProps(row: ExplorerRow) {
  return {
    style: 'cursor: pointer',
    onDblclick: () => {
      if (row.kind === 'folder') openFolder(row.id)
      else openPreview(row.file)
    },
    onContextmenu: (e: MouseEvent) => {
      e.preventDefault()
      if (row.kind === 'folder') onContextFolder({ folder: row.folder, x: e.clientX, y: e.clientY })
      else onContextFile({ file: row.file, x: e.clientX, y: e.clientY })
    },
  }
}

/** Selection only tracks file ids (folder rows are disabled / never checked). */
function onUpdateChecked(keys: Array<string | number>): void {
  const fileIds = new Set(files.value.map((f) => f.id))
  selectedIds.value = keys.map(String).filter((k) => fileIds.has(k))
}

// ---- context menu ----
const ctxOptions = computed<DropdownOption[]>(() => {
  if (ctx.type === 'folder') {
    return [
      { key: 'open', label: t('open') },
      { key: 'newSub', label: t('newSubFolder') },
      { key: 'rename', label: t('renameFolder') },
      { type: 'divider', key: 'd1' },
      { key: 'delete', label: t('deleteFolder'), disabled: !canDeleteFolder(ctx.folder) },
    ]
  }
  return [
    { key: 'preview', label: t('actions.preview') },
    { key: 'download', label: t('actions.download') },
    { type: 'divider', key: 'd1' },
    { key: 'tags', label: t('actions.tags') },
    { key: 'metadata', label: t('actions.metadata') },
    { type: 'divider', key: 'd2' },
    { key: 'delete', label: t('actions.delete') },
  ]
})

function onContextFile(payload: { file: FileRecordDto; x: number; y: number }): void {
  ctx.type = 'file'
  ctx.file = payload.file
  ctx.folder = null
  ctx.x = payload.x
  ctx.y = payload.y
  ctx.show = true
}
function onContextFolder(payload: { folder: FileFolderDto; x: number; y: number }): void {
  ctx.type = 'folder'
  ctx.folder = payload.folder
  ctx.file = null
  ctx.x = payload.x
  ctx.y = payload.y
  ctx.show = true
}
function onCtxSelect(key: string): void {
  ctx.show = false
  if (ctx.type === 'file' && ctx.file) {
    const f = ctx.file
    if (key === 'preview') openPreview(f)
    else if (key === 'download') downloadFile(f)
    else if (key === 'tags') openTagsModal(f)
    else if (key === 'metadata') void openMetadataModal(f)
    else if (key === 'delete') void deleteSingleFile(f)
  } else if (ctx.type === 'folder' && ctx.folder) {
    const f = ctx.folder
    if (key === 'open') openFolder(f.id)
    else if (key === 'newSub') openCreateFolder(f.id)
    else if (key === 'rename') openRenameFolder(f)
    else if (key === 'delete') void deleteFolder(f)
  }
}

// ---- data loading ----
async function loadFolders(): Promise<void> {
  foldersLoading.value = true
  try {
    folders.value = await bridge.folders.getTree()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    folders.value = []
  } finally {
    foldersLoading.value = false
  }
}

function buildFilters(): Record<string, unknown> {
  const f: Record<string, unknown> = {}
  const name = search.originalName.trim()
  if (name) f.originalName = name
  if (search.contentType) f.contentType = search.contentType
  if (!isSearching.value) {
    if (currentFolderId.value) f.folderId = currentFolderId.value
    else f.includeUnfiled = true
  }
  return f
}

async function loadFiles(): Promise<void> {
  filesLoading.value = true
  selectedIds.value = []
  moveTarget.value = undefined
  try {
    const result = await bridge.files.fetch({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      sortField: 'creationTime',
      sortOrder: 'desc' as const,
      searchText: '',
      filters: buildFilters(),
    })
    files.value = result.items
    totalFiles.value = result.totalCount
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    files.value = []
    totalFiles.value = 0
  } finally {
    filesLoading.value = false
  }
}

async function reload(): Promise<void> {
  await Promise.all([loadFolders(), loadFiles()])
}

// ---- navigation ----
function openFolder(id: string): void {
  currentFolderId.value = id
  search.originalName = ''
  search.contentType = ''
  pageIndex.value = 1
  void loadFiles()
}
function goRoot(): void {
  currentFolderId.value = null
  search.originalName = ''
  search.contentType = ''
  pageIndex.value = 1
  void loadFiles()
}
function applyFilters(): void {
  pageIndex.value = 1
  void loadFiles()
}
function onPageSizeChange(next: number): void {
  pageSize.value = next
  pageIndex.value = 1
  void loadFiles()
}

// ---- file actions ----
function downloadFile(row: FileRecordDto): void {
  window.open(`/api/files/${row.id}/download`, '_blank')
}
function openPreview(row: FileRecordDto): void {
  preview.file = row
  preview.show = true
}
/** Open the right-side detail drawer and lazily load the file's references. */
function openDetail(row: FileRecordDto): void {
  detailDrawer.file = row
  detailDrawer.references = []
  detailDrawer.show = true
  detailDrawer.loadingRefs = true
  bridge.references
    .byFile(row.id)
    .then((refs) => { detailDrawer.references = refs })
    .catch(() => { detailDrawer.references = [] })
    .finally(() => { detailDrawer.loadingRefs = false })
}
async function deleteSingleFile(row: FileRecordDto): Promise<void> {
  try {
    await bridge.files.delete([row.id])
    message.success(t('deleteSuccess'))
    await reload()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

function openTagsModal(row: FileRecordDto): void {
  tagsModal.fileId = row.id
  tagsModal.fileName = row.originalName
  const existing = (row as { tags?: string[] }).tags
  tagsModal.tags = Array.isArray(existing) ? [...existing] : []
  tagsModal.show = true
}
async function saveTags(): Promise<void> {
  tagsModal.saving = true
  try {
    await bridge.tags.set(tagsModal.fileId, tagsModal.tags)
    message.success(t('tags.saveSuccess'))
    tagsModal.show = false
    await loadFiles()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    tagsModal.saving = false
  }
}

async function openMetadataModal(row: FileRecordDto): Promise<void> {
  metadataModal.fileId = row.id
  metadataModal.fileName = row.originalName
  metadataModal.rows = []
  metadataModal.show = true
  metadataModal.loading = true
  try {
    const map = await bridge.metadata.get(row.id)
    metadataModal.rows = Object.entries(map).map(([key, value]) => ({ key, value }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    metadataModal.loading = false
  }
}
function addMetadataRow(): void {
  metadataModal.rows = [...metadataModal.rows, { key: '', value: '' }]
}
function removeMetadataRow(index: number): void {
  metadataModal.rows = metadataModal.rows.filter((_, i) => i !== index)
}
async function saveMetadata(): Promise<void> {
  metadataModal.saving = true
  try {
    const map: Record<string, string> = {}
    for (const r of metadataModal.rows) {
      const key = r.key.trim()
      if (key) map[key] = r.value
    }
    await bridge.metadata.set(metadataModal.fileId, map)
    message.success(t('metadata.saveSuccess'))
    metadataModal.show = false
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    metadataModal.saving = false
  }
}

// ---- batch ----
async function batchMoveFiles(): Promise<void> {
  if (!selectedIds.value.length || moveTarget.value === undefined) return
  moving.value = true
  try {
    const target = moveTarget.value === ROOT_TARGET ? null : moveTarget.value
    await bridge.files.moveTo(selectedIds.value, target)
    message.success(t('moveSuccess'))
    await reload()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    moving.value = false
  }
}
async function batchDeleteFiles(): Promise<void> {
  if (!selectedIds.value.length) return
  deleting.value = true
  try {
    await bridge.files.delete(selectedIds.value)
    message.success(t('deleteSuccess'))
    await reload()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    deleting.value = false
  }
}

// ---- drag move ----
async function onMoveFile(payload: { fileId: string; folderId: string }): Promise<void> {
  try {
    await bridge.files.moveTo([payload.fileId], payload.folderId)
    message.success(t('moveSuccess'))
    await reload()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
/** Whether `ancestorCandidate` lies on the path from `folderId` down (i.e. is a descendant of folderId). */
function isDescendantOf(folderId: string, candidateId: string): boolean {
  let f: FileFolderDto | null = flatFolders.value.get(candidateId) ?? null
  while (f) {
    if (f.id === folderId) return true
    f = f.parentId ? flatFolders.value.get(f.parentId) ?? null : null
  }
  return false
}
async function onMoveFolder(payload: { folderId: string; newParentId: string }): Promise<void> {
  if (payload.folderId === payload.newParentId || isDescendantOf(payload.folderId, payload.newParentId)) {
    message.warning(t('invalidMove'))
    return
  }
  try {
    await bridge.folders.move(payload.folderId, payload.newParentId)
    message.success(t('moveSuccess'))
    await reload()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// ---- upload (folder-aware; chunked fallback for large files) ----
const CHUNK_THRESHOLD = 90 * 1024 * 1024
const CHUNK_SIZE = 5 * 1024 * 1024

async function chunkedUpload(file: File): Promise<void> {
  const chunkCount = Math.max(1, Math.ceil(file.size / CHUNK_SIZE))
  const { uploadId } = await bridge.files.initUpload({ name: file.name, size: file.size, chunkCount })
  for (let i = 0; i < chunkCount; i++) {
    const blob = file.slice(i * CHUNK_SIZE, Math.min(file.size, (i + 1) * CHUNK_SIZE))
    await bridge.files.uploadChunk(uploadId, i, blob)
  }
  await bridge.files.completeUpload(uploadId)
}

/** Upload one file; returns true when it went chunked-to-root despite a target folder. */
async function uploadOne(file: File, target: string | null): Promise<boolean> {
  if (file.size > CHUNK_THRESHOLD) {
    await chunkedUpload(file)
    return target != null
  }
  const res = await bridge.files.upload(file)
  if (target) await bridge.files.moveTo([res.id], target)
  return false
}

async function uploadFiles(list: File[], target: string | null): Promise<void> {
  if (!list.length) return
  uploading.value = true
  let ok = 0
  let largeToRoot = false
  for (const file of list) {
    try {
      if (await uploadOne(file, target)) largeToRoot = true
      ok++
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    }
  }
  uploading.value = false
  if (ok) {
    message.success(t('uploadSuccess', { n: ok }))
    if (largeToRoot) message.info(t('largeUploadedToRoot'))
    await reload()
  }
}

function triggerUpload(): void {
  fileInput.value?.click()
}
function onFileInputChange(e: Event): void {
  const input = e.target as HTMLInputElement
  const list = input.files ? Array.from(input.files) : []
  void uploadFiles(list, currentFolderId.value)
  input.value = ''
}
function onUploadDrop(payload: { files: File[]; folderId: string | null }): void {
  void uploadFiles(payload.files, payload.folderId ?? currentFolderId.value)
}

// list-view drop zone (OS files → upload to current dir)
function onListDragOver(e: DragEvent): void {
  if (e.dataTransfer?.types?.includes('Files')) {
    e.preventDefault()
    listDropActive.value = true
  }
}
function onListDragLeave(e: DragEvent): void {
  if (e.currentTarget === e.target) listDropActive.value = false
}
function onListDrop(e: DragEvent): void {
  listDropActive.value = false
  const osFiles = e.dataTransfer?.files
  if (osFiles && osFiles.length) {
    e.preventDefault()
    void uploadFiles(Array.from(osFiles), currentFolderId.value)
  }
}

// ---- folder CRUD ----
function openCreateFolder(parentId: string | null): void {
  folderModal.mode = 'create'
  folderModal.parentId = parentId
  folderModal.targetId = null
  folderModal.form = { name: '', description: undefined, sortOrder: 0 }
  folderModal.show = true
}
function openRenameFolder(folder: FileFolderDto): void {
  folderModal.mode = 'rename'
  folderModal.parentId = folder.parentId ?? null
  folderModal.targetId = folder.id
  folderModal.form = {
    name: folder.name,
    description: folder.description ?? undefined,
    sortOrder: folder.sortOrder,
  }
  folderModal.show = true
}
async function submitFolderModal(): Promise<void> {
  folderSaving.value = true
  try {
    if (folderModal.mode === 'create') {
      const created = await bridge.folders.create({
        name: folderModal.form.name,
        parentId: folderModal.parentId,
        description: folderModal.form.description ?? null,
        sortOrder: folderModal.form.sortOrder,
      })
      message.success(t('createFolderSuccess'))
      folderModal.show = false
      await loadFolders()
      openFolder(created.id)
    } else if (folderModal.targetId) {
      await bridge.folders.update(folderModal.targetId, {
        name: folderModal.form.name,
        description: folderModal.form.description ?? null,
        sortOrder: folderModal.form.sortOrder,
      })
      message.success(t('renameFolderSuccess'))
      folderModal.show = false
      await loadFolders()
    }
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    folderSaving.value = false
  }
}
async function deleteFolder(folder: FileFolderDto): Promise<void> {
  if (!canDeleteFolder(folder)) {
    message.warning(t('cannotDeleteNonEmpty'))
    return
  }
  try {
    await bridge.folders.delete(folder.id)
    message.success(t('deleteFolderSuccess'))
    if (currentFolderId.value === folder.id) {
      currentFolderId.value = folder.parentId ?? null
    }
    await reload()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

onMounted(async () => {
  await loadFolders()
  await loadFiles()
})
</script>

<style scoped>
.t-storage-file-page__file-input {
  display: none;
}
.t-storage-file-page__layout {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  min-width: 0;
}
.t-storage-file-page__main {
  display: flex;
  flex-direction: column;
  /* Fill the layout's height so the flex-height table inside resolves to a
     real height (a flex-column child collapses to content height without
     `flex: 1` — the former grid cell got this stretch for free). */
  flex: 1;
  min-height: 0;
  /* Allow the pane to shrink below the table's intrinsic min-width (NDataTable
     scroll-x) so the table scrolls horizontally inside it instead of pushing
     the page past the right edge. */
  min-width: 0;
}
.t-storage-file-page__bar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
  flex-wrap: wrap;
}
.t-storage-file-page__breadcrumb {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  margin-right: auto;
  min-width: 0;
}
.t-storage-file-page__crumb {
  background: none;
  border: none;
  cursor: pointer;
  font: inherit;
  font-size: 13px;
  padding: 2px 6px;
  border-radius: var(--tnzi-admin-radius-sm, 3px);
  color: var(--tnzi-base-text-muted);
}
.t-storage-file-page__crumb:hover {
  background: var(--tnzi-layout-bg);
  color: var(--tnzi-base-text);
}
.t-storage-file-page__crumb.is-active {
  color: var(--tnzi-base-text);
  font-weight: 600;
}
.t-storage-file-page__crumb-sep {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-storage-file-page__filters {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.t-storage-file-page__filter-name {
  width: 180px;
}
.t-storage-file-page__filter-type {
  width: 140px;
}
.t-storage-file-page__folder-actions {
  flex-shrink: 0;
}
.t-storage-file-page__batch {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 8px;
  padding: 8px 12px;
  background: rgb(var(--tnzi-primary-rgb) / 0.06);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  font-size: 13px;
}
.t-storage-file-page__list {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  display: flex;
  flex-direction: column;
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
.t-storage-file-page__list--drop {
  box-shadow: inset 0 0 0 2px var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb) / 0.04);
}
.t-storage-file-page__table-spin {
  flex: 1;
  min-height: 0;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.t-storage-file-page__table-spin :deep(.n-spin-container),
.t-storage-file-page__table-spin :deep(.n-spin-content) {
  flex: 1;
  min-height: 0;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.t-storage-file-page__table {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
}
.t-storage-file-page__pagination {
  flex-shrink: 0;
  display: flex;
  justify-content: flex-end;
  padding: 12px 4px 4px;
}
.t-storage-file-page__name-link {
  cursor: pointer;
}
.t-storage-file-page__name-link:hover {
  color: var(--tnzi-primary);
  text-decoration: underline;
}
.t-storage-file-page__refs {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-storage-file-page__ref {
  padding: 8px 10px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
</style>
