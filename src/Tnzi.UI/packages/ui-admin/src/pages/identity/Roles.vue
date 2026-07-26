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
      Read-only role detail - opens from the row "Detail" action (the CRUD
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
              <!-- Same identity band as every other record surface: the role's
                   name, what kind of role it is, and the facts that identify
                   it, before any body text. -->
              <TRecordHeader
                compact
                :name="roleDetail?.name ?? EMPTY_DASH"
                icon="mdi:account-key-outline"
                :badges="roleBadges"
                :facts="roleFacts"
              />
              <p class="t-roles-page__role-desc">
                {{ roleDetail?.description || EMPTY_DASH }}
              </p>
            </div>
          </NTabPane>

          <NTabPane name="users" :tab="t('detail.tabs.users')">
            <p class="t-roles-page__hint">{{ t('detail.users.hint') }}</p>
            <!-- Members are PEOPLE: a face, a name, a way to reach them. A
                 three-column grid of the same three fields made the roster
                 read like a join table. -->
            <NSpin :show="users.loading">
              <TEmpty v-if="!users.loading && !users.items.length" :text="t('detail.users.empty')" size="small" />
              <div v-else class="t-roles-page__members">
                <TItemCard
                  v-for="u in users.items"
                  :key="u.id"
                  :title="u.userName"
                  :avatar="undefined"
                  icon="mdi:account"
                  :meta="memberMeta(u)"
                />
              </div>
            </NSpin>
            <div v-if="users.totalCount > users.pageSize" class="t-roles-page__pager">
              <NPagination
                :page="users.pageIndex"
                :page-size="users.pageSize"
                :item-count="users.totalCount"
                size="small"
                @update:page="(p: number) => { users.pageIndex = p; reloadUsers() }"
              />
            </div>
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
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NPagination, NSpin, NTabPane, NTabs } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRecordHeader, { type RecordBadge, type RecordFact } from '../../components/detail/TRecordHeader.vue'
import TItemCard, { type ItemCardMeta } from '../../components/data/TItemCard.vue'
import TEmpty from '../../components/data/TEmpty.vue'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core/utils'
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
  permission: 'role',
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

const roleBadges = computed<RecordBadge[]>(() => {
  const out: RecordBadge[] = []
  if (roleDetail.value?.isDefault) out.push({ label: t('detail.stats.defaultBadge'), type: 'info' })
  if (roleDetail.value?.isSystem) out.push({ label: t('detail.stats.systemBadge'), type: 'warning' })
  return out
})

const roleFacts = computed<RecordFact[]>(() => [
  {
    icon: 'mdi:account-group-outline',
    label: t('detail.stats.userCount'),
    value: String(roleDetail.value?.userCount ?? 0),
  },
])

/** Member row facts: how to reach them, and when they joined the role. */
function memberMeta(user: UserListItemDto): ItemCardMeta[] {
  const out: ItemCardMeta[] = []
  if (user.email) out.push({ icon: 'mdi:email-outline', text: user.email })
  out.push({ icon: 'mdi:calendar-plus', text: formatDateOnly(user.creationTime) })
  return out
}
</script>

<style scoped>
.t-roles-page__detail-info {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 8px 0;
}
.t-roles-page__role-desc {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--tnzi-base-text-muted);
  white-space: pre-wrap;
}
.t-roles-page__members {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-roles-page__pager {
  display: flex;
  justify-content: flex-end;
  padding-top: 10px;
}
.t-roles-page__hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  line-height: 1.5;
  margin: 0 0 12px;
}
</style>
