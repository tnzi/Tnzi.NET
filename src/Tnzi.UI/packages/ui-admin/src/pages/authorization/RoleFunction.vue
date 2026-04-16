<template>
  <TCrudPage
    :state="crud"
    :all-columns="roleFunctionColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="roleFunctionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
// RoleFunction page — read-only view of role↔function assignments. The backend
// uses assignFunctions/removeFunctions/setFunctions endpoints (batch by roleId)
// rather than per-row CRUD; a tree-based assignment UX is deferred to a later
// phase (plan Task 3.9 deviation: canonical flat page instead of TPermissionTree
// slot override to keep Phase 3 unblocked).
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import type { RoleFunctionDto } from '@tnzi/core/services/authorization'
import TFormSchemaRenderer from '../_shared/form-schema'
import { roleFunctionColumns, roleFunctionFormSchema } from './role-function-config'
import { translatePageKey } from '../_shared/translate'

const title = 'Role Function Assignment'
// Wired to canonical GET /admin/role-functions (paged) via Plan C
// 2026-04-14. Client is injected by createTnziUiAdmin({ client }).
const bridge = createAuthorizationBridge({ client: useAdminClient() })

const readOnly = () => Promise.reject(new Error('RoleFunction: use bulk assign endpoints instead'))

const crud = useCrudPage<RoleFunctionDto>({
  pageId: 'authorization.roleFunctions',
  columns: roleFunctionColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.roleFunctions.fetch(query),
  createData: readOnly as never,
  updateData: readOnly as never,
  deleteData: readOnly as never,
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('authorization.roleFunctions', key)
</script>
