<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'name', title: 'Name', sortable: true },
  { key: 'description', title: 'Description' },
  { key: 'isDefault', title: 'Default' },
  { key: 'creationTime', title: 'Created At', sortable: true },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Role Management"
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
          <label class="block text-sm font-medium mb-1">Description</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).description"
            placeholder="Description"
            @input="(e: Event) => { (data as any).description = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div class="flex items-center gap-2">
          <input
            id="isDefault"
            type="checkbox"
            :checked="(data as any).isDefault"
            @change="(e: Event) => { (data as any).isDefault = (e.target as HTMLInputElement).checked }"
          />
          <label for="isDefault" class="text-sm font-medium">Default Role</label>
        </div>
      </div>
    </template>
  </CrudPage>
</template>
