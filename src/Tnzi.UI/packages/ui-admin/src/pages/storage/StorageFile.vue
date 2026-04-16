<template>
  <TCrudPage
    :state="crud"
    :all-columns="fileColumns"
    :title="title"
    :translate="t"
  >
    <!-- Replace Create button with chunked uploader -->
    <template #primary>
      <TChunkFileUpload
        :uploader="bridge.files"
        :translate="t"
        @success="onUploadSuccess"
        @error="onUploadError"
      />
    </template>

    <!-- Download row action -->
    <template #rowActions="{ row }">
      <NButton size="tiny" @click="downloadFile(row as FileRecordDto)">
        Download
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="fileFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { NButton } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TChunkFileUpload from '../../components/data/TChunkFileUpload.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { fileColumns, fileFormSchema } from './file-config'
import { translatePageKey } from '../_shared/translate'
import type { FileRecordDto } from '@tnzi/core/services/storage'

const title = 'File Management'
const bridge = createStorageBridge({ client: useAdminClient() })

const readOnlyFn = async (): Promise<never> => { throw new Error('File: use chunked upload instead of create') }

const crud = useCrudPage<FileRecordDto>({
  pageId: 'storage.files',
  columns: fileColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.files.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: (ids) => bridge.files.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

function onUploadSuccess(_result: { url: string }): void {
  crud.refresh().catch(() => undefined)
}

function onUploadError(_error: Error): void {
  // Error surfaced by TChunkFileUpload itself
}

function downloadFile(row: FileRecordDto): void {
  const url = bridge.files.downloadUrl(row.id)
  window.open(url, '_blank')
}

const t = (key: string) => translatePageKey('storage.files', key)
</script>
