<template>
  <!-- Read-only page: showCreate=false hides the Create button; no-op handlers ensure
       useCrudPage's required callbacks are satisfied without being reachable from the UI. -->
  <TCrudPage
    :state="crud"
    :all-columns="loginLogColumns"
    :title="title"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="loginLogFormSchema"
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
import { loginLogColumns, loginLogFormSchema } from './login-log-config'
import { translatePageKey } from '../_shared/translate'
import type { LoginLogDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })

// No-op handlers: login logs are read-only; these callbacks are required by useCrudPage
// but the UI never triggers them because showCreate=false and row actions are view-only.
const readOnlyFn = async (): Promise<never> => { throw new Error('Login Log is read-only') }

const crud = useCrudPage<LoginLogDto, string>({
  pageId: 'identity.loginLogs',
  columns: loginLogColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.loginLogs.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: readOnlyFn,
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('identity.loginLogs', key)
</script>
