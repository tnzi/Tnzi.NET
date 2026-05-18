<template>
  <div class="t-entity-role-page t-page-scroll">
    <NCard :title="t('title')" :bordered="false">
      <template #header-extra>
        <NSpace>
          <NButton size="small" @click="loadAll">{{ t('refresh') }}</NButton>
        </NSpace>
      </template>

      <!-- Top selector: which entity type to manage -->
      <div class="t-entity-role-page__toolbar">
        <NSpace align="center" :wrap="false">
          <span class="t-entity-role-page__toolbar-label">{{ t('selectEntity') }}</span>
          <NSelect
            v-model:value="selectedEntityInfoId"
            :options="entityOptions"
            :placeholder="t('entityPlaceholder')"
            :loading="entitiesLoading"
            clearable
            filterable
            style="width: 360px"
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
    </NCard>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, watch } from 'vue'
import {
  NCard, NSpace, NButton, NSelect, NTag, NCheckbox, NSpin, useMessage,
} from 'naive-ui'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import type {
  EntityInfoDto,
  EntityRoleDto,
  DataAuthOperation,
} from '@tnzi/core/services/authorization'
import type { RoleDto } from '@tnzi/core/services/identity'

interface Role { id: string; code: string; name: string }

const client = useAdminClient()
const authBridge = createAuthorizationBridge({ client })
const idBridge = createIdentityBridge({ client })
const t = (key: string) => translatePageKey('authorization.entityRoles', key)

let message: { success(s: string): void; error(s: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {} }
}

const operations: DataAuthOperation[] = ['Query', 'Update', 'Delete', 'All']

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
 * Monotonic token guarding concurrent loadAssignments() calls — when the user
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
    // Refresh just the row's assignments — small payload, keeps the matrix accurate.
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

onMounted(() => {
  void loadAll()
})
</script>

<style scoped>
.t-entity-role-page {
  padding: 16px;
}
.t-entity-role-page__toolbar {
  margin-bottom: 16px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--tnzi-base-border, #efeff5);
}
.t-entity-role-page__toolbar-label {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-entity-role-page__placeholder {
  color: var(--tnzi-base-text-muted, #888);
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
  border-bottom: 1px solid var(--tnzi-base-border, #efeff5);
  text-align: center;
}
.t-entity-role-page__table thead th {
  background: var(--tnzi-base-fill, #f5f5f7);
  font-weight: 500;
  color: var(--tnzi-base-text-muted, #888);
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
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-entity-role-page__cell {
  width: 100px;
}
.t-entity-role-page__empty {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
</style>
