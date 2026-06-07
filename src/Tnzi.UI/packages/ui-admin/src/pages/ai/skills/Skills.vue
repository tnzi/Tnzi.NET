<template>
  <!--
    Skills — Phase 5 Task 5.7. Standard CRUD over the admin skill catalog.
    Follows the Agents canonical pattern (Task 5.2):
      - sibling `skill-config.ts` for columns + form schema
      - bridge.skills wired via useCrudPage
      - single #form slot delegates to TFormSchemaRenderer
      - i18n via translatePageKey('ai.skills', key)
  -->
  <TCrudPage
    :state="crud"
    :all-columns="skillColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="skillFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../../headless/rowActions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { skillColumns, skillFormSchema } from './skill-config'
import type {
  SkillSummaryDto,
  CreateSkillDto,
  UpdateSkillDto,
} from '@tnzi/core/services/ai'

const title = 'title'

const bridge = createAiBridge({ client: useAdminClient() })

const crud = useCrudPage<SkillSummaryDto>({
  pageId: 'ai.skills',
  columns: skillColumns,
  // File-source skills have no DB row → backend returns Guid.Empty for `id`;
  // fall back to slug so v-model:checked-row-keys + dataTable selection still
  // produces unique keys (slug is unique within scope).
  rowKey: (s) => {
    const id = String(s.id ?? '')
    return id && id !== '00000000-0000-0000-0000-000000000000' ? id : `slug:${s.slug}`
  },
  fetchData: (query) => bridge.skills.fetch(query),
  createData: (data) => bridge.skills.create(data as CreateSkillDto),
  updateData: (id, data) => bridge.skills.update(String(id), data as UpdateSkillDto),
  deleteData: (ids) => bridge.skills.delete(ids.map(String)),
})

const rowActions: RowAction<SkillSummaryDto>[] = [editAction(crud), deleteAction(crud)]


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.skills', key)
</script>
