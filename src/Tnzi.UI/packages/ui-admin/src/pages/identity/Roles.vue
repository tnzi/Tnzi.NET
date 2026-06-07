<template>
  <TCrudPage
    :state="crud"
    :all-columns="roleColumns"
    :search-fields="roleSearchFields"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="roleFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>

  <!--
    Role detail drawer — opens from the row More menu. Shows three tabs:
      • Information (read-only basics + UserCount stat)
      • Members    (paged user list assigned to this role)
      • Permission Summary (count + link to role-permissions editor)
    Lazy-loads on first open per role; reuses cached state on tab swap.
  -->
  <NDrawer v-model:show="detailDrawer.show" :width="detailDrawerWidth" placement="right">
    <NDrawerContent :title="detailDrawer.title" closable>
      <NSpin :show="detailDrawer.loading">
        <NTabs v-model:value="detailDrawer.tab" type="line" animated>
          <NTabPane name="info" :tab="t('detail.tabs.info')">
            <div class="t-roles-page__detail-info">
              <header class="t-roles-page__role-header">
                <h3 class="t-roles-page__role-name">{{ detailDrawer.detail?.name ?? '—' }}</h3>
                <NSpace size="small">
                  <NTag v-if="detailDrawer.detail?.isDefault" type="info" size="small" :bordered="false">
                    {{ t('detail.stats.defaultBadge') }}
                  </NTag>
                  <NTag v-if="detailDrawer.detail?.isSystem" type="warning" size="small" :bordered="false">
                    {{ t('detail.stats.systemBadge') }}
                  </NTag>
                </NSpace>
              </header>
              <p class="t-roles-page__role-desc">
                {{ detailDrawer.detail?.description || '—' }}
              </p>
              <div class="t-roles-page__stats">
                <NStatistic
                  :label="t('detail.stats.userCount')"
                  :value="detailDrawer.detail?.userCount ?? 0"
                />
              </div>
            </div>
          </NTabPane>

          <NTabPane name="users" :tab="t('detail.tabs.users')">
            <p class="t-roles-page__hint">{{ t('detail.users.hint') }}</p>
            <TResponsiveTable
              :columns="userColumns"
              :data="detailDrawer.users.items"
              :loading="detailDrawer.users.loading"
              :pagination="{
                page: detailDrawer.users.pageIndex,
                pageSize: detailDrawer.users.pageSize,
                itemCount: detailDrawer.users.totalCount,
                onUpdatePage: (p: number) => { detailDrawer.users.pageIndex = p; reloadUsers() },
                onUpdatePageSize: (s: number) => { detailDrawer.users.pageSize = s; detailDrawer.users.pageIndex = 1; reloadUsers() },
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
    </NDrawerContent>
  </NDrawer>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NDrawer,
  NDrawerContent,
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
import { TSvgIcon } from '@tnzi/ui'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { useSafeMessage } from '../_shared/safeMessage'
import { roleColumns, roleFormSchema, roleSearchFields } from './role-config'
import { interpolate, translatePageKey } from '../_shared/translate'
import type { RoleDto, RoleDetailDto, UserListItemDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()
const router = useRouter()
const bp = useBreakpoint()

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

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('identity.roles', key), params)

// ─── Detail drawer ─────────────────────────────────────────────────────
// Backing state for the master-detail view. `detail` is the cached
// RoleDetailDto from `roles.getDetail()`; `users.items` is paged from
// `roles.getUsersInRole()`. Switching to the Users tab triggers the
// first load; further loads reuse the loader (page/pageSize updates).
const detailDrawer = reactive({
  show: false,
  loading: false,
  tab: 'info' as 'info' | 'users' | 'permissions',
  roleId: '',
  title: '',
  detail: null as RoleDetailDto | null,
  users: {
    loading: false,
    items: [] as UserListItemDto[],
    pageIndex: 1,
    pageSize: 10,
    totalCount: 0,
    /** Flag: only fire the first load on tab activation; subsequent
     *  page-size updates use the same loader. */
    loaded: false,
  },
})

// Phone-friendly: full-screen on narrow viewports, 560px on desktop.
// The role detail is information-dense but not table-heavy, so we keep
// it narrow on wide screens to leave the master list visible behind it.
const detailDrawerWidth = computed(() => (bp.isSm.value ? '100vw' : 560))

const rowActions: RowAction<RoleDto>[] = [
  editAction(crud),
  { key: 'detail', label: 'actions.detail', onClick: (row) => openDetail(row) },
  deleteAction(crud),
]

async function openDetail(row: RoleDto): Promise<void> {
  detailDrawer.show = true
  detailDrawer.tab = 'info'
  detailDrawer.roleId = row.id
  detailDrawer.title = t('detail.title', { role: row.name })
  detailDrawer.detail = null
  detailDrawer.users.items = []
  detailDrawer.users.totalCount = 0
  detailDrawer.users.pageIndex = 1
  detailDrawer.users.loaded = false
  detailDrawer.loading = true
  try {
    detailDrawer.detail = await bridge.roles.getDetail(row.id)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    detailDrawer.loading = false
  }
}

async function reloadUsers(): Promise<void> {
  if (!detailDrawer.roleId) return
  detailDrawer.users.loading = true
  try {
    const result = await bridge.roles.getUsersInRole(detailDrawer.roleId, {
      pageIndex: detailDrawer.users.pageIndex,
      pageSize: detailDrawer.users.pageSize,
    })
    detailDrawer.users.items = result.items
    detailDrawer.users.totalCount = result.totalCount
    detailDrawer.users.loaded = true
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    detailDrawer.users.items = []
    detailDrawer.users.totalCount = 0
  } finally {
    detailDrawer.users.loading = false
  }
}

// Lazy-load the users tab on first activation. Switching back doesn't
// re-fetch; manual refresh would mean reopening the drawer.
watch(
  () => detailDrawer.tab,
  (tab) => {
    if (tab === 'users' && !detailDrawer.users.loaded && detailDrawer.roleId) {
      void reloadUsers()
    }
  },
)

function goToPermissionEditor(): void {
  void router.push({
    name: 'authorization.roleFunctions',
    query: { roleId: detailDrawer.roleId },
  })
}

const userColumns: DataTableColumns<UserListItemDto> = [
  { key: 'userName', title: t('detail.users.userName'), minWidth: 140 },
  { key: 'email', title: t('detail.users.email'), minWidth: 200, ellipsis: { tooltip: true } },
  {
    key: 'creationTime',
    title: t('detail.users.creationTime'),
    width: 160,
    render: (row) => {
      if (!row.creationTime) return '—'
      try { return new Date(row.creationTime).toLocaleDateString() } catch { return String(row.creationTime) }
    },
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
