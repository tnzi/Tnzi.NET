<template>
  <TCrudPage
    :state="crud"
    :all-columns="entityRoleColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="entityRoleFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { entityRoleColumns, entityRoleFormSchema } from './entity-role-config'
import { translatePageKey } from '../_shared/translate'

interface EntityRoleDto { id: string; entityInfoId: string; roleId: string; operation: string; filter?: string; isEnabled: boolean }

const title = 'Entity Role Management'
const bridge = createAuthorizationBridge({ client: useAdminClient() })

const crud = useCrudPage<EntityRoleDto>({
  pageId: 'authorization.entityRoles',
  columns: entityRoleColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.entityRoles.fetch(query),
  createData: (data) => bridge.entityRoles.create(data as never) as Promise<EntityRoleDto>,
  updateData: (id, data) => bridge.entityRoles.update(String(id), data as never) as Promise<EntityRoleDto>,
  deleteData: (ids) => bridge.entityRoles.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('authorization.entityRoles', key)
</script>
