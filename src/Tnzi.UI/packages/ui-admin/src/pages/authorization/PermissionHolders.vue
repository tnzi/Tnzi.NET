<template>
  <div class="t-perm-holders">
    <NSpin :show="loading">
      <NAlert v-if="error" type="error" :bordered="false" data-test="holders-error">{{ error }}</NAlert>

      <template v-else-if="holders">
        <header class="t-perm-holders__head">
          <div class="t-perm-holders__identity">
            <span class="t-perm-holders__name">{{ holders.name }}</span>
            <code class="t-perm-holders__code">{{ holders.code }}</code>
          </div>
          <NTag size="small" :bordered="false" :type="holders.isInEffect ? 'success' : 'warning'">
            {{ t(`holders.status.${statusKey}`) }}
          </NTag>
        </header>

        <p class="t-perm-holders__summary">
          {{ t('holders.summary', { users: holders.users.length, roles: grantingRoleCount, denied: holders.deniedUsers.length }) }}
        </p>

        <NAlert v-if="!holders.isInEffect" type="warning" :bordered="false" class="t-perm-holders__block" data-test="holders-not-in-effect">
          {{ t('holders.notInEffect') }}
        </NAlert>

        <NAlert v-if="holders.userNamesHidden" type="info" :bordered="false" class="t-perm-holders__block" data-test="holders-names-hidden">
          {{ t('holders.namesHidden') }}
        </NAlert>

        <section class="t-perm-holders__block">
          <h4 class="t-perm-holders__title">{{ t('holders.rolesTitle') }}</h4>
          <p class="t-perm-holders__hint">{{ t('holders.rolesHint') }}</p>
          <p v-if="!holders.roles.length" class="t-perm-holders__empty">{{ t('holders.noRoles') }}</p>
          <ul v-else class="t-perm-holders__roles" data-test="holders-roles">
            <li
              v-for="role in holders.roles"
              :key="role.id"
              class="t-perm-holders__role"
              :class="{ 't-perm-holders__role--inert': !confersCode(role) }"
            >
              <span class="t-perm-holders__role-name">{{ role.name || t('holders.unknownRole') }}</span>
              <NTag size="small" :bordered="false" :type="stateTagType(role.state)">
                {{ t(`holders.state.${role.state}`) }}
              </NTag>
              <span class="t-perm-holders__role-members">{{ t('holders.members', { n: role.memberCount }) }}</span>
              <NButton
                v-if="canOpenRole && role.name"
                text
                size="tiny"
                type="primary"
                class="t-perm-holders__role-link"
                @click="openRole(role.id)"
              >
                {{ t('holders.openRole') }}
              </NButton>
            </li>
          </ul>
        </section>

        <section class="t-perm-holders__block" data-test="holders-users">
          <div class="t-perm-holders__title-row">
            <h4 class="t-perm-holders__title">{{ t('holders.usersTitle') }}</h4>
            <NInput
              v-model:value="keyword"
              size="small"
              clearable
              class="t-perm-holders__filter"
              :placeholder="t('holders.searchUser')"
            />
          </div>
          <TResponsiveTable
            :columns="userColumns"
            :data="filteredUsers"
            :row-key="userKey"
            size="small"
            :bordered="false"
            :pagination="filteredUsers.length > USERS_PAGE_SIZE ? { pageSize: USERS_PAGE_SIZE } : false"
            :empty-text="t('holders.noUsers')"
          />
        </section>

        <section v-if="holders.deniedUsers.length" class="t-perm-holders__block" data-test="holders-denied">
          <h4 class="t-perm-holders__title">{{ t('holders.deniedTitle') }}</h4>
          <p class="t-perm-holders__hint">{{ t('holders.deniedHint') }}</p>
          <div class="t-perm-holders__denied">
            <template v-for="user in holders.deniedUsers" :key="user.id">
              <NButton v-if="canOpenUser" text size="small" type="error" @click="openUser(user.id)">
                {{ user.userName || shortId(user.id) }}
              </NButton>
              <NTag v-else size="small" :bordered="false" type="error">{{ user.userName || shortId(user.id) }}</NTag>
            </template>
          </div>
        </section>
      </template>
    </NSpin>
  </div>
</template>

<script setup lang="ts">
/**
 * Who holds one permission code - the access-review question asked from the
 * code's side ("who can do X?"), complementing the per-user permission picture.
 *
 * Everything comes from one backend read whose account list matches the
 * runtime check account by account. Three things the screen must not lose,
 * because each is where a reviewer would otherwise draw the wrong conclusion:
 *
 *   - super admins: they hold every code without a grant row, so a list built
 *     from grants alone silently leaves out the most powerful accounts;
 *   - dormant grants: a disabled or retired code grants nothing today, but its
 *     role grants come back the moment it is re-enabled - hiding them would
 *     make re-enabling an unreviewed grant;
 *   - explicit denies: a member of a granting role who is missing from the
 *     holders is missing for a reason, and the reason is shown.
 */
