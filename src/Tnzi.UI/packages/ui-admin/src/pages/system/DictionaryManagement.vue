<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'name', title: 'Name', sortable: true },
  { key: 'code', title: 'Code' },
  { key: 'value', title: 'Value' },
  { key: 'sortOrder', title: 'Sort Order' },
]

const formFields = [
  { key: 'name', type: 'text' as const, label: 'Name', required: true },
  { key: 'code', type: 'text' as const, label: 'Code', required: true },
  { key: 'value', type: 'text' as const, label: 'Value', required: true },
  { key: 'description', type: 'text' as const, label: 'Description', required: false },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Dictionary Management"
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
        <div v-for="field in formFields" :key="field.key">
          <label class="block text-sm font-medium mb-1">{{ field.label }}</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            :type="field.type"
            :value="(data as any)[field.key]"
            :placeholder="field.label"
            @input="(e: Event) => { (data as any)[field.key] = (e.target as HTMLInputElement).value }"
          />
        </div>
      </div>
    </template>
  </CrudPage>
</template>
