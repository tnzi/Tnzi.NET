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
import { onActivated, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { loginLogColumns, loginLogFormSchema, loginLogSearchFields } from './login-log-config'
import { makePageTranslator } from '../_shared/translate'
import type { LoginLogDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })
const route = useRoute()

const crud = useCrudPage<LoginLogDto, string>({
  pageId: 'identity.loginLogs',
  columns: loginLogColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.loginLogs.fetch(query),
  // read-only: no create/update/delete callbacks → affordances auto-hidden.
  // Manual first load: a `?userId=` deep link (from LoginSecurity's "View
  // login logs" row action) seeds the userId filter before the initial fetch.
  autoLoad: false,
})

const t = makePageTranslator('identity.loginLogs')

// Seed the userId filter from a `?userId=` deep link, then load. Runs on the
// initial mount (non-keepAlive consumers + tests) and on every keepAlive
// re-activation, so navigating in from LoginSecurity for a different user
// re-filters without the cached component remounting.
function loadFromQuery(): void {
  const userId = typeof route.query.userId === 'string' ? route.query.userId : undefined
  if (userId) crud.setFilters({ userId })
  void crud.refresh()
}

// Under keepAlive, onActivated fires immediately after onMounted on the first
// render; `initialized` skips that duplicate so the initial load runs once.
let initialized = false
onMounted(() => {
  initialized = true
  loadFromQuery()
})
onActivated(() => {
  if (initialized) {
    initialized = false
    return
  }
  loadFromQuery()
})
</script>
