<template>
  <!-- Read-only page: createData/updateData/deleteData are omitted, so
       canCreate/canUpdate/canDelete are false and the shell hides all
       mutating affordances automatically. -->
  <TCrudPage
    :state="crud"
    :all-columns="loginLogColumns"
    :search-fields="loginLogSearchFields"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="loginLogFormSchema"
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
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { loginLogColumns, loginLogFormSchema, loginLogSearchFields } from './login-log-config'
import { translatePageKey } from '../_shared/translate'
import type { LoginLogDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })

const crud = useCrudPage<LoginLogDto, string>({
  pageId: 'identity.loginLogs',
  columns: loginLogColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.loginLogs.fetch(query),
  // read-only: no create/update/delete callbacks → affordances auto-hidden
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('identity.loginLogs', key)
</script>
