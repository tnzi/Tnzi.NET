<script setup lang="ts">
/**
 * `TFileVersionHistory` - one file's version history, shown in the file
 * manager's detail drawer: which version is current, its size, when and by whom
 * it was stored, its note, and the per-version operations (download, restore,
 * delete) plus "upload a new version".
 *
 * It owns its own requests: the drawer is the only place it lives, and the
 * history has a lifecycle of its own (a slower answer for the previous file
 * must not land on the next one, the "this host keeps no versions" state,
 * one busy row at a time) that has nothing to do with the rest of `Files.vue`.
 *
 * The parent mounts it only once the `storage-workspace` module is known to be
 * loaded, so a host without `Tnzi.Storage.Workspace` never sends the request.
 * A backend too old to report its modules still gets a quiet line instead of an
 * error toast: the bridge maps that 501 to `null`.
 *
 * Emits `changed` with the file's id after an upload or a restore: the file's
 * current content (and with it the size and thumbnail the listing shows) moved.
 * The id travels with the event because the drawer may already show another
 * file by the time the request finishes. Deleting a version that is not current
 * changes nothing the listing shows, so it does not emit.
 *
 * Every operation captures the file it started on before its first `await`, for
 * the same reason: the name a downloaded version is saved under, the id a write
 * goes to and the id `changed` reports must all belong to that file.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NEmpty, NInput, NPopconfirm, NSpin, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { downloadBlob, formatDateTime, formatFileSize } from '@tnzi/core'
import { versionedFileName, type FileRecordDto, type FileVersionDto } from '@tnzi/core/services/storage'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { createStorageBridge } from '../../../services/bridges/storage-bridge'
import { useAdminClient } from '../../../plugin/client'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { useSafeMessage } from '../../_shared/safe-message'

const props = defineProps<{
  file: FileRecordDto
  /** The page translator (`storage.files`); this component's keys live under `versions.*`. */
  translate: (key: string, params?: Record<string, unknown>) => string
}>()

const emit = defineEmits<{ (e: 'changed', fileId: string): void }>()

const bridge = createStorageBridge({ client: useAdminClient() })
const message = useSafeMessage()
const { can } = usePermissionGuard()

function tv(key: string, params?: Record<string, unknown>): string {
  return props.translate(`versions.${key}`, params)
}

