<template>
  <div class="t-storage-file-page t-page-scroll">
    <NCard :title="t('title')" :bordered="false">
      <template #header-extra>
        <NSpace>
          <NButton size="small" @click="loadFolders">{{ t('refresh') }}</NButton>
          <NButton size="small" @click="openCreateFolder(null)">
            {{ t('newRootFolder') }}
          </NButton>
          <TChunkFileUpload
            :uploader="bridge.files"
            :translate="t"
            @success="onUploadSuccess"
            @error="onUploadError"
          />
        </NSpace>
      </template>

      <div class="t-storage-file-page__layout">
        <!-- Left: folder tree -->
        <aside class="t-storage-file-page__tree">
          <NInput
            v-model:value="folderFilter"
            :placeholder="t('folderFilter')"
            size="small"
            clearable
          />
          <NSpin :show="foldersLoading" style="margin-top: 8px">
            <ul class="t-storage-file-page__root-list">
              <li
                class="t-storage-file-page__root-item"
                :class="{ 'is-active': scope === 'all' }"
                @click="selectScope('all')"
              >
                <span class="t-storage-file-page__root-icon">📂</span>
                {{ t('scope.all') }}
              </li>
              <li
                class="t-storage-file-page__root-item"
                :class="{ 'is-active': scope === 'unfiled' }"
                @click="selectScope('unfiled')"
              >
                <span class="t-storage-file-page__root-icon">📥</span>
                {{ t('scope.unfiled') }}
              </li>
            </ul>
            <div class="t-storage-file-page__tree-divider"></div>
            <NTree
              v-if="treeData.length"
              :data="treeData"
              :pattern="folderFilter"
              :selected-keys="selectedFolderId ? [selectedFolderId] : []"
              :default-expand-all="true"
              :show-irrelevant-nodes="false"
              block-line
              class="t-storage-file-page__naive-tree"
              @update:selected-keys="onSelectFolder"
            />
            <div v-else-if="!foldersLoading" class="t-storage-file-page__empty">
              {{ t('noFolders') }}
            </div>
          </NSpin>
        </aside>

        <!-- Right: file list -->
        <section class="t-storage-file-page__main">
          <header class="t-storage-file-page__main-header">
            <div>
              <h3 class="t-storage-file-page__scope-title">{{ scopeLabel }}</h3>
              <span class="t-storage-file-page__scope-hint">
                {{ t('fileCount', { n: totalFiles }) }}
              </span>
            </div>
            <NSpace v-if="selectedFolderId" size="small">
              <NButton size="small" @click="openCreateFolder(selectedFolderId)">
                {{ t('newSubFolder') }}
              </NButton>
              <NButton size="small" @click="openRenameFolder">
                {{ t('renameFolder') }}
              </NButton>
              <NPopconfirm
                :disabled="!canDeleteSelectedFolder"
                @positive-click="deleteSelectedFolder"
              >
                <template #trigger>
                  <NButton
                    size="small"
                    type="error"
                    ghost
                    :disabled="!canDeleteSelectedFolder"
                    :title="canDeleteSelectedFolder ? '' : t('cannotDeleteNonEmpty')"
                  >
                    {{ t('deleteFolder') }}
                  </NButton>
                </template>
                {{ t('confirmDeleteFolder') }}
              </NPopconfirm>
            </NSpace>
          </header>

          <div class="t-storage-file-page__search">
            <NInput
              v-model:value="search.originalName"
              :placeholder="t('search.originalName')"
              size="small"
              clearable
              @change="applyFilters"
            />
            <NSelect
              v-model:value="search.contentType"
              :options="contentTypeOptions"
              :placeholder="t('search.contentType')"
              size="small"
              clearable
              @update:value="applyFilters"
            />
            <NButton size="small" type="primary" @click="applyFilters">
              {{ t('search.search') }}
            </NButton>
          </div>

          <div v-if="selectedIds.length" class="t-storage-file-page__batch">
            <span>{{ t('selected', { n: selectedIds.length }) }}</span>
            <NSelect
              v-model:value="moveTarget"
              :options="moveTargetOptions"
              :placeholder="t('moveToPlaceholder')"
              size="small"
              clearable
              filterable
              style="width: 260px"
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

          <NSpin :show="filesLoading">
            <NDataTable
              :data="files"
              :columns="columns"
              :row-key="(r: FileRecordDto) => r.id"
              :checked-row-keys="selectedIds"
              size="small"
              :bordered="false"
              :max-height="540"
              @update:checked-row-keys="(keys: Array<string | number>) => selectedIds = keys.map(String)"
            />
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
          </NSpin>
        </section>
      </div>
    </NCard>

    <!-- Create / rename folder modal -->
    <NModal
      v-model:show="folderModal.show"
      :title="folderModal.mode === 'rename' ? t('renameFolder') : t('newFolder')"
      preset="card"
      style="width: 460px"
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
        <div style="display: flex; justify-content: flex-end; gap: 8px">
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
  </div>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, onMounted } from 'vue'
