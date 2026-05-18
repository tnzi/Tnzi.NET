<template>
  <TCrudPage
    :state="crud"
    :all-columns="userColumns"
    :search-fields="userSearchFields"
    :title="title"
    :translate="t"
    :row-key="rowKey"
    :row-actions-width="260"
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
    <template #rowActions="{ row }">
      <TRowActions :row="(row as UserListItem)" :state="crud" :translate="t">
        <template #middle="{ row: r }">
          <NPopconfirm
            v-if="(r as UserListItem).isEnabled === false"
            @positive-click="() => handleEnable((r as UserListItem).id)"
          >
            <template #trigger>
              <NButton size="small" type="success" ghost>
                {{ t('actions.enable') }}
              </NButton>
            </template>
            {{ t('actions.confirmEnable') }}
          </NPopconfirm>
          <NPopconfirm
            v-else
            @positive-click="() => handleDisable((r as UserListItem).id)"
          >
            <template #trigger>
              <NButton size="small" type="warning" ghost>
                {{ t('actions.disable') }}
              </NButton>
            </template>
            {{ t('actions.confirmDisable') }}
          </NPopconfirm>

          <!-- Secondary ops collapsed into a "More" dropdown so the row
               actions cell stays compact (5 inline buttons overflowed). -->
          <NDropdown
            trigger="click"
            :options="moreOptionsFor(r as UserListItem)"
            @select="(key: string) => onMoreSelect(key, (r as UserListItem))"
          >
            <NButton size="small" ghost>
              {{ t('actions.more') }} ▾
            </NButton>
          </NDropdown>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>

  <NModal v-model:show="resetPwdModal.show" :title="t('actions.resetPassword')" preset="card" style="width: 480px">
    <NForm>
      <NFormItem :label="t('actions.newPassword')" required>
        <NInput
          v-model:value="resetPwdModal.password"
          type="password"
          show-password-on="click"
          :placeholder="t('actions.resetPasswordHint')"
        />
      </NFormItem>
    </NForm>
    <template #footer>
      <div style="display: flex; justify-content: flex-end; gap: 8px">
        <NButton @click="resetPwdModal.show = false">{{ t('admin.crud.cancel') }}</NButton>
        <NButton type="primary" :disabled="!resetPwdModal.password" @click="submitResetPassword">
          {{ t('admin.crud.confirm') }}
        </NButton>
      </div>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import { userColumns, userSearchFields } from './user-config'
import { computed, reactive } from 'vue'
import { NForm, NFormItem, NInput, NButton, NPopconfirm, NDropdown, NModal, useMessage } from 'naive-ui'

interface UserListItem {
  id: string
  userName: string
  email: string
  createdAt?: string
  isEnabled?: boolean
  isLockedOut?: boolean
}

const title = 'title'

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

crud.refresh().catch(() => undefined)

function setField(key: keyof UserListItem, value: unknown) {
  if (!crud.formModal.formData.value) {
    crud.formModal.formData.value = {} as UserListItem
  }
  ;(crud.formModal.formData.value as unknown as Record<string, unknown>)[key as string] = value
}

const t = (key: string) => translatePageKey('identity.users', key)

// `useMessage()` throws synchronously when no <NMessageProvider> ancestor exists
// (e.g. isolated component-mount integration tests). The admin shell always
// provides one, so this fallback only kicks in for test contexts.
let message: { success(msg: string): void; error(msg: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {} }
}

async function withRefresh(action: () => Promise<void>, successKey: string): Promise<void> {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const handleEnable = (id: string) => withRefresh(() => bridge.users.enable(id), 'actions.enableSuccess')
const handleDisable = (id: string) => withRefresh(() => bridge.users.disable(id), 'actions.disableSuccess')
const handleLock = (id: string) => withRefresh(() => bridge.users.lock(id), 'actions.lockSuccess')
const handleUnlock = (id: string) => withRefresh(() => bridge.users.unlock(id), 'actions.unlockSuccess')

/**
 * Build the dropdown option list per-row — lock/unlock toggles based on the
 * row's current state, and the password-reset entry opens a modal.
 */
function moreOptionsFor(row: UserListItem) {
  return [
    row.isLockedOut
      ? { key: 'unlock', label: t('actions.unlock') }
      : { key: 'lock', label: t('actions.lock') },
    { key: 'resetPassword', label: t('actions.resetPassword') },
  ]
}

function onMoreSelect(key: string, row: UserListItem): void {
  if (key === 'lock') void handleLock(row.id)
  else if (key === 'unlock') void handleUnlock(row.id)
  else if (key === 'resetPassword') openResetPassword(row.id)
}

const resetPwdModal = reactive({ show: false, userId: '', password: '' })
function openResetPassword(id: string): void {
  resetPwdModal.userId = id
  resetPwdModal.password = ''
  resetPwdModal.show = true
}
async function submitResetPassword(): Promise<void> {
  const { userId, password } = resetPwdModal
  if (!userId || !password) return
  await withRefresh(() => bridge.users.resetPassword(userId, password), 'actions.resetPasswordSuccess')
  resetPwdModal.show = false
}
</script>
