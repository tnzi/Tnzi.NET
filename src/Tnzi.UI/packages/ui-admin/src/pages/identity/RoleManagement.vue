<template>
  <TCrudPage
    :state="crud"
    :all-columns="roleColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="roleFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { roleColumns, roleFormSchema } from './role-config'
import { translatePageKey } from '../_shared/translate'
import type { RoleDto } from '@tnzi/core/services/identity'

const title = 'Role Management'
const bridge = createIdentityBridge({ client: useAdminClient() })

const crud = useCrudPage<RoleDto, string>({
  pageId: 'identity.roles',
  columns: roleColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.roles.fetch(query),
  createData: (data) => bridge.roles.create(data as never),
  updateData: (id, data) => bridge.roles.update(id, data as never),
  deleteData: (ids) => bridge.roles.delete(ids),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('identity.roles', key)
</script>
