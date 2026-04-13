<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'name', title: 'Name', sortable: true },
  { key: 'path', title: 'Path' },
  { key: 'icon', title: 'Icon' },
  { key: 'parentId', title: 'Parent ID' },
  { key: 'sortOrder', title: 'Sort Order' },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Menu Management"
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
          <label class="block text-sm font-medium mb-1">Path</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).path"
            placeholder="/path/to/page"
            @input="(e: Event) => { (data as any).path = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Icon</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).icon"
            placeholder="Icon name or class"
            @input="(e: Event) => { (data as any).icon = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Parent Menu</label>
          <select
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            :value="(data as any).parentId"
            @change="(e: Event) => { (data as any).parentId = (e.target as HTMLSelectElement).value || null }"
          >
            <option value="">-- None (Root) --</option>
          </select>
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Sort Order</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="number"
            :value="(data as any).sortOrder"
            placeholder="0"
            @input="(e: Event) => { (data as any).sortOrder = Number((e.target as HTMLInputElement).value) }"
          />
        </div>
      </div>
    </template>
  </CrudPage>
</template>
