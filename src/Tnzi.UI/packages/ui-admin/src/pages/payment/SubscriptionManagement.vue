<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'planName', title: 'Plan Name', sortable: true },
  { key: 'status', title: 'Status', sortable: true },
  { key: 'startDate', title: 'Start Date', sortable: true },
  { key: 'endDate', title: 'End Date', sortable: true },
  { key: 'userName', title: 'User', sortable: true },
]

const subscriptionStatuses = ['Active', 'Cancelled', 'Expired', 'Pending', 'Paused']

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Subscription Management"
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
          <label class="block text-sm font-medium mb-1">Plan Name</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="text"
            :value="(data as any).planName"
            placeholder="Plan Name"
            @input="(e: Event) => { (data as any).planName = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Status</label>
          <select
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            :value="(data as any).status"
            @change="(e: Event) => { (data as any).status = (e.target as HTMLSelectElement).value }"
          >
            <option value="">-- Select Status --</option>
            <option v-for="s in subscriptionStatuses" :key="s" :value="s">{{ s }}</option>
          </select>
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">Start Date</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="date"
            :value="(data as any).startDate"
            @input="(e: Event) => { (data as any).startDate = (e.target as HTMLInputElement).value }"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-1">End Date</label>
          <input
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            type="date"
            :value="(data as any).endDate"
            @input="(e: Event) => { (data as any).endDate = (e.target as HTMLInputElement).value }"
          />
        </div>
      </div>
    </template>
  </CrudPage>
</template>
