<template>
  <!-- Phase J overhaul (2026-05-18): banner moved to the title (i) popover. -->
  <TCrudPage
    :state="crud"
    :all-columns="versionColumns"
    :search-fields="versionSearchFields"
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
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { viewAction, type RowAction } from '../../headless/rowActions'
import { createStorageBridge, type FileVersionAuditDto } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { versionColumns, versionFormSchema, versionSearchFields } from './version-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'

const title = 'title'
// Wired to /admin/storage/audit/versions (Plan E, 2026-04-14). Restore reuses
// the user-side /files/{id}/versions/{v}/restore endpoint via the bridge.
const bridge = createStorageBridge({ client: useAdminClient() })
const t = makePageTranslator('storage.versions')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const crud = useCrudPage<FileVersionAuditDto>({
  pageId: 'storage.versions',
  columns: versionColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.versions.fetch(query),
  // Versions are produced by the file lifecycle - read-only here; the only
  // admin operation is the per-row Restore action below.
})

// Restore lives in the per-row More menu - admins almost always restore a
// single version at a time. Disabled for rows that are already `isCurrent`
// since restoring the current version is a no-op that emits a confusing
// audit log entry.
const rowActions: RowAction<FileVersionAuditDto>[] = [
  viewAction(crud),
  {
    key: 'restore',
    label: 'actions.restore',
    show: () => can('storage.file.update'),
    disabled: (row) => row.isCurrent === true,
    onClick: (row) => void handleRestore(row),
  },
]

async function handleRestore(row: FileVersionAuditDto): Promise<void> {
  if (!row.fileId || row.version == null) {
    message.error(t('actions.restoreInvalid'))
    return
  }
  try {
    await bridge.versions.restore(row.fileId, row.version)
    message.success(t('actions.restoreSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
</script>
