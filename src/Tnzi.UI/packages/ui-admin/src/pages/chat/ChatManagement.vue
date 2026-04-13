<script setup lang="ts">
import CrudPage from '../../components/crud/CrudPage.vue'
import type { CrudPageQuery, CrudPageResult } from '../../headless/useCrudPage'

const columns = [
  { key: 'id', title: 'ID', width: 80 },
  { key: 'senderName', title: 'Sender', sortable: true },
  { key: 'content', title: 'Content' },
  { key: 'type', title: 'Type' },
  { key: 'creationTime', title: 'Sent At', sortable: true },
]

async function fetchData(query: CrudPageQuery): Promise<CrudPageResult<any>> {
  return { items: [], totalCount: 0, pageIndex: query.pageIndex, pageSize: query.pageSize }
}
</script>

<template>
  <CrudPage
    title="Chat Management"
    :columns="columns"
    :fetch-fn="fetchData"
    row-key="id"
    searchable
    :creatable="false"
    :editable="false"
    :deletable="false"
  />
</template>
