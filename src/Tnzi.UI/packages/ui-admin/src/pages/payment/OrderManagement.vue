<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'orderNumber', title: 'Order Number', sortable: true },
  { key: 'amount', title: 'Amount', sortable: true },
  { key: 'status', title: 'Status', sortable: true },
  { key: 'userName', title: 'User', sortable: true },
  { key: 'creationTime', title: 'Created At', sortable: true },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Order Management"
    :columns="columns"
    :fetch-fn="fetchData"
    row-key="id"
    searchable
    :creatable="false"
    :editable="false"
    :deletable="false"
  />
</template>
