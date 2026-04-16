<template>
  <!--
    AgentList — Phase 5 canonical AI admin page (Task 5.2).
    Pattern that all subsequent Phase 5 page tasks (5.3–5.14) copy:
      - sibling `{page}-config.ts` exports columns + form schema (pure data)
      - bridge.<resource> wired to TCrudPage via useCrudPage
      - single #form slot delegates to TFormSchemaRenderer
      - i18n via translatePageKey('ai.agents', key)
    See src/pages/_shared/page-conventions.md for the full Phase 3 convention
    that Phase 5 inherits.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="agentColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="agentFormSchema"
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
import { agentColumns, agentFormSchema } from './agent-list-config'
import type {
  AgentDto,
  CreateAgentDto,
  UpdateAgentDto,
} from '@tnzi/core/services/ai'

const title = 'Agents'

// TODO(phase-5.15+): inject HttpClient via createTnziUiAdmin options instead of
// constructing the bridge inline. Mirrors the Phase 3 identity-bridge pattern;
// will move when the AI router is wired up in Task 5.15.
const bridge = createAiBridge({ client: useAdminClient() })

const crud = useCrudPage<AgentDto>({
  pageId: 'ai.agents',
  columns: agentColumns,
  rowKey: (a) => a.id,
  fetchData: (query) => bridge.agents.fetch(query),
  createData: (data) => bridge.agents.create(data as CreateAgentDto),
  updateData: (id, data) => bridge.agents.update(String(id), data as UpdateAgentDto),
  deleteData: (ids) => bridge.agents.delete(ids.map(String)),
})

// useCrudPage's generic return type doesn't precisely match TCrudPage's
// `state: ReturnType<typeof useCrudPage>` prop (the latter erases T). Cast
// through unknown — same workaround used by every Phase 3 page.

// Initial load — errors are surfaced via crud.error and the UI; swallow here
// to avoid an unhandled rejection in setup.
crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.agents', key)
</script>
