<template>
  <!-- Phase J overhaul (2026-05-18): banner moved to the title (i) popover. -->
  <TCrudPage
    :state="crud"
    :all-columns="versionColumns"
    :search-fields="versionSearchFields"
    :show-default-search="false"
    :title="title"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
    :translate="t"
    :row-actions="rowActions"
  >
    <!-- Read-only view drawer (a `#detail` slot mounts it even on a page with no
         create/update callbacks; a `#form` slot would not). -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="versionFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        :readonly="true"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { downloadBlob } from '@tnzi/core'
import { versionedFileName } from '@tnzi/core/services/storage'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { viewAction, type RowAction } from '../../headless/row-actions'
import { createStorageBridge, type FileVersionAuditDto } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { versionColumns, versionFormSchema, versionSearchFields } from './version-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'

const title = 'title'
// Wired to /admin/storage/audit/versions (Plan E, 2026-04-14). Restore, download
// and delete reuse the user-side /files/{id}/versions/{v}/... routes via the bridge.
const bridge = createStorageBridge({ client: useAdminClient() })
const t = makePageTranslator('storage.versions')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const crud = useCrudPage<FileVersionAuditDto>({
  pageId: 'storage.versions',
  columns: versionColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.versions.fetch(query),
  // Versions are produced by the file lifecycle - no create/edit form here; the
  // admin operations are the per-row actions below.
})

// Restore and delete live in the per-row More menu - admins almost always act
// on a single version at a time. Both are disabled for the `isCurrent` row:
// restoring it is a no-op that emits a confusing audit log entry, and the
// server refuses to delete it. Download needs only read access (the page's own
// storage.file.view gate); the two writes reach the backend's write check.
const rowActions: RowAction<FileVersionAuditDto>[] = [
  viewAction(crud),
  {
    key: 'download',
    label: 'actions.download',
    onClick: (row) => void handleDownload(row),
  },
  {
    key: 'restore',
    label: 'actions.restore',
    show: () => can('storage.file.update'),
    disabled: (row) => row.isCurrent === true,
    confirm: (row) => t('actions.confirmRestore', { n: row.version }),
    onClick: (row) => void handleRestore(row),
  },
  {
    key: 'deleteVersion',
    label: 'actions.delete',
    type: 'error',
    show: () => can('storage.file.update'),
    disabled: (row) => row.isCurrent === true,
    confirm: (row) => t('actions.confirmDelete', { n: row.version }),
    onClick: (row) => void handleDelete(row),
  },
]

function hasVersionKey(row: FileVersionAuditDto): boolean {
  if (row.fileId && row.version != null) return true
  message.error(t('actions.invalidRow'))
  return false
}

async function handleRestore(row: FileVersionAuditDto): Promise<void> {
  if (!hasVersionKey(row)) return
  try {
    await bridge.versions.restore(row.fileId, row.version)
    message.success(t('actions.restoreSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

/**
 * The audit row carries no file name, and a version saved as `<guid>.v2` is
 * useless to whoever opens it, so the record is read alongside the bytes and the
 * copy is saved as `<original name>.v<n>.<ext>` (the backend's own naming).
 * Both requests go through the client: the route needs a signed-in caller,
 * which a plain `<a href>` cannot be.
 */
async function handleDownload(row: FileVersionAuditDto): Promise<void> {
  if (!hasVersionKey(row)) return
  try {
    const [blob, record] = await Promise.all([
      bridge.versions.download(row.fileId, row.version),
      bridge.files.get(row.fileId),
    ])
    downloadBlob(blob, versionedFileName(record.originalName || record.fileName, row.version))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function handleDelete(row: FileVersionAuditDto): Promise<void> {
  if (!hasVersionKey(row)) return
  try {
    await bridge.versions.delete(row.fileId, row.version)
    message.success(t('actions.deleteSuccess', { n: row.version }))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
</script>
