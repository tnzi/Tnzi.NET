<template>
  <!--
    PersonaList — Phase 5 Task 5.13. Standard CRUD over agent personas
    (soul templates). Follows the AgentList canonical pattern (Task 5.2).
  -->
  <TCrudPage
    :state="crud"
    :all-columns="personaColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="personaFormSchema"
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
import { personaColumns, personaFormSchema } from './persona-list-config'
import type {
  AgentPersonaDto,
  CreateAgentPersonaDto,
  UpdateAgentPersonaDto,
} from '@tnzi/core/services/ai'

const title = 'Personas'

const bridge = createAiBridge({ client: useAdminClient() })

const crud = useCrudPage<AgentPersonaDto>({
  pageId: 'ai.personas',
  columns: personaColumns,
  rowKey: (p) => p.id,
  fetchData: (query) => bridge.personas.fetch(query),
  createData: (data) => bridge.personas.create(data as CreateAgentPersonaDto),
  updateData: (id, data) => bridge.personas.update(String(id), data as UpdateAgentPersonaDto),
  deleteData: (ids) => bridge.personas.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.personas', key)
</script>
