<template>
  <TCrudPage
    :state="crud"
    :all-columns="roleColumns"
    :search-fields="roleSearchFields"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
    :detail-width="detailDrawerWidth"
    :detail-title="(d: RoleDto) => t('detail.title', { role: d.name })"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="roleFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>

    <!--
      Read-only role detail — opens from the row "Detail" action (the CRUD
      `view` open-state, deep-linkable for free). Three tabs:
        • Information (read-only basics + UserCount stat)
        • Members    (paged user list assigned to this role)
        • Permission Summary (link to role-permissions editor)
      `onView` lazy-loads RoleDetailDto; the Users tab loads on first activation.
    -->
    <template #detail>
      <NSpin :show="detailLoading">
        <NTabs v-model:value="tab" type="line" animated>
          <NTabPane name="info" :tab="t('detail.tabs.info')">
            <div class="t-roles-page__detail-info">
              <header class="t-roles-page__role-header">
                <h3 class="t-roles-page__role-name">{{ roleDetail?.name ?? '—' }}</h3>
                <NSpace size="small">
                  <NTag v-if="roleDetail?.isDefault" type="info" size="small" :bordered="false">
                    {{ t('detail.stats.defaultBadge') }}
                  </NTag>
                  <NTag v-if="roleDetail?.isSystem" type="warning" size="small" :bordered="false">
                    {{ t('detail.stats.systemBadge') }}
                  </NTag>
                </NSpace>
              </header>
              <p class="t-roles-page__role-desc">
                {{ roleDetail?.description || '—' }}
              </p>
              <div class="t-roles-page__stats">
                <NStatistic
                  :label="t('detail.stats.userCount')"
                  :value="roleDetail?.userCount ?? 0"
                />
              </div>
            </div>
          </NTabPane>

          <NTabPane name="users" :tab="t('detail.tabs.users')">
            <p class="t-roles-page__hint">{{ t('detail.users.hint') }}</p>
            <TResponsiveTable
              :columns="userColumns"
              :data="users.items"
              :loading="users.loading"
              :pagination="{
                page: users.pageIndex,
                pageSize: users.pageSize,
                itemCount: users.totalCount,
                onUpdatePage: (p: number) => { users.pageIndex = p; reloadUsers() },
                onUpdatePageSize: (s: number) => { users.pageSize = s; users.pageIndex = 1; reloadUsers() },
                showSizePicker: true,
                pageSizes: [10, 20, 50],
              }"
              :bordered="false"
              remote
              size="small"
              :row-key="(r: UserListItemDto) => r.id"
            />
          </NTabPane>

          <NTabPane name="permissions" :tab="t('detail.tabs.permissions')">
            <p class="t-roles-page__hint">{{ t('detail.permissions.hint') }}</p>
            <NButton type="primary" tertiary @click="goToPermissionEditor">
              <template #icon><TSvgIcon icon="mdi:key-variant" :size="14" /></template>
              {{ t('detail.permissions.gotoEditor') }}
            </NButton>
          </NTabPane>
        </NTabs>
      </NSpin>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NSpace,
  NSpin,
  NStatistic,
  NTabPane,
  NTabs,
  NTag,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import type { DataTableColumns } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { TSvgIcon, TRelativeTime } from '@tnzi/ui'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { useSafeMessage } from '../_shared/safeMessage'
import { roleColumns, roleFormSchema, roleSearchFields } from './role-config'
import { makePageTranslator } from '../_shared/translate'
import type { RoleDto, RoleDetailDto, UserListItemDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()
const router = useRouter()
const bp = useBreakpoint()

const t = makePageTranslator('identity.roles')

const crud = useCrudPage<RoleDto, string>({
  pageId: 'identity.roles',
  columns: roleColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.roles.fetch(query),
  createData: (data) => bridge.roles.create(data as never),
  updateData: (id, data) => bridge.roles.update(id, data as never),
  deleteData: (ids) => bridge.roles.delete(ids),
  onView: (row) => void loadRoleDetail(row),
})

// ─── Detail drawer ─────────────────────────────────────────────────────
// The viewed role IS the CRUD `view` open-state (the row "Detail" action →
// `crud.openView`, deep-linkable for free); only the heavier per-role payload
// stays page-local. `roleDetail` is the cached RoleDetailDto from
// `roles.getDetail()`; `users.items` is paged from `roles.getUsersInRole()`,
// lazy-loaded on first Users-tab activation. `onView` loads the detail on open
// AND on a deep-link cold reload.
const detailLoading = ref(false)
const tab = ref<'info' | 'users' | 'permissions'>('info')
const roleDetail = ref<RoleDetailDto | null>(null)
const roleId = computed(() => (crud.formModal.formData.value as RoleDto | null)?.id ?? '')
const users = reactive({
  loading: false,
  items: [] as UserListItemDto[],
  pageIndex: 1,
  pageSize: 10,
  totalCount: 0,
  /** Flag: only fire the first load on tab activation; subsequent
   *  page-size updates use the same loader. */
  loaded: false,
})

// Phone-friendly: full-screen on narrow viewports, 560px on desktop.
// The role detail is information-dense but not table-heavy, so we keep
// it narrow on wide screens to leave the master list visible behind it.
const detailDrawerWidth = computed<number | string>(() => (bp.isSm.value ? '100vw' : 560))

const rowActions: RowAction<RoleDto>[] = [
  editAction(crud),
  { key: 'detail', label: 'actions.detail', onClick: (row) => crud.openView(row) },
  deleteAction(crud),
]

async function loadRoleDetail(row: RoleDto): Promise<void> {
  tab.value = 'info'
  roleDetail.value = null
  users.items = []
  users.totalCount = 0
  users.pageIndex = 1
  users.loaded = false
  detailLoading.value = true
  try {
    roleDetail.value = await bridge.roles.getDetail(row.id)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    detailLoading.value = false
  }
}

async function reloadUsers(): Promise<void> {
  if (!roleId.value) return
  users.loading = true
  try {
    const result = await bridge.roles.getUsersInRole(roleId.value, {
      pageIndex: users.pageIndex,
      pageSize: users.pageSize,
    })
    users.items = result.items
    users.totalCount = result.totalCount
    users.loaded = true
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    users.items = []
    users.totalCount = 0
  } finally {
    users.loading = false
  }
}

// Lazy-load the users tab on first activation. Switching back doesn't
// re-fetch; manual refresh would mean reopening the drawer.
watch(tab, (value) => {
  if (value === 'users' && !users.loaded && roleId.value) {
    void reloadUsers()
  }
})

function goToPermissionEditor(): void {
  void router.push({
    name: 'authorization.roleFunctions',
    query: { roleId: roleId.value },
  })
}

const userColumns: DataTableColumns<UserListItemDto> = [
  { key: 'userName', title: t('detail.users.userName'), minWidth: 140 },
  { key: 'email', title: t('detail.users.email'), minWidth: 200, ellipsis: { tooltip: true } },
  {
    key: 'creationTime',
    title: t('detail.users.creationTime'),
    width: 160,
    // Match the main role/user tables: relative timestamp via TRelativeTime
    // instead of a hand-rolled toLocaleDateString.
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]
</script>

<style scoped>
.t-roles-page__detail-info {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 8px 0;
}
.t-roles-page__role-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}
.t-roles-page__role-name {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-roles-page__role-desc {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
  white-space: pre-wrap;
}
.t-roles-page__stats {
  margin-top: 8px;
  padding-top: 12px;
  border-top: 1px dashed var(--tnzi-border);
}
.t-roles-page__hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  line-height: 1.5;
  margin: 0 0 12px;
}
</style>
