<template>
  <TCrudPage
    :state="crud"
    :all-columns="versionColumns"
    :title="title"
    :translate="t"
  >
    <!-- Restore action: available when exactly 1 row is selected -->
    <template #batchActions="{ selectedIds }">
      <NButton
        v-if="selectedIds.length === 1"
        class="t-version-restore"
        type="warning"
        size="small"
        :loading="restoring"
        @click="restoreVersion(String(selectedIds[0]))"
      >
        Restore This Version
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="versionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createStorageBridge, type FileVersionAuditDto } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { versionColumns, versionFormSchema } from './version-config'
import { translatePageKey } from '../_shared/translate'

const title = 'File Version History'
// Wired to /admin/storage/audit/versions via DefaultStorageAuditAdminController
// (Plan E, 2026-04-14). Supports optional fileId + currentOnly filters.
const bridge = createStorageBridge({ client: useAdminClient() })
const restoring = ref(false)

const readOnlyFn = async (): Promise<never> => { throw new Error('Version: read-only resource') }

const crud = useCrudPage<FileVersionAuditDto>({
  pageId: 'storage.versions',
  columns: versionColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.versions.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: readOnlyFn,
})


crud.refresh().catch(() => undefined)

async function restoreVersion(id: string): Promise<void> {
  restoring.value = true
  try {
    await bridge.versions.restore(id)
    await crud.refresh()
  } finally {
    restoring.value = false
  }
}

const t = (key: string) => translatePageKey('storage.versions', key)
</script>