function errorText(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

// Upload / restore / delete all reach the backend's write check (owner or
// storage.file.update); the console gates them on the permission code, the same
// code the Versions audit page and the drawer's visibility switch use.
const canWrite = computed(() => can('storage.file.update'))

// ---- history ----
const versions = ref<FileVersionDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const unavailable = ref(false)

/** Only the newest request may write the list: the drawer can move to another file mid-flight. */
let loadSeq = 0

async function load(): Promise<void> {
  const seq = ++loadSeq
  loading.value = true
  loadError.value = null
  try {
    const items = await bridge.versions.list(props.file.id)
    if (seq !== loadSeq) return
    unavailable.value = items === null
    versions.value = items ?? []
  } catch (e) {
    if (seq !== loadSeq) return
    versions.value = []
    loadError.value = errorText(e)
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

// ---- upload a new version ----
const fileInput = ref<HTMLInputElement | null>(null)
const pendingFile = ref<File | null>(null)
const description = ref('')
const uploading = ref(false)

function pickFile(): void {
  fileInput.value?.click()
}

function onFilePicked(e: Event): void {
  const input = e.target as HTMLInputElement
  pendingFile.value = input.files?.[0] ?? null
  // Clear so picking the same file again still fires `change`.
  input.value = ''
}

function resetUpload(): void {
  pendingFile.value = null
  description.value = ''
}

async function submitUpload(): Promise<void> {
  const chosen = pendingFile.value
  if (!chosen || uploading.value) return
  const fileId = props.file.id
  uploading.value = true
  try {
    const created = await bridge.versions.upload(fileId, chosen, description.value.trim() || undefined)
    message.success(tv('uploadSuccess', { n: created.version }))
    resetUpload()
    await load()
    emit('changed', fileId)
  } catch (e) {
    message.error(errorText(e))
  } finally {
    uploading.value = false
  }
}

// ---- per-version operations ----
type VersionOp = 'download' | 'restore' | 'delete'

/** One operation at a time: a restore racing a delete on the same history helps nobody. */
const busy = ref<{ version: number; op: VersionOp } | null>(null)

function isBusy(v: FileVersionDto, op: VersionOp): boolean {
  return busy.value?.version === v.version && busy.value.op === op
}

async function runOp(v: FileVersionDto, op: VersionOp, work: () => Promise<void>): Promise<void> {
  if (busy.value) return
  busy.value = { version: v.version, op }
  try {
    await work()
  } catch (e) {
    message.error(errorText(e))
  } finally {
    busy.value = null
  }
}

function download(v: FileVersionDto): Promise<void> {
  const file = props.file
  return runOp(v, 'download', async () => {
    const blob = await bridge.versions.download(file.id, v.version)
    downloadBlob(blob, versionedFileName(file.originalName || file.fileName, v.version))
  })
}

function restore(v: FileVersionDto): Promise<void> {
  const fileId = props.file.id
  return runOp(v, 'restore', async () => {
    await bridge.versions.restore(fileId, v.version)
    message.success(tv('restoreSuccess', { n: v.version }))
    await load()
    emit('changed', fileId)
  })
}

function remove(v: FileVersionDto): Promise<void> {
  const fileId = props.file.id
  return runOp(v, 'delete', async () => {
    await bridge.versions.delete(fileId, v.version)
    message.success(tv('deleteSuccess', { n: v.version }))
    await load()
  })
}

watch(
  () => props.file.id,
  () => {
    resetUpload()
    void load()
  },
  { immediate: true },
)
</script>

<template>
  <section class="t-file-versions">
    <header class="t-file-versions__head">
      <p class="t-file-versions__title">{{ tv('title') }}</p>
      <NButton
        v-if="canWrite && !unavailable"
        size="tiny"
        secondary
        :disabled="uploading || !!pendingFile"
        @click="pickFile"
      >
        <template #icon><TSvgIcon icon="mdi:file-upload-outline" :size="14" /></template>
        {{ tv('upload') }}
      </NButton>
      <input ref="fileInput" type="file" class="t-file-versions__input" @change="onFilePicked" />
    </header>

    <!-- A version replaces the record's bytes, so every holder of the record sees the
         new content: other entities that reference it, and other uploaders whose
         identical bytes were stored once (MD5 dedup). Say so before anyone writes. -->
    <p v-if="canWrite && !unavailable && file.referenceCount > 1" class="t-file-versions__muted t-file-versions__shared">
      {{ tv('sharedHint', { n: file.referenceCount }) }}
    </p>

    <div v-if="pendingFile" class="t-file-versions__pending">
      <div class="t-file-versions__pending-file">
        <span class="t-file-versions__pending-name" :title="pendingFile.name">{{ pendingFile.name }}</span>
        <span class="t-file-versions__muted">{{ formatFileSize(pendingFile.size) }}</span>
      </div>
      <NInput
        v-model:value="description"
        size="small"
        :maxlength="500"
        :placeholder="tv('descriptionPlaceholder')"
      />
      <p class="t-file-versions__muted">{{ tv('uploadHint') }}</p>
      <div class="t-file-versions__pending-actions">
        <NButton size="small" :disabled="uploading" @click="resetUpload">{{ translate('cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="uploading" @click="submitUpload">{{ tv('submit') }}</NButton>
      </div>
    </div>

    <NSpin :show="loading" size="small">
      <p v-if="unavailable" class="t-file-versions__muted">{{ tv('unavailable') }}</p>
      <div v-else-if="loadError" class="t-file-versions__error">
        <span>{{ loadError }}</span>
        <NButton size="tiny" text type="primary" @click="load">{{ tv('retry') }}</NButton>
      </div>
      <NEmpty
        v-else-if="!versions.length && !loading"
        size="small"
        :description="tv('empty')"
        class="t-file-versions__empty"
      />
      <ol v-else class="t-file-versions__list">
        <li
          v-for="v in versions"
          :key="v.id"
          class="t-file-versions__item"
          :class="{ 'is-current': v.isCurrent }"
        >
          <div class="t-file-versions__line">
            <span class="t-file-versions__label">{{ tv('label', { n: v.version }) }}</span>
            <NTag v-if="v.isCurrent" size="small" type="success" :bordered="false">{{ tv('current') }}</NTag>
            <span class="t-file-versions__size">{{ formatFileSize(v.size) }}</span>
          </div>
          <div class="t-file-versions__muted t-file-versions__meta">
            <span>{{ formatDateTime(v.creationTime, { fallback: EMPTY_DASH }) }}</span>
            <span v-if="v.creatorId" class="t-file-versions__creator" :title="v.creatorId">
              <TSvgIcon icon="mdi:account-outline" :size="12" />{{ v.creatorId }}
            </span>
          </div>
          <p v-if="v.description" class="t-file-versions__desc">{{ v.description }}</p>
          <div class="t-file-versions__actions">
            <NButton
              size="tiny"
              quaternary
              :loading="isBusy(v, 'download')"
              :disabled="busy !== null"
              @click="download(v)"
            >
              <template #icon><TSvgIcon icon="mdi:download" :size="14" /></template>
              {{ tv('download') }}
            </NButton>
            <template v-if="canWrite && !v.isCurrent">
              <NPopconfirm @positive-click="restore(v)">
                <template #trigger>
                  <NButton size="tiny" quaternary :loading="isBusy(v, 'restore')" :disabled="busy !== null">
                    <template #icon><TSvgIcon icon="mdi:restore" :size="14" /></template>
                    {{ tv('restore') }}
                  </NButton>
                </template>
                {{ tv('confirmRestore', { n: v.version }) }}
              </NPopconfirm>
              <NPopconfirm @positive-click="remove(v)">
                <template #trigger>
                  <NButton
                    size="tiny"
                    quaternary
                    type="error"
                    :loading="isBusy(v, 'delete')"
                    :disabled="busy !== null"
                  >
                    <template #icon><TSvgIcon icon="mdi:trash-can-outline" :size="14" /></template>
                    {{ tv('delete') }}
                  </NButton>
                </template>
                {{ tv('confirmDelete', { n: v.version }) }}
              </NPopconfirm>
            </template>
          </div>
        </li>
      </ol>
    </NSpin>
  </section>
</template>

<style scoped>
.t-file-versions__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 6px;
}
.t-file-versions__title {
  font-size: 13px;
  font-weight: 600;
  margin: 0;
}
.t-file-versions__input {
  display: none;
}
.t-file-versions__muted {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  margin: 0;
}
.t-file-versions__shared {
  margin-bottom: 6px;
}
.t-file-versions__pending {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 10px;
  padding: 8px 10px;
  border: 1px dashed var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
.t-file-versions__pending-file {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
  font-size: 13px;
}
.t-file-versions__pending-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-file-versions__pending-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.t-file-versions__error {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  color: var(--tnzi-error);
  font-size: 12px;
}
.t-file-versions__empty {
  margin: 8px 0;
}
.t-file-versions__list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-file-versions__item {
  padding: 8px 10px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
.t-file-versions__item.is-current {
  border-color: rgb(var(--tnzi-primary-rgb) / 0.45);
  background: rgb(var(--tnzi-primary-rgb) / 0.04);
}
.t-file-versions__line {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-file-versions__label {
  font-size: 13px;
  font-weight: 600;
}
.t-file-versions__size {
  margin-left: auto;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
}
.t-file-versions__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  margin-top: 2px;
  min-width: 0;
}
.t-file-versions__creator {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  min-width: 0;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-file-versions__desc {
  margin: 4px 0 0;
  font-size: 12px;
  word-break: break-word;
}
.t-file-versions__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 2px 4px;
  margin-top: 4px;
}
</style>
