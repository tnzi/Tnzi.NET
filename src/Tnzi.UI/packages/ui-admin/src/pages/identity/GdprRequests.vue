<template>
  <TCrudPage
    :state="crud"
    :all-columns="gdprColumns"
    :title="title"
    :translate="t"
    :show-create="false"
  >
    <!-- Approve / Deny buttons live in toolbarRight so they are always visible,
         regardless of row selection state. They operate on selectedIds. -->
    <template #toolbarRight>
      <NButton class="gdpr-approve" type="success" size="small" @click="handleApprove(crud.batchActions.selectedIds.value as string[])">
        Approve Selected
      </NButton>
      <NButton class="gdpr-deny" type="error" size="small" @click="handleDeny(crud.batchActions.selectedIds.value as string[])">
        Deny Selected
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="gdprFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { NButton } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { gdprColumns, gdprFormSchema } from './gdpr-config'
import { translatePageKey } from '../_shared/translate'
import type { GdprRequestDto } from '../../services/bridges/identity-bridge'

const title = 'GDPR Requests'
const bridge = createIdentityBridge({ client: useAdminClient() })

const crud = useCrudPage<GdprRequestDto>({
  pageId: 'identity.gdpr',
  columns: gdprColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.gdpr.fetchRequests(query),
  // GDPR requests are approved/denied via batch action buttons, not standard create/update.
  // These no-ops satisfy useCrudPage's required callbacks without being reachable from the UI.
  createData: async () => { throw new Error('GDPR requests are not created from this page') },
  updateData: async () => { throw new Error('GDPR requests are not edited from this page') },
  deleteData: async () => { throw new Error('GDPR requests are not deleted from this page') },
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('identity.gdpr', key)

async function handleApprove(ids: string[]): Promise<void> {
  for (const id of ids) {
    await bridge.gdpr.approveRequest(id)
  }
  await crud.refresh()
}

async function handleDeny(ids: string[]): Promise<void> {
  for (const id of ids) {
    await bridge.gdpr.denyRequest(id)
  }
  await crud.refresh()
}

// Exposed for testing: allows tests to pre-populate selectedIds via crud.batchActions.toggle
defineExpose({ crud })
</script>
