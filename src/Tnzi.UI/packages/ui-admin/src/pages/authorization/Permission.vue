<template>
  <TCrudPage
    :state="crud"
    :all-columns="permissionColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="permissionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
// Permission page — read-only view of ModuleFunction entries. Backend has no
// admin create/update/delete endpoints for permissions (they are defined by
// application code). The page uses TCrudPage for consistent UX; create/update/
// delete bridge callbacks are no-ops returning rejected promises.
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import type { ModuleFunctionDto as PermissionDto } from '@tnzi/core/services/authorization'
import TFormSchemaRenderer from '../_shared/form-schema'
import { permissionColumns, permissionFormSchema } from './permission-config'
import { translatePageKey } from '../_shared/translate'

const title = 'Permission Management'
// Wired to GET /admin/modules/{id}/functions (read-only) via Plan C
// 2026-04-14. Client is injected by createTnziUiAdmin({ client }).
const bridge = createAuthorizationBridge({ client: useAdminClient() })

const readOnly = () => Promise.reject(new Error('Permission is read-only: backend has no admin mutation endpoints'))

const crud = useCrudPage<PermissionDto>({
  pageId: 'authorization.permissions',
  columns: permissionColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.permissions.fetch(query),
  createData: readOnly as never,
  updateData: readOnly as never,
  deleteData: readOnly as never,
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('authorization.permissions', key)
</script>
