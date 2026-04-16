<template>
  <TCrudPage
    :state="crud"
    :all-columns="userColumns"
    :title="title"
    :translate="t"
    :row-key="rowKey"
  >
    <template #form="{ mode }">
      <NForm :disabled="mode === 'view'">
        <NFormItem label="Username" path="userName">
          <NInput
            :value="userNameValue"
            @update:value="(v: string) => setField('userName', v)"
          />
        </NFormItem>
        <NFormItem label="Email" path="email">
          <NInput
            :value="emailValue"
            @update:value="(v: string) => setField('email', v)"
          />
        </NFormItem>
      </NForm>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { en } from '../../locales/en'
import { userColumns } from './user-config'
import { computed } from 'vue'
import { NForm, NFormItem, NInput } from 'naive-ui'

interface UserListItem {
  id: string
  userName: string
  email: string
  createdAt?: string
}

const title = 'User Management'

// TODO(phase-3): inject client via createTnziUiAdmin options
const bridge = createIdentityBridge({ client: useAdminClient() })

const crud = useCrudPage<UserListItem>({
  pageId: 'identity.users',
  columns: userColumns,
  rowKey: (u) => u.id,
  fetchData: (query) =>
    bridge.users.fetch(query) as Promise<{
      items: UserListItem[]
      totalCount: number
      pageIndex: number
      pageSize: number
    }>,
  createData: (data) => bridge.users.create(data as never) as Promise<UserListItem>,
  updateData: (id, data) =>
    bridge.users.update(String(id), data as never) as Promise<UserListItem>,
  deleteData: (ids) => bridge.users.delete(ids.map(String)),
  exportData: (query) => bridge.users.export!(query),
  importData: (file) => bridge.users.import!(file),
})


const rowKey = (row: unknown) => (row as UserListItem).id

const userNameValue = computed(() => crud.formModal.formData.value?.userName ?? '')
const emailValue = computed(() => crud.formModal.formData.value?.email ?? '')

// Initial load
crud.refresh().catch(() => undefined)

function setField(key: keyof UserListItem, value: unknown) {
  if (!crud.formModal.formData.value) {
    crud.formModal.formData.value = {} as UserListItem
  }
  ;(crud.formModal.formData.value as unknown as Record<string, unknown>)[key as string] = value
}

function t(key: string): string {
  const segments = key.split('.')
  let node: unknown = en
  for (const seg of segments) {
    if (typeof node === 'object' && node !== null && seg in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[seg]
    } else {
      return key
    }
  }
  return typeof node === 'string' ? node : key
}
</script>
