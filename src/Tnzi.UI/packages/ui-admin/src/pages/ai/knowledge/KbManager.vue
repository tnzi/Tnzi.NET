<template>
  <!--
    KbManager — Phase 5 Task 5.10. Standard CRUD over knowledge bases plus
    a per-row Reindex action surfaced inside the edit form.

    Follows the AgentList canonical pattern (Task 5.2). Reindex deviation:
    `bridge.knowledge.reindex` is currently typed `Promise<void>` (the
    underlying kbApi returns a ReindexResultDto, but the bridge discards it).
    Per task scope we don't modify the bridge, so the success message reports
    completion without surfacing ChunkCount/DurationMs. A follow-up task can
    upgrade the bridge contract to return the ReindexResultDto.

    Reindex status is exposed via a small inline panel in the #form slot
    rather than a Naive UI message provider — ui-admin doesn't currently wrap
    the shell with NMessageProvider, so useMessage() is unavailable. Inline
    status keeps the page test-friendly and avoids an unmounted-provider warn.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="kbColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="kbFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
      <div v-if="mode === 'edit' && (formData as Record<string, unknown>)?.id" class="kb-reindex">
        <button
          type="button"
          class="kb-reindex__btn"
          :disabled="reindexBusy"
          @click="onReindex((formData as Record<string, unknown>).id as string)"
        >
          {{ reindexBusy ? 'Reindexing…' : 'Reindex' }}
        </button>
        <span v-if="reindexStatus" class="kb-reindex__status" :data-status="reindexStatus.kind">
          {{ reindexStatus.message }}
        </span>
      </div>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { kbColumns, kbFormSchema } from './kb-manager-config'

type KbRow = Record<string, unknown>

const title = 'Knowledge Bases'

const bridge = createAiBridge({ client: useAdminClient() })

const crud = useCrudPage<KbRow>({
  pageId: 'ai.knowledge',
  columns: kbColumns,
  rowKey: (kb) => String((kb as { id?: string }).id ?? ''),
  fetchData: (query) => bridge.knowledge.fetch(query),
  createData: (data) => bridge.knowledge.create(data as KbRow),
  updateData: (id, data) => bridge.knowledge.update(String(id), data as KbRow),
  deleteData: (ids) => bridge.knowledge.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const reindexBusy = ref(false)
const reindexStatus = ref<{ kind: 'success' | 'error'; message: string } | null>(null)

async function onReindex(id: string) {
  if (!id || reindexBusy.value) return
  reindexBusy.value = true
  reindexStatus.value = null
  try {
    await bridge.knowledge.reindex(id)
    reindexStatus.value = { kind: 'success', message: 'Reindex completed' }
  } catch (err) {
    reindexStatus.value = {
      kind: 'error',
      message: err instanceof Error ? err.message : 'Reindex failed',
    }
  } finally {
    reindexBusy.value = false
  }
}

const t = (key: string) => translatePageKey('ai.knowledge', key)

defineExpose({ onReindex, reindexBusy, reindexStatus })
</script>

<style scoped>
.kb-reindex {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
}
.kb-reindex__btn {
  padding: 0.4rem 0.9rem;
  cursor: pointer;
}
.kb-reindex__btn:disabled {
  cursor: wait;
  opacity: 0.6;
}
.kb-reindex__status[data-status='error'] {
  color: var(--n-error-color, #d03050);
}
</style>
