<template>
  <!--
    WorkflowRunViewer — Phase 5 Task 5.6. Read-only viewer over workflow
    execution summaries.

    Read-only deviation from the canonical CRUD pattern:
      - bridge.workflowRuns has only fetch() + tail() (and tail() rejects);
        no create/update/delete are exposed by the bridge.
      - useCrudPage requires createData/updateData/deleteData in its options,
        so we pass stubs that reject. TCrudPage hides the create button via
        :show-create="false". Batch delete is also hidden by not wiring any
        rowKey-based selection actions in the slots.
      - Status streaming (tail) is intentionally NOT wired — the bridge
        rejects, and the plan's "row click → drawer" can land in a follow-up
        task once the backend exposes a streaming run-detail endpoint.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="workflowRunColumns"
    :title="title"
    :show-create="false"
    :translate="t"
  >
    <template #toolbarRight>
      <div class="run-viewer__filters">
        <TFormSchemaRenderer
          :schema="workflowRunFilterSchema"
          :model="filterModel"
          :readonly="false"
        />
        <button type="button" class="run-viewer__apply" @click="onApplyFilters">
          Apply
        </button>
      </div>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { reactive } from 'vue'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { workflowRunColumns, workflowRunFilterSchema } from './run-viewer-config'
import type { WorkflowExecutionSummaryDto } from '@tnzi/core/services/ai'

const title = 'Workflow Runs'

const bridge = createAiBridge({ client: useAdminClient() })

const readOnlyReject = (op: string) => () =>
  Promise.reject(new Error(`workflowRuns.${op} is not supported — read-only viewer`))

const crud = useCrudPage<WorkflowExecutionSummaryDto>({
  pageId: 'ai.workflowRuns',
  columns: workflowRunColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.workflowRuns.fetch(query),
  createData: readOnlyReject('create') as never,
  updateData: readOnlyReject('update') as never,
  deleteData: readOnlyReject('delete') as never,
})


const filterModel = reactive<Record<string, unknown>>({})

function onApplyFilters() {
  const next: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(filterModel)) {
    if (v !== undefined && v !== null && v !== '') next[k] = v
  }
  crud.setFilters(next)
}

crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.workflowRuns', key)

defineExpose({ filterModel, onApplyFilters })
</script>

<style scoped>
.run-viewer__filters {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.run-viewer__apply {
  padding: 0.35rem 0.8rem;
  cursor: pointer;
}
</style>
