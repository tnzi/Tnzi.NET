<template>
  <TCrudPage
    :state="crud"
    :all-columns="threadColumns"
    :title="title"
    :translate="t"
  >
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" :show-edit="false" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TRowActions from '../../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { translatePageKey } from '../../_shared/translate'
import { threadColumns } from './thread-config'

import type { AgentThreadDto } from '@tnzi/core/services/ai'

const bridge = createAiBridge({ client: useAdminClient() })

const readOnly = () => Promise.reject(new Error('Read-only page'))

const crud = useCrudPage<AgentThreadDto>({
  pageId: 'ai.threads',
  columns: threadColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.threads.fetch(q),
  createData: readOnly as never,
  updateData: readOnly as never,
  deleteData: (ids) => bridge.threads.delete(ids.map(String)),
})
crud.refresh().catch(() => undefined)

const title = 'title'
const t = (key: string) => translatePageKey('ai.threads', key)
</script>