import type { TreeOption, DataTableColumns } from 'naive-ui'
import {
  NCard, NSpace, NButton, NTree, NInput, NSelect, NSpin, NDataTable, NPagination,
  NModal, NForm, NFormItem, NInputNumber, NPopconfirm, useMessage,
} from 'naive-ui'
import TChunkFileUpload from '../../components/data/TChunkFileUpload.vue'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import type { FileRecordDto, FileFolderDto } from '@tnzi/core/services/storage'

type Scope = 'all' | 'unfiled' | 'folder'

const bridge = createStorageBridge({ client: useAdminClient() })
const t = makePageTranslator('storage.files')

let message: { success(s: string): void; error(s: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {} }
}

// ---- State ----------------------------------------------------------------

const folders = ref<FileFolderDto[]>([])
const files = ref<FileRecordDto[]>([])
const totalFiles = ref(0)
const scope = ref<Scope>('all')
const selectedFolderId = ref<string | null>(null)
const folderFilter = ref('')
const pageIndex = ref(1)
const pageSize = ref(20)
const foldersLoading = ref(false)
const filesLoading = ref(false)
const moving = ref(false)
const deleting = ref(false)
const folderSaving = ref(false)
const selectedIds = ref<string[]>([])
/**
 * NSelect doesn't accept `null` as an option value, so the "to root" target
 * is represented by the sentinel below. `undefined` = no selection yet.
 */
const ROOT_TARGET = '__ROOT__'
const moveTarget = ref<string | undefined>(undefined)

const search = reactive({
  originalName: '',
  contentType: '',
})

const folderModal = reactive({
  show: false,
  mode: 'create' as 'create' | 'rename',
  parentId: null as string | null,
  form: { name: '', description: '' as string | undefined, sortOrder: 0 },
})

// ---- Derived --------------------------------------------------------------

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

const selectedFolder = computed(() =>
  selectedFolderId.value ? flatFolders.value.get(selectedFolderId.value) ?? null : null,
)

/**
 * The backend rejects deletion of a non-empty folder. Pre-check both direct
 * children (`children`) and direct files (`fileCount`) so the UI can disable
 * the delete button + tooltip the reason instead of relying on a 4xx round trip.
 */
const canDeleteSelectedFolder = computed(() => {
  const f = selectedFolder.value
  if (!f) return false
  if ((f.fileCount ?? 0) > 0) return false
  if (f.children && f.children.length > 0) return false
  return true
})

const scopeLabel = computed(() => {
  if (scope.value === 'all') return t('scope.allTitle')
  if (scope.value === 'unfiled') return t('scope.unfiledTitle')
  return selectedFolder.value?.name ?? '—'
})

const treeData = computed<TreeOption[]>(() => {
  const adapt = (nodes: FileFolderDto[]): TreeOption[] =>
    nodes.map((n) => ({
      key: n.id,
      label: n.name,
      children: n.children?.length ? adapt(n.children) : undefined,
      suffix: () =>
        h(
          'span',
          { style: 'color: var(--tnzi-base-text-muted); font-size: 11px; margin-left: 4px' },
          `(${n.fileCount})`,
        ),
    }))
  return adapt(folders.value)
})

