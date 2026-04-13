<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'title', title: 'Title', sortable: true },
  { key: 'type', title: 'Type' },
  { key: 'isRead', title: 'Read' },
  { key: 'creationTime', title: 'Created At', sortable: true },
]

const notificationTypes = ['System', 'Alert', 'Info', 'Warning', 'Success']

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Notification Management"
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
          <label class="block text-sm font-medium mb-1">Title</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).title"
            placeholder="Title"
            @input="(e: Event) => { (data as any).title = (e.target as HTMLInputElement).value }"
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
            <option v-for="t in notificationTypes" :key="t" :value="t">{{ t }}</option>
          </select>
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Content</label>
          <textarea
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            rows="4"
            :value="(data as any).content"
            placeholder="Notification content..."
            @input="(e: Event) => { (data as any).content = (e.target as HTMLTextAreaElement).value }"
          />
        </div>
      </div>
    </template>
  </CrudPage>
</template>
