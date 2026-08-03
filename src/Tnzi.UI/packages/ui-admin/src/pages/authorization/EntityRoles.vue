<template>
  <TTabsPage :title="t('title')" :translate="t" :sections="sections">
    <template #actions>
      <NButton size="small" @click="loadAll">{{ t('refresh') }}</NButton>
    </template>

    <!-- Matrix mode - pick an entity, edit role×operation cells. -->
    <template #matrix>
          <div class="t-entity-role-page__toolbar">
            <NSpace align="center" :wrap="false">
              <span class="t-entity-role-page__toolbar-label">{{ t('selectEntity') }}</span>
              <NSelect
                v-model:value="selectedEntityInfoId"
                :options="entityOptions"
                :placeholder="t('entityPlaceholder')"
                :loading="entitiesLoading"
                size="small"
                clearable
                filterable
                class="w-360px max-w-full"
              />
              <NTag
                v-if="selectedEntityInfo"
                size="small"
                :type="selectedEntityInfo.isDataAuthEnabled ? 'success' : 'warning'"
                :bordered="false"
              >
                {{ selectedEntityInfo.isDataAuthEnabled ? t('status.dataAuthOn') : t('status.dataAuthOff') }}
              </NTag>
            </NSpace>
          </div>

          <div v-if="!selectedEntityInfoId" class="t-entity-role-page__placeholder">
            {{ t('selectPrompt') }}
          </div>

          <div v-else>
            <NSpin :show="matrixLoading">
              <div v-if="!roles.length" class="t-entity-role-page__empty">
                {{ t('noRoles') }}
              </div>
              <div v-else class="t-entity-role-page__matrix">
                <table class="t-entity-role-page__table">
                  <thead>
                    <tr>
                      <th class="t-entity-role-page__role-col">{{ t('matrix.roleHeader') }}</th>
                      <th v-for="op in operations" :key="op" :title="t(`operations.${op}.tip`)">
                        {{ t(`operations.${op}.label`) }}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="role in roles" :key="role.id">
                      <td class="t-entity-role-page__role-cell">
                        <div class="t-entity-role-page__role-name">{{ role.name }}</div>
                        <code class="t-entity-role-page__role-code">{{ role.code }}</code>
                      </td>
                      <td v-for="op in operations" :key="op" class="t-entity-role-page__cell">
                        <NCheckbox
                          :checked="isAssigned(role.id, op)"
                          :disabled="pendingKey === cellKey(role.id, op)"
                          @update:checked="(v: boolean) => onToggle(role.id, op, v)"
                        />
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </NSpin>
          </div>
    </template>

    <!--
      Inspect-user tab - read-only view of a user's effective data
      permissions. The admin picks a user + clicks "Look up". Useful for
      diagnosing "why can / can't user X do operation Y?" without flipping
      between the user, role, and entity-role pages.
    -->
    <template #inspect>
          <p class="t-entity-role-page__hint">{{ t('inspect.hint') }}</p>
          <NSpace class="t-entity-role-page__inspect-toolbar" align="center">
            <TUserSelector
              :value="inspect.userId || null"
              :fetcher="userFetcher"
              :placeholder="t('inspect.userIdPlaceholder')"
              size="small"
              class="w-320px max-w-full"
              @update:value="onInspectUserChange"
            />
            <NButton
              type="primary"
              size="small"
              :loading="inspect.loading"
              :disabled="!inspect.userId"
              @click="runInspect"
            >
              {{ t('inspect.lookup') }}
            </NButton>
          </NSpace>
          <NSpin :show="inspect.loading">
            <div
              v-if="inspect.hasQueried && !inspect.results.length"
              class="t-entity-role-page__empty"
            >
              {{ t('inspect.emptyResult') }}
            </div>
            <TResponsiveTable
              v-else-if="inspect.results.length"
              :columns="inspectColumns"
              :data="inspect.results"
              :pagination="{ pageSize: 20 }"
              :bordered="false"
              size="small"
              :row-key="(r: EntityRoleDto) => r.id"
            />
          </NSpin>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, reactive, ref, onMounted, watch } from 'vue'