const moveTargetOptions = computed(() => {
  const opts: Array<{ label: string; value: string }> = [
    { label: t('scope.unfiled'), value: ROOT_TARGET },
  ]
  for (const [id, f] of flatFolders.value) {
    opts.push({ label: f.path || f.name, value: id })
  }
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

const columns = computed<DataTableColumns<FileRecordDto>>(() => [
  { type: 'selection' },
  {
    key: 'originalName',
    title: t('columns.originalName'),
    minWidth: 200,
    ellipsis: { tooltip: true },
  },
  {
    key: 'size',
    title: t('columns.size'),
    width: 100,
    render: (row) => formatSize(row.size),
  },
  { key: 'contentType', title: t('columns.contentType'), width: 160, ellipsis: { tooltip: true } },
  { key: 'creatorName', title: t('columns.creatorName'), width: 140 },
  {
    key: 'creationTime',
    title: t('columns.creationTime'),
    width: 160,
    render: (row) => (row.creationTime ? new Date(row.creationTime).toLocaleString() : '—'),
  },
  {
    key: 'actions',
    title: t('columns.actions'),
    width: 100,
    render: (row) =>
      h(
        NButton,
        {
          size: 'small',
          ghost: true,
          onClick: () => downloadFile(row),
        },
        { default: () => t('actions.download') },
      ),
  },
])

// ---- Actions --------------------------------------------------------------

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

function buildFilters(): Record<string, unknown> {
  const f: Record<string, unknown> = {}
  if (search.originalName.trim()) f.originalName = search.originalName.trim()
  if (search.contentType) f.contentType = search.contentType
  if (scope.value === 'unfiled') {
    f.includeUnfiled = true
  } else if (scope.value === 'folder' && selectedFolderId.value) {
    f.folderId = selectedFolderId.value
  }
  return f
}

function selectScope(s: Scope): void {
  scope.value = s
  selectedFolderId.value = null
  pageIndex.value = 1
  void loadFiles()
}

function onSelectFolder(keys: Array<string | number>): void {
  const key = keys[0] as string | undefined
  if (!key) {
    selectedFolderId.value = null
    return
  }
  selectedFolderId.value = key
  scope.value = 'folder'
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

function downloadFile(row: FileRecordDto): void {
  const url = bridge.files.downloadUrl(row.id)
  window.open(url, '_blank')
}

async function batchMoveFiles(): Promise<void> {
  if (!selectedIds.value.length || moveTarget.value === undefined) return
  moving.value = true
  try {
    const target = moveTarget.value === ROOT_TARGET ? null : moveTarget.value
    await bridge.files.moveTo(selectedIds.value, target)
    message.success(t('moveSuccess'))
    await Promise.all([loadFolders(), loadFiles()])
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
    await Promise.all([loadFolders(), loadFiles()])
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    deleting.value = false
  }
}

function openCreateFolder(parentId: string | null): void {
  folderModal.mode = 'create'
  folderModal.parentId = parentId
  folderModal.form = { name: '', description: undefined, sortOrder: 0 }
  folderModal.show = true
}

function openRenameFolder(): void {
  if (!selectedFolder.value) return
  folderModal.mode = 'rename'
  folderModal.parentId = selectedFolder.value.parentId ?? null
  folderModal.form = {
    name: selectedFolder.value.name,
    description: selectedFolder.value.description ?? undefined,
    sortOrder: selectedFolder.value.sortOrder,
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
      selectedFolderId.value = created.id
      scope.value = 'folder'
      pageIndex.value = 1
      await loadFiles()
    } else if (selectedFolder.value) {
      await bridge.folders.update(selectedFolder.value.id, {
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

async function deleteSelectedFolder(): Promise<void> {
  if (!selectedFolder.value) return
  try {
    await bridge.folders.delete(selectedFolder.value.id)
    message.success(t('deleteFolderSuccess'))
    selectedFolderId.value = null
    scope.value = 'all'
    await Promise.all([loadFolders(), loadFiles()])
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

function onUploadSuccess(_result: { url: string }): void {
  void loadFiles()
  void loadFolders()
}

function onUploadError(_error: Error): void {
  // Inline status surfaced by TChunkFileUpload — keep page state untouched
}

onMounted(async () => {
  await loadFolders()
  await loadFiles()
})
</script>

<style scoped>
.t-storage-file-page {
  padding: 16px;
}
.t-storage-file-page__layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 16px;
  min-height: 540px;
}
.t-storage-file-page__tree {
  border-right: 1px solid var(--tnzi-base-border, #efeff5);
  padding-right: 16px;
}
.t-storage-file-page__root-list {
  list-style: none;
  padding: 0;
  margin: 8px 0;
}
.t-storage-file-page__root-item {
  padding: 8px 12px;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  font-size: 13px;
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 4px;
}
.t-storage-file-page__root-item:hover {
  background: var(--tnzi-base-fill, #f5f5f7);
}
.t-storage-file-page__root-item.is-active {
  background: var(--tnzi-primary-color-suppl, rgba(6, 182, 212, 0.12));
  color: var(--tnzi-primary-color, #06B6D4);
}
.t-storage-file-page__root-icon {
  font-size: 14px;
}
.t-storage-file-page__tree-divider {
  height: 1px;
  background: var(--tnzi-base-border, #efeff5);
  margin: 8px 0;
}
.t-storage-file-page__naive-tree {
  max-height: 48vh;
  overflow: auto;
}
.t-storage-file-page__empty {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 16px 8px;
  font-size: 13px;
}
.t-storage-file-page__main {
  padding: 0 4px;
}
.t-storage-file-page__main-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 12px;
  gap: 16px;
}
.t-storage-file-page__scope-title {
  margin: 0;
  font-size: 18px;
}
.t-storage-file-page__scope-hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-storage-file-page__search {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
  align-items: center;
  flex-wrap: wrap;
}
.t-storage-file-page__search > :nth-child(1) {
  flex: 1;
  min-width: 200px;
}
.t-storage-file-page__search > :nth-child(2) {
  width: 200px;
}
.t-storage-file-page__batch {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 8px;
  padding: 8px 12px;
  background: var(--tnzi-primary-color-suppl, rgba(6, 182, 212, 0.06));
  border-radius: var(--tnzi-admin-radius-md, 4px);
  font-size: 13px;
}
.t-storage-file-page__pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
