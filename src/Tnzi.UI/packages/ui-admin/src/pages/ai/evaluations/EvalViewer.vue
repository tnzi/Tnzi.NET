<template>
  <!--
    EvalViewer — Phase 5 Task 5.14. Read + create-and-run evaluation runs.

    IMPORTANT BEHAVIOR: the backend's create endpoint at
    POST /admin/ai/evaluations/run is *create-and-run-in-one-shot*. There is
    no "create draft, run later" flow. The visible "Create" button in
    TCrudPage actually spawns a fresh run that immediately starts executing.

    Update is not supported (immutable runs once started). Delete IS exposed
    via the bridge so admins can prune historical rows.

    The MVP form takes a single test case (input + optional expected output).
    A follow-up task can ship a richer multi-case editor.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="evalRunColumns"
    :title="title"
    :translate="t"
  >
    <template #header>
      <div class="eval-viewer__header">
        <h2 class="t-crud-page__title">{{ title }}</h2>
        <p class="eval-viewer__note">
          Note: clicking <strong>Create</strong> immediately starts a new
          evaluation run on the backend (create-and-run is a single endpoint
          call). Runs are immutable once started.
        </p>
      </div>
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="evalRunFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { evalRunColumns, evalRunFormSchema } from './eval-viewer-config'
import type {
  EvaluationRunDto,
  CreateEvaluationRunDto,
} from '@tnzi/core/services/ai'

const title = 'Evaluation Runs'

const bridge = createAiBridge({ client: useAdminClient() })

/**
 * Form-shape → DTO adapter. The form is flat for ergonomics, but
 * CreateEvaluationRunDto wants `cases: EvaluationCaseDto[]`. We transform
 * the single inline {caseInput, caseExpected} pair into the cases array.
 */
function toCreateDto(form: Record<string, unknown>): CreateEvaluationRunDto {
  const input = String(form.caseInput ?? '').trim()
  return {
    agentId: String(form.agentId ?? ''),
    versionNumber: (form.versionNumber as number | undefined) ?? null,
    cases: input
      ? [
          {
            input,
            expectedOutput: (form.caseExpected as string | undefined) || null,
          },
        ]
      : [],
  }
}

const updateNotSupported = (): Promise<EvaluationRunDto> =>
  Promise.reject(new Error('evaluations.update is not supported — runs are immutable'))

const crud = useCrudPage<EvaluationRunDto>({
  pageId: 'ai.evaluations',
  columns: evalRunColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.evaluations.fetch(query),
  createData: (data) => bridge.evaluations.create(toCreateDto(data as Record<string, unknown>)),
  updateData: updateNotSupported as never,
  deleteData: (ids) => bridge.evaluations.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.evaluations', key)

defineExpose({ toCreateDto })
</script>

<style scoped>
.eval-viewer__header {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.eval-viewer__note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--n-text-color-3, #888);
}
.t-crud-page__title {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
</style>
