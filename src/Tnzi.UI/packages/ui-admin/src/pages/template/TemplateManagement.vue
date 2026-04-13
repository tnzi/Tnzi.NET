<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'name', title: 'Name', sortable: true },
  { key: 'type', title: 'Type' },
  { key: 'isActive', title: 'Active' },
  { key: 'creationTime', title: 'Created At', sortable: true },
]

const templateTypes = ['Email', 'SMS', 'Push', 'InApp']

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Template Management"
    :columns="columns"
    :fetch-fn="fetchData"
    row-key="id"
    searchable
    creatable
    editable
    deletable
  >
    <template #form-modal="{ data }">
      <div class="space-y-4">
        <div>
          <label class="block text-sm font-medium mb-1">Name</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).name"
            placeholder="Name"
            @input="(e: Event) => { (data as any).name = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Type</label>
          <select
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            :value="(data as any).type"
            @change="(e: Event) => { (data as any).type = (e.target as HTMLSelectElement).value }"
          >
            <option value="">-- Select Type --</option>
            <option v-for="t in templateTypes" :key="t" :value="t">{{ t }}</option>
          </select>
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Content</label>
          <textarea
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            rows="6"
            :value="(data as any).content"
            placeholder="Template content..."
            @input="(e: Event) => { (data as any).content = (e.target as HTMLTextAreaElement).value }"
          />
        </div>
      </div>
    </template>
  </CrudPage>
</template>
