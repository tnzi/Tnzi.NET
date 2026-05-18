<template>
  <div>
    <NAlert :type="'info'" :title="t('banner.title')" closable style="margin: 16px 16px 0">
      {{ t('banner.body') }}
    </NAlert>
    <TCrudPage
      :state="crud"
      :all-columns="chunkColumns"
      :title="title"
      :translate="t"
    >
      <template #form="{ formData, mode }">
        <TFormSchemaRenderer
          :schema="chunkFormSchema"
          :model="(formData ?? {}) as Record<string, unknown>"
          :readonly="mode === 'view'"
        />
      </template>
    </TCrudPage>
  </div>
</template>

<script setup lang="ts">
import { NAlert } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createStorageBridge, type FileChunkAuditDto } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { chunkColumns, chunkFormSchema } from './chunk-config'
import { translatePageKey } from '../_shared/translate'

const title = 'title'
// Wired to /admin/storage/audit/chunks via DefaultStorageAuditAdminController
// (Plan E, 2026-04-14). Supports optional uploadSessionId filter via query.filters.
const bridge = createStorageBridge({ client: useAdminClient() })

const readOnlyFn = async (): Promise<never> => { throw new Error('Chunk: read-only resource') }

const crud = useCrudPage<FileChunkAuditDto>({
  pageId: 'storage.chunks',
  columns: chunkColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.chunks.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: (ids) => bridge.chunks.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('storage.chunks', key)
</script>
