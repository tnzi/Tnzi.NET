<template>
  <!--
    SkillList — Phase 5 Task 5.7. Standard CRUD over the admin skill catalog.
    Follows the AgentList canonical pattern (Task 5.2):
      - sibling `skill-list-config.ts` for columns + form schema
      - bridge.skills wired via useCrudPage
      - single #form slot delegates to TFormSchemaRenderer
      - i18n via translatePageKey('ai.skills', key)
  -->
  <TCrudPage
    :state="crud"
    :all-columns="skillColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="skillFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TRowActions from '../../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { skillColumns, skillFormSchema } from './skill-list-config'
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
  rowKey: (s) => s.id,
  fetchData: (query) => bridge.skills.fetch(query),
  createData: (data) => bridge.skills.create(data as CreateSkillDto),
  updateData: (id, data) => bridge.skills.update(String(id), data as UpdateSkillDto),
  deleteData: (ids) => bridge.skills.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.skills', key)
</script>
