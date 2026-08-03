<template>
  <!-- Phase J overhaul (2026-05-18): removed the top-of-page NAlert banner.
       Diagnostic context lives in the (i) popover next to the title now -
       see TCrudPage `titleHelp` prop. The banner was always-visible chrome
       that pushed the data table down on every visit; the popover is just
       as discoverable on hover/click and stays out of the way otherwise. -->
  <!-- Read-only diagnostics view: chunks are owned by the upload lifecycle.
       No create/update/delete callbacks → affordances auto-hidden. -->
  <TCrudPage
    :state="crud"
    :all-columns="chunkColumns"
    :search-fields="chunkSearchFields"
    :title="title"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
    :translate="t"
    :row-actions="rowActions"
  >
    <!-- Read-only view drawer (the `#detail` slot mounts it even on a page with
         no create/update/delete callbacks; a `#form` slot would not). -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="chunkFormSchema"
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
import { viewAction, type RowAction } from '../../headless/row-actions'
import { createStorageBridge, type FileChunkAuditDto } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { chunkColumns, chunkFormSchema, chunkSearchFields } from './chunk-config'
import { makePageTranslator } from '../_shared/translate'

const title = 'title'
// Wired to /admin/storage/audit/chunks via DefaultStorageAuditAdminController
// (Plan E, 2026-04-14). Supports optional uploadSessionId filter via query.filters.
const bridge = createStorageBridge({ client: useAdminClient() })

const crud = useCrudPage<FileChunkAuditDto>({
  pageId: 'storage.chunks',
  columns: chunkColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.chunks.fetch(query),
  // Chunks are owned by the upload lifecycle - no admin write surface.
})

const t = makePageTranslator('storage.chunks')

// Read-only "View" opens the chunk detail drawer (the already-defined schema).
const rowActions: RowAction<FileChunkAuditDto>[] = [viewAction(crud)]
</script>
