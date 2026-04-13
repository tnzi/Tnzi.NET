<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'fileName', title: 'File Name', sortable: true },
  { key: 'fileSize', title: 'File Size' },
  { key: 'contentType', title: 'Content Type' },
  { key: 'creationTime', title: 'Uploaded At', sortable: true },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Storage Manager"
    :columns="columns"
    :fetch-fn="fetchData"
    row-key="id"
    searchable
    creatable
    :editable="false"
    deletable
  >
    <template #form-modal="{ data }">
      <div class="space-y-4">
        <div>
          <label class="block text-sm font-medium mb-1">File</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="file"
            @change="(e: Event) => { (data as any).file = (e.target as HTMLInputElement).files?.[0] ?? null }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">File Name (optional override)</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).fileName"
            placeholder="Leave blank to use original name"
            @input="(e: Event) => { (data as any).fileName = (e.target as HTMLInputElement).value }"
          />
        </div>
      </div>
    </template>
  </CrudPage>
</template>
