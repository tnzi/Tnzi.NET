<template>
  <!--
    QuotaRules — Phase 5 Task 5.12. CRUD over per-user AI token quotas.

    Stage 1 backend prereq unblocked the paged list at
    POST /admin/quotas/query. The bridge maps create/update both to
    quotaApi.setQuota (idempotent upsert keyed on userId). Delete is not
    supported by the bridge — surfaced as the inline note in the page header.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="quotaColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="quotaFormSchema"
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
import { quotaColumns, quotaFormSchema } from './quota-rules-config'
import type { UserQuotaDto, SetQuotaDto } from '@tnzi/core/services/ai'

const title = 'Quota Rules'

const bridge = createAiBridge({ client: useAdminClient() })

const crud = useCrudPage<UserQuotaDto>({
  pageId: 'ai.quota',
  columns: quotaColumns,
  rowKey: (q) => q.id,
  fetchData: (query) => bridge.quota.fetch(query),
  createData: (data) => bridge.quota.create(data as SetQuotaDto),
  updateData: (_id, data) => bridge.quota.update(String(_id), data as SetQuotaDto),
  deleteData: (ids) => bridge.quota.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('ai.quota', key)
</script>
