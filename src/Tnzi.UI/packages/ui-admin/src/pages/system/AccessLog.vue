<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'path', title: 'Path', sortable: true },
  { key: 'method', title: 'Method' },
  { key: 'statusCode', title: 'Status Code', sortable: true },
  { key: 'userName', title: 'User', sortable: true },
  { key: 'creationTime', title: 'Created At', sortable: true },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Access Log"
    :columns="columns"
    :fetch-fn="fetchData"
    row-key="id"
    searchable
    :creatable="false"
    :editable="false"
    :deletable="false"
  />
</template>