import { computed, h, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NInput, NSpin, NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { makePageTranslator } from '../_shared/translate'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import {
  PermissionRoleGrantState,
  type PermissionHolderRoleDto,
  type PermissionHolderUserDto,
  type PermissionHoldersDto,
} from '@tnzi/core/services/authorization'

const props = defineProps<{
  /** Permission code to look up (matched case-insensitively by the backend). */
  code: string
}>()

const USERS_PAGE_SIZE = 20

const t = makePageTranslator('authorization.permissions')
const bridge = createAuthorizationBridge({ client: useAdminClient() })
const router = useRouter()
const { can } = usePermissionGuard()

const canOpenUser = computed(() => can('user.view'))
const canOpenRole = computed(() => can('authorization.roleFunction.view'))

const holders = ref<PermissionHoldersDto | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const keyword = ref('')

// Sequence token: switching quickly between two codes must not let the slower
// answer overwrite the newer one.
let requestSeq = 0

async function load(code: string): Promise<void> {
  const seq = ++requestSeq
  loading.value = true
  error.value = null
  keyword.value = ''
  try {
    const result = await bridge.permissions.holders(code)
    if (seq === requestSeq) holders.value = result
  } catch (e) {
    if (seq === requestSeq) {
      holders.value = null
      error.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    if (seq === requestSeq) loading.value = false
  }
}

watch(
  () => props.code,
  (code) => {
    if (code) void load(code)
  },
  { immediate: true },
)

const statusKey = computed(() => {
  const dto = holders.value
  if (!dto || dto.isInEffect) return 'inEffect'
  return dto.isRetired ? 'retired' : 'disabled'
})

const roleStates = computed(() => {
  const map = new Map<string, PermissionRoleGrantState>()
  for (const role of holders.value?.roles ?? []) map.set(role.id, role.state)
  return map
})

/** Roles whose members hold the code right now (super-admin roles included). */
const grantingRoleCount = computed(
  () => (holders.value?.roles ?? []).filter((role) => confersCode(role)).length,
)

function confersCode(role: PermissionHolderRoleDto): boolean {
  return role.state === PermissionRoleGrantState.SuperAdmin || role.state === PermissionRoleGrantState.Effective
}

function stateTagType(state: PermissionRoleGrantState): 'warning' | 'success' | 'info' | 'default' {
  switch (state) {
    case PermissionRoleGrantState.SuperAdmin:
      return 'warning'
    case PermissionRoleGrantState.Effective:
      return 'success'
    case PermissionRoleGrantState.Dormant:
      return 'info'
    default:
      return 'default'
  }
}

// Matches the id as well as the name: without `user.view` the backend sends ids
// only, and the rows show a short id the filter must still find.
const filteredUsers = computed(() => {
  const users = holders.value?.users ?? []
  const q = keyword.value.trim().toLowerCase()
  return q
    ? users.filter((user) => user.userName.toLowerCase().includes(q) || user.id.toLowerCase().includes(q))
    : users
})

function userKey(user: PermissionHolderUserDto): string {
  return user.id
}

function shortId(id: string): string {
  return `${id.slice(0, 8)}…`
}

function openUser(id: string): void {
  void router.push({ name: 'identity.users.detail', params: { id } })
}

function openRole(roleId: string): void {
  void router.push({ name: 'authorization.roleFunctions', query: { roleId } })
}

function viaTags(user: PermissionHolderUserDto) {
  const tags = user.roles.map((role) =>
    roleStates.value.get(role.id) === PermissionRoleGrantState.SuperAdmin
      ? h(NTag, { size: 'small', bordered: false, type: 'warning' }, { default: () => t('holders.via.superAdmin', { role: role.name }) })
      : h(NTag, { size: 'small', bordered: false }, { default: () => role.name || t('holders.unknownRole') }),
  )
  if (user.isDirect) {
    tags.push(h(NTag, { size: 'small', bordered: false, type: 'info' }, { default: () => t('holders.via.direct') }))
  }
  return h('span', { class: 't-perm-holders__via' }, tags)
}

const userColumns = computed<DataTableColumns<PermissionHolderUserDto>>(() => [
  {
    key: 'userName',
    title: t('holders.columns.user'),
    minWidth: 160,
    render: (user) => {
      const label = user.userName || shortId(user.id)
      return canOpenUser.value
        ? h(NButton, { text: true, type: 'primary', onClick: () => openUser(user.id) }, { default: () => label })
        : label
    },
  },
  {
    key: 'via',
    title: t('holders.columns.via'),
    minWidth: 220,
    render: (user) => viaTags(user),
  },
])
</script>

<style scoped>
.t-perm-holders__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}
.t-perm-holders__identity {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-perm-holders__name {
  font-size: 15px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-perm-holders__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  word-break: break-all;
}
.t-perm-holders__summary {
  margin: 10px 0 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-perm-holders__block {
  margin-top: 16px;
}
.t-perm-holders__title-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}
.t-perm-holders__title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-perm-holders__hint {
  margin: 4px 0 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-perm-holders__empty {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-perm-holders__filter {
  width: 220px;
  max-width: 100%;
}
.t-perm-holders__roles {
  list-style: none;
  margin: 0;
  padding: 0;
}
.t-perm-holders__role {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 7px 0;
  border-bottom: 1px solid var(--tnzi-border);
  font-size: 13px;
}
.t-perm-holders__role:last-child {
  border-bottom: none;
}
.t-perm-holders__role--inert .t-perm-holders__role-name {
  color: var(--tnzi-base-text-muted);
}
.t-perm-holders__role-name {
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-perm-holders__role-members {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-perm-holders__role-link {
  margin-left: auto;
}
.t-perm-holders__via {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px;
}
.t-perm-holders__denied {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 10px;
}
</style>
