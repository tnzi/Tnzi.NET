<template>
  <!-- Phase J overhaul (2026-05-18): removed the top-of-page NAlert banner.
       Diagnostic context lives in the (i) popover next to the title now —
       see TCrudPage `titleHelp` prop. The banner was always-visible chrome
       that pushed the data table down on every visit; the popover is just
       as discoverable on hover/click and stays out of the way otherwise. -->
  <!-- Read-only diagnostics view: chunks are owned by the upload lifecycle.
       No create/update/delete callbacks → affordances auto-hidden. -->
  <TCrudPage
    :state="crud"
    :all-columns="chunkColumns"
    :title="title"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="chunkFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
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

const crud = useCrudPage<FileChunkAuditDto>({
  pageId: 'storage.chunks',
  columns: chunkColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.chunks.fetch(query),
  // Chunks are owned by the upload lifecycle — no admin write surface.
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('storage.chunks', key)
</script>
