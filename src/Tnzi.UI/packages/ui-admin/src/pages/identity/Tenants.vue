<template>
  <TCrudPage
    :state="crud"
    :all-columns="tenantColumns"
    :search-fields="tenantSearchFields"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="tenantFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { tenantColumns, tenantFormSchema, tenantSearchFields } from './tenant-config'
import { translatePageKey } from '../_shared/translate'
import type { TenantDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })

const crud = useCrudPage<TenantDto, string>({
  pageId: 'identity.tenants',
  columns: tenantColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.tenants.fetch(query),
  createData: (data) => bridge.tenants.create(data as never),
  updateData: (id, data) => bridge.tenants.update(id, data as never),
  deleteData: (ids) => bridge.tenants.delete(ids),
})

const rowActions: RowAction<TenantDto>[] = [editAction(crud), deleteAction(crud)]


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('identity.tenants', key)
</script>
