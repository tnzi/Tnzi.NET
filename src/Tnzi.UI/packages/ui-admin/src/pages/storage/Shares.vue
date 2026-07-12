<template>
  <!--
    Shares — active share-link management. Read + revoke only: shares are
    created from the file detail / user API, never here. The list is paged
    via `shares.fetch` (filters: fileId / creatorId / includeExpired /
    includeDisabled). Per-row Revoke + batch Revoke both call
    `shares.batchRevoke`. No create/edit affordances (createData/updateData
    omitted → hidden automatically); deleteData IS the revoke wiring so the
    batch toolbar surfaces "Revoke".
  -->
  <TCrudPage
    :state="crud"
    :all-columns="shareColumns"
    :title="t('title')"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
    :search-fields="shareSearchFields"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #batchActions="{ selectedIds }">
      <NPopconfirm v-if="can('storage.file.delete')" @positive-click="() => batchRevoke(selectedIds)">
        <template #trigger>
          <NButton size="small" type="error" ghost :disabled="!selectedIds.length">
            {{ t('actions.revoke') }}
          </NButton>
        </template>
        {{ t('actions.confirmRevoke') }}
      </NPopconfirm>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { NButton, NPopconfirm } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { type RowAction } from '../../headless/rowActions'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildShareColumns, shareSearchFields } from './shares-config'
import type { FileShareSummaryDto } from '@tnzi/core/services/storage'

const t = makePageTranslator('storage.shares')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const bridge = createStorageBridge({ client: useAdminClient() })

const shareColumns = buildShareColumns(t)

const crud = useCrudPage<FileShareSummaryDto, string>({
  pageId: 'storage.shares',
  permission: 'storage.file',
  columns: shareColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.shares.fetch(query),
  // Revoke wired through deleteData so the batch toolbar surfaces it; the
  // per-row Revoke action below reuses the same path.
  deleteData: async (ids) => {
    await bridge.shares.batchRevoke(ids)
  },
})

const rowActions: RowAction<FileShareSummaryDto>[] = [
  {
    key: 'revoke',
    label: 'actions.revoke',
    type: 'error',
    confirm: 'actions.confirmRevoke',
    show: (row) => can('storage.file.delete') && row.isEnabled === true,
    onClick: (row) => void revokeOne(row),
  },
]

async function revokeOne(row: FileShareSummaryDto): Promise<void> {
  try {
    await bridge.shares.batchRevoke([row.id])
    message.success(t('actions.revokeSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function batchRevoke(ids: string[]): Promise<void> {
  if (!ids.length) return
  try {
    const count = await bridge.shares.batchRevoke(ids)
    message.success(t('actions.batchRevokeSuccess', { n: count }))
    crud.batchActions.clear()
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
</script>