import {
  NSpace, NButton, NSelect, NTag, NCheckbox, NSpin,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TUserSelector from '../../components/forms/TUserSelector.vue'
import type { SelectorOption } from '../../components/forms/_selector-factory'
import type { DataTableColumns } from 'naive-ui'
import { useSafeMessage } from '../_shared/safe-message'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import type {
  EntityInfoDto,
  EntityRoleDto,
  DataAuthOperation,
} from '@tnzi/core/services/authorization'
import type { RoleDto } from '@tnzi/core/services/identity'
import TTabsPage from '../../components/layout/TTabsPage.vue'

interface Role { id: string; code: string; name: string }

const client = useAdminClient()
const authBridge = createAuthorizationBridge({ client })
const idBridge = createIdentityBridge({ client })
const t = makePageTranslator('authorization.entityRoles')

const message = useSafeMessage()

const operations: DataAuthOperation[] = ['Query', 'Update', 'Delete', 'All']

// Primary tabs - TTabsPage owns `?section=` deep-linking + Back/Forward.
// Both panes own their vertical scroll (matrix table + inspect list).
const sections = computed(() => [
  { name: 'matrix', label: t('tabs.matrix'), scroll: true },
  { name: 'inspect', label: t('tabs.inspect'), scroll: true },
])

const entities = ref<EntityInfoDto[]>([])
const roles = ref<Role[]>([])
const assignments = ref<EntityRoleDto[]>([])
const selectedEntityInfoId = ref<string | null>(null)
const entitiesLoading = ref(false)
const matrixLoading = ref(false)
const pendingKey = ref<string | null>(null)

const entityOptions = computed(() =>
  entities.value.map((e) => ({
    label: e.displayName ? `${e.displayName} (${e.name})` : e.name,
    value: e.id,
  })),
)
const selectedEntityInfo = computed(() =>
  entities.value.find((e) => e.id === selectedEntityInfoId.value) ?? null,
)

const assignmentIndex = computed(() => {
  // key = roleId|operation → isEnabled boolean (assignment row exists & enabled)
  const idx = new Map<string, boolean>()
  for (const a of assignments.value) {
    idx.set(`${a.roleId}|${a.operation}`, a.isEnabled)
  }
  return idx
})

function cellKey(roleId: string, op: DataAuthOperation): string {
  return `${roleId}|${op}`
}

function isAssigned(roleId: string, op: DataAuthOperation): boolean {
  return assignmentIndex.value.get(cellKey(roleId, op)) === true
}

async function loadAll(): Promise<void> {
  await Promise.all([loadEntities(), loadRoles()])
  if (selectedEntityInfoId.value) await loadAssignments()
}

async function loadEntities(): Promise<void> {
  entitiesLoading.value = true
  try {
    entities.value = await authBridge.entityInfos.getAll()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    entities.value = []
  } finally {
    entitiesLoading.value = false
  }
}

async function loadRoles(): Promise<void> {
  try {
    const result = await idBridge.roles.fetch({
      pageIndex: 1,
      pageSize: 500,
      sortField: 'name',
      sortOrder: 'asc' as const,
      searchText: '',
      filters: {},
    })
    roles.value = result.items
      .filter((r): r is RoleDto & { id: string; name: string } => !!(r.id && r.name))
      .map((r) => ({
        id: r.id,
        code: r.normalizedName ?? r.name,
        name: r.name,
      }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    roles.value = []
  }
}

/**
 * Monotonic token guarding concurrent loadAssignments() calls - when the user
 * flicks between entities the older request can still be in flight; without
 * this guard its late resolve would overwrite the newer entity's data.
 */
let assignmentsFetchToken = 0

async function loadAssignments(): Promise<void> {
  if (!selectedEntityInfoId.value) {
    assignments.value = []
    return
  }
  const myToken = ++assignmentsFetchToken
  const myEntityId = selectedEntityInfoId.value
  matrixLoading.value = true
  try {
    const result = await authBridge.entityRoles.getByEntityInfo(myEntityId)
    // Stale-response guard: a newer call has taken over, drop our result.
    if (myToken !== assignmentsFetchToken) return
    assignments.value = result
  } catch (e) {
    if (myToken !== assignmentsFetchToken) return
    message.error(e instanceof Error ? e.message : String(e))
    assignments.value = []
  } finally {
    if (myToken === assignmentsFetchToken) matrixLoading.value = false
  }
}

async function onToggle(roleId: string, op: DataAuthOperation, value: boolean): Promise<void> {
  if (!selectedEntityInfoId.value) return
  const key = cellKey(roleId, op)
  pendingKey.value = key
  try {
    await authBridge.entityRoles.setCell({
      entityInfoId: selectedEntityInfoId.value,
      roleId,
      operation: op,
      enable: value,
    })
    // Refresh just the row's assignments - small payload, keeps the matrix accurate.
    await loadAssignments()
    message.success(value ? t('toggleOnSuccess') : t('toggleOffSuccess'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    pendingKey.value = null
  }
}

// Re-pull assignments when the selected entity changes.
watch(selectedEntityInfoId, () => {
  void loadAssignments()
})

// ─── Inspect User tab ─────────────────────────────────────────────────────
// Read-only view of a user's effective entity-roles. Uses the
// `getForUser(userId)` bridge method which aggregates across every role
// the user holds. Result is a flat list rendered as a table grouped by
// entity. `hasQueried` lets us show the "no results" placeholder only
// after a lookup ran (not on initial render).

const inspect = reactive({
  userId: '',
  loading: false,
  hasQueried: false,
  results: [] as EntityRoleDto[],
})

const entityNameById = computed(() => {
  const m = new Map<string, string>()
  for (const e of entities.value) {
    m.set(e.id, e.displayName ?? e.name)
  }
  return m
})

// Inspect-user picker fetcher (reuses the shared TUserSelector) - remote search
// users by keyword so admins pick a user instead of pasting a raw userId.
const userFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  try {
    const res = await idBridge.users.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: keyword.trim(),
      sortField: undefined,
      sortOrder: null,
      filters: {},
    })
    return res.items.map((u) => ({
      label: u.email ? `${u.userName} (${u.email})` : u.userName,
      value: u.id,
    }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    return []
  }
}

function onInspectUserChange(v: unknown): void {
  inspect.userId = (v as string | null) ?? ''
}

async function runInspect(): Promise<void> {
  const userId = inspect.userId.trim()
  if (!userId) return
  inspect.loading = true
  try {
    inspect.results = await authBridge.entityRoles.getForUser(userId)
    inspect.hasQueried = true
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    inspect.results = []
    inspect.hasQueried = true
  } finally {
    inspect.loading = false
  }
}

const inspectColumns = computed<DataTableColumns<EntityRoleDto>>(() => [
  {
    title: t('inspect.cols.entity'),
    key: 'entityInfoId',
    minWidth: 220,
    render: (row) =>
      entityNameById.value.get(row.entityInfoId) ?? t('inspect.unknownEntity'),
  },
  {
    title: t('inspect.cols.operation'),
    key: 'operation',
    width: 120,
    render: (row) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: 'info' },
        () => t(`operations.${row.operation}.label`),
      ),
  },
  {
    title: t('inspect.cols.filter'),
    key: 'filter',
    minWidth: 200,
    ellipsis: { tooltip: true },
    render: (row) =>
      row.filter
        ? h(
            'code',
            { class: 'tnzi-mono text-11px' },
            row.filter,
          )
        : EMPTY_DASH,
  },
  {
    title: t('inspect.cols.enabled'),
    key: 'isEnabled',
    width: 110,
    align: 'center',
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: row.isEnabled ? 'success' : 'warning',
        },
        () => (row.isEnabled ? t('inspect.statusOn') : t('inspect.statusOff')),
      ),
  },
])

