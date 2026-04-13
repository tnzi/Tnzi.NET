<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'name', title: 'Name', sortable: true },
  { key: 'code', title: 'Code' },
  { key: 'functionModuleId', title: 'Function Module' },
  { key: 'isEnabled', title: 'Enabled' },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Permission Management"
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
          <label class="block text-sm font-medium mb-1">Code</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).code"
            placeholder="Code"
            @input="(e: Event) => { (data as any).code = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Function Module</label>
          <select
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            :value="(data as any).functionModuleId"
            @change="(e: Event) => { (data as any).functionModuleId = (e.target as HTMLSelectElement).value || null }"
          >
            <option value="">-- Select Module --</option>
          </select>
        </div>
      </div>
    </template>
  </CrudPage>
</template>
