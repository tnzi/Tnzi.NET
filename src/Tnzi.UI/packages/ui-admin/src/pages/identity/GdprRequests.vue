<template>
  <TCrudPage
    :state="crud"
    :all-columns="gdprColumns"
    :title="title"
    :translate="t"
    :show-create="false"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
  >
    <!-- Batch approve / deny only surface once at least one row is
         selected. TBatchActions inside TCrudPage already gates render on
         `state.hasSelection.value`, so this slot stays hidden in the
         default empty state and the always-on toolbar stops competing
         with the data table for attention. -->
    <template #batchActions="{ selectedIds }">
      <NPopconfirm @positive-click="handleApprove(selectedIds as string[])">
        <template #trigger>
          <NButton class="gdpr-approve" type="success" size="small" ghost>
            {{ t('actions.batchApprove') }}
          </NButton>
        </template>
        {{ t('actions.confirmBatchApprove') }}
      </NPopconfirm>
      <NPopconfirm @positive-click="handleDeny(selectedIds as string[])">
        <template #trigger>
          <NButton class="gdpr-deny" type="error" size="small" ghost>
            {{ t('actions.batchReject') }}
          </NButton>
        </template>
        {{ t('actions.confirmBatchReject') }}
      </NPopconfirm>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="gdprFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>

    <!-- Per-row actions: a request that's already approved/denied is
         immutable, so the action buttons only appear while the row is
         still pending. The edit + delete buttons are suppressed because
         the form is read-only and rejected requests must stay auditable. -->
    <template #rowActions="{ row }">
      <TRowActions :row="(row as GdprRow)" :actions="rowActions" :translate="t">
        <template #prepend="{ row: r }">
          <template v-if="(r as GdprRow).status === 'pending'">
            <NPopconfirm @positive-click="handleApprove([(r as GdprRow).id ?? ''])">
              <template #trigger>
                <NButton type="success" size="small" ghost>
                  {{ t('actions.approve') }}
                </NButton>
              </template>
              {{ t('actions.confirmApprove') }}
            </NPopconfirm>
            <NPopconfirm @positive-click="handleDeny([(r as GdprRow).id ?? ''])">
              <template #trigger>
                <NButton type="error" size="small" ghost>
                  {{ t('actions.reject') }}
                </NButton>
              </template>
              {{ t('actions.confirmReject') }}
            </NPopconfirm>
          </template>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { NButton, NPopconfirm, useMessage } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { type RowAction } from '../../headless/rowActions'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { gdprColumns, gdprFormSchema, type GdprRow } from './gdpr-config'
import { translatePageKey } from '../_shared/translate'
import type { GdprRequestDto } from '../../services/bridges/identity-bridge'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useMessage()

const crud = useCrudPage<GdprRequestDto>({
  pageId: 'identity.gdpr',
  columns: gdprColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.gdpr.fetchRequests(query),
  // GDPR requests are approved/denied via row + batch actions, not
  // standard create/update/delete. These no-ops satisfy useCrudPage's
  // required callbacks without being reachable from the UI.
  createData: async () => { throw new Error('GDPR requests are not created from this page') },
  updateData: async () => { throw new Error('GDPR requests are not edited from this page') },
  deleteData: async () => { throw new Error('GDPR requests are not deleted from this page') },
})


crud.refresh().catch(() => undefined)

// Approve/Reject are rendered by the page in the #prepend slot (pending rows
// only); edit/delete are intentionally suppressed (requests stay auditable),
// so the inner <TRowActions> has no built-in inline actions of its own.
const rowActions: RowAction<GdprRow>[] = []

const t = (key: string) => translatePageKey('identity.gdpr', key)

async function handleApprove(ids: string[]): Promise<void> {
  const filtered = ids.filter(Boolean)
  if (filtered.length === 0) return
  try {
    for (const id of filtered) {
      await bridge.gdpr.approveRequest(id)
    }
    message.success(t('actions.approveSuccess'))
    crud.batchActions.clear()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('actions.approveError'))
  }
}

async function handleDeny(ids: string[]): Promise<void> {
  const filtered = ids.filter(Boolean)
  if (filtered.length === 0) return
  try {
    for (const id of filtered) {
      await bridge.gdpr.denyRequest(id)
    }
    message.success(t('actions.rejectSuccess'))
    crud.batchActions.clear()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('actions.rejectError'))
  }
}

// Exposed for testing: allows tests to pre-populate selectedIds via crud.batchActions.toggle
defineExpose({ crud })
</script>

<style scoped>
.gdpr-approve,
.gdpr-deny {
  /* Keep ghost buttons consistent with the rest of the admin row actions
     so the batch bar visually matches the per-row buttons. */
  font-weight: 500;
}
</style>