onMounted(() => {
  void loadAll()
})
</script>

<style scoped>
.t-entity-role-page__toolbar {
  margin-bottom: 16px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--tnzi-border);
}
.t-entity-role-page__toolbar-label {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-entity-role-page__placeholder {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 60px 16px;
}
.t-entity-role-page__matrix {
  overflow-x: auto;
}
.t-entity-role-page__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
.t-entity-role-page__table th,
.t-entity-role-page__table td {
  padding: 10px 16px;
  border-bottom: 1px solid var(--tnzi-border);
  text-align: center;
}
.t-entity-role-page__table thead th {
  background: var(--tnzi-layout-bg);
  font-weight: 500;
  color: var(--tnzi-base-text-muted);
  text-transform: capitalize;
  font-size: 12px;
  letter-spacing: 0.04em;
}
.t-entity-role-page__role-col {
  text-align: left !important;
  min-width: 200px;
}
.t-entity-role-page__role-cell {
  text-align: left !important;
}
.t-entity-role-page__role-name {
  font-weight: 500;
  font-size: 14px;
}
.t-entity-role-page__role-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-entity-role-page__cell {
  width: 100px;
}
.t-entity-role-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
.t-entity-role-page__hint {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  line-height: 1.5;
  margin: 0 0 12px;
}
.t-entity-role-page__inspect-toolbar {
  margin-bottom: 16px;
}
</style>
