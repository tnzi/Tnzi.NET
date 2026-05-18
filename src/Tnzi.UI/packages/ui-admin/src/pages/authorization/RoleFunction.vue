<template>
  <div class="t-role-func-page t-page-scroll">
    <NCard :title="t('title')" :bordered="false">
      <template #header-extra>
        <NSpace>
          <NButton size="small" @click="loadAll">{{ t('refresh') }}</NButton>
        </NSpace>
      </template>

      <div class="t-role-func-page__layout">
        <!-- Left: role list -->
        <aside class="t-role-func-page__roles">
          <NInput
            v-model:value="roleFilter"
            :placeholder="t('searchRole')"
            size="small"
            clearable
          />
          <NSpin :show="rolesLoading" style="margin-top: 8px">
            <div v-if="!roles.length && !rolesLoading" class="t-role-func-page__empty">
              {{ t('noRoles') }}
            </div>
            <ul v-else class="t-role-func-page__role-list">
              <li
                v-for="role in filteredRoles"
                :key="role.id"
                class="t-role-func-page__role-item"
                :class="{ 'is-active': role.id === selectedRoleId }"
                @click="selectRole(role.id)"
              >
                <div class="t-role-func-page__role-name">{{ role.name }}</div>
                <code class="t-role-func-page__role-code">{{ role.code }}</code>
              </li>
            </ul>
          </NSpin>
        </aside>

        <!-- Right: function tree with checkboxes -->
        <section class="t-role-func-page__tree">
          <div v-if="!selectedRoleId" class="t-role-func-page__placeholder">
            {{ t('selectRolePrompt') }}
          </div>
          <div v-else>
            <header class="t-role-func-page__tree-header">
              <div>
                <h3 class="t-role-func-page__selected-name">{{ selectedRole?.name }}</h3>
                <span class="t-role-func-page__assigned-count">
                  {{ t('assignedCount', { n: checkedFunctionIds.length, total: totalFunctionCount }) }}
                </span>
              </div>
              <NSpace>
                <NButton size="small" :disabled="treeLoading" @click="reset">
                  {{ t('reset') }}
                </NButton>
                <NPopconfirm @positive-click="handleClear">
                  <template #trigger>
                    <NButton
                      size="small"
                      type="error"
                      ghost
                      :disabled="!checkedFunctionIds.length"
                    >
                      {{ t('clearAll') }}
                    </NButton>
                  </template>
                  {{ t('confirmClear') }}
                </NPopconfirm>
                <NButton
                  type="primary"
                  size="small"
                  :loading="saving"
                  :disabled="!isDirty"
                  @click="handleSave"
                >
                  {{ t('save') }}
                </NButton>
              </NSpace>
            </header>
            <NSpin :show="treeLoading">
              <div v-if="!treeData.length" class="t-role-func-page__empty">
                {{ t('noFunctions') }}
              </div>
              <NTree
                v-else
                :data="treeData"
                :checked-keys="checkedFunctionIds"
                :default-expand-all="true"
                checkable
                cascade
                check-strategy="all"
                block-line
                class="t-role-func-page__naive-tree"
                @update:checked-keys="onCheckedChange"
              />
            </NSpin>
          </div>
        </section>
      </div>
    </NCard>
  </div>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import type { TreeOption } from 'naive-ui'
import {
  NCard, NSpace, NButton, NTree, NInput, NSpin, NTag, NPopconfirm, useMessage,
} from 'naive-ui'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import type {
  FunctionModuleDto,
  ModuleFunctionDto,
} from '@tnzi/core/services/authorization'
import type { RoleDto } from '@tnzi/core/services/identity'

interface Role { id: string; code: string; name: string; description?: string | null }

const client = useAdminClient()
const authBridge = createAuthorizationBridge({ client })
const idBridge = createIdentityBridge({ client })
const t = makePageTranslator('authorization.roleFunctions')

let message: { success(s: string): void; error(s: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {} }
}

const roles = ref<Role[]>([])
const modules = ref<FunctionModuleDto[]>([])
/** moduleId → its functions (loaded lazily / cached). */
const functionsByModule = ref(new Map<string, ModuleFunctionDto[]>())

const selectedRoleId = ref<string | null>(null)
const checkedFunctionIds = ref<string[]>([])
/** Snapshot of the server-side assigned set, to compute dirty + reset. */
const originalAssignedIds = ref<Set<string>>(new Set())

const rolesLoading = ref(false)
const treeLoading = ref(false)
const saving = ref(false)
const roleFilter = ref('')

const filteredRoles = computed(() => {
  if (!roleFilter.value) return roles.value
  const q = roleFilter.value.toLowerCase()
  return roles.value.filter(
    (r) => r.name.toLowerCase().includes(q) || r.code.toLowerCase().includes(q),
  )
})

const selectedRole = computed(() =>
  roles.value.find((r) => r.id === selectedRoleId.value) ?? null,
)

const totalFunctionCount = computed(() => {
  let n = 0
  for (const list of functionsByModule.value.values()) n += list.length
  return n
})

const isDirty = computed(() => {
  if (originalAssignedIds.value.size !== checkedFunctionIds.value.length) return true
  for (const id of checkedFunctionIds.value) {
    if (!originalAssignedIds.value.has(id)) return true
  }
  return false
})

/**
 * Build a unified TreeOption[] where each module is a parent node and its
 * functions are leaves. Both module and function ids share the same key
 * namespace because NTree's `cascade` mode needs unique keys; we prefix
 * module keys to avoid colliding with function ids in the checked set, but
 * filter them out when computing checkedFunctionIds → backend payload.
 */
const treeData = computed<TreeOption[]>(() => {
  if (!modules.value.length) return []
  // Build parent → children map for modules.
  const byParent = new Map<string | undefined, FunctionModuleDto[]>()
  for (const m of modules.value) {
    const k = m.parentId ?? undefined
    if (!byParent.has(k)) byParent.set(k, [])
    byParent.get(k)!.push(m)
  }
  for (const list of byParent.values()) {
    list.sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name))
  }
  const adaptModule = (parentId?: string): TreeOption[] =>
    (byParent.get(parentId) ?? []).map((m) => {
      const functions = functionsByModule.value.get(m.id) ?? []
      const children: TreeOption[] = [
        ...adaptModule(m.id),
        ...functions
          .slice()
          .sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name))
          .map<TreeOption>((f) => ({
            key: f.id,
            label: f.name,
            suffix: () =>
              h(
                'code',
                { class: 't-role-func-page__fn-code' },
                f.code,
              ),
            disabled: !f.isEnabled,
          })),
      ]
      return {
        key: `module:${m.id}`,
        label: m.name,
        children: children.length ? children : undefined,
        suffix: () =>
          h(
            NTag,
            { size: 'small', bordered: false, type: 'default', style: 'margin-left: 6px' },
            { default: () => m.code },
          ),
        // Modules themselves aren't assignable — only their function leaves are.
        checkboxDisabled: true,
      }
    })
  return adaptModule(undefined)
})

async function loadAll(): Promise<void> {
  await Promise.all([loadRoles(), loadModulesAndFunctions()])
  // If a role was already selected, re-pull its assignments so the tree
  // reflects any external changes.
  if (selectedRoleId.value) await loadAssignedForRole(selectedRoleId.value)
}

async function loadRoles(): Promise<void> {
  rolesLoading.value = true
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
        // RoleDto exposes normalizedName (uppercased system identifier) rather
        // than a separate `code` — use that as the secondary line under the
        // display name; fall back to the name itself if empty.
        code: r.normalizedName ?? r.name,
        name: r.name,
        description: r.description ?? null,
      }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    roles.value = []
  } finally {
    rolesLoading.value = false
  }
}

async function loadModulesAndFunctions(): Promise<void> {
  treeLoading.value = true
  try {
    modules.value = await authBridge.functionModules.getAll()
    // Load functions for each module in parallel; bounded fan-out keeps it
    // friendly for typical module counts (<50).
    const next = new Map<string, ModuleFunctionDto[]>()
    await Promise.all(
      modules.value.map(async (m) => {
        try {
          next.set(m.id, await authBridge.permissions.getByModule(m.id))
        } catch {
          next.set(m.id, [])
        }
      }),
    )
    functionsByModule.value = next
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    modules.value = []
  } finally {
    treeLoading.value = false
  }
}

async function loadAssignedForRole(roleId: string): Promise<void> {
  treeLoading.value = true
  try {
    const ids = await authBridge.roleFunctions.getAssignedIds(roleId)
    originalAssignedIds.value = new Set(ids)
    checkedFunctionIds.value = [...ids]
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    originalAssignedIds.value = new Set()
    checkedFunctionIds.value = []
  } finally {
    treeLoading.value = false
  }
}

async function selectRole(roleId: string): Promise<void> {
  if (isDirty.value) {
    // Defer to the user: just keep their dirty state if they click another
    // role — they'd lose work. Pop a confirm? For now we keep selection
    // sticky on dirty state — explicit save/reset is required to switch.
    const ok = confirm(t('confirmSwitchDirty'))
    if (!ok) return
  }
  selectedRoleId.value = roleId
  await loadAssignedForRole(roleId)
}

function onCheckedChange(keys: Array<string | number>): void {
  // Strip module:XXX keys (modules are never assigned themselves) — only
  // raw function IDs go to the backend.
  checkedFunctionIds.value = keys
    .map(String)
    .filter((k) => !k.startsWith('module:'))
}

function reset(): void {
  checkedFunctionIds.value = [...originalAssignedIds.value]
}

async function handleSave(): Promise<void> {
  if (!selectedRoleId.value) return
  saving.value = true
  try {
    await authBridge.roleFunctions.setForRole(selectedRoleId.value, checkedFunctionIds.value)
    originalAssignedIds.value = new Set(checkedFunctionIds.value)
    message.success(t('saveSuccess'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

async function handleClear(): Promise<void> {
  if (!selectedRoleId.value) return
  saving.value = true
  try {
    await authBridge.roleFunctions.clearForRole(selectedRoleId.value)
    originalAssignedIds.value = new Set()
    checkedFunctionIds.value = []
    message.success(t('clearSuccess'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  void loadAll()
})
</script>

<style scoped>
.t-role-func-page {
  padding: 16px;
}
.t-role-func-page__layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 16px;
  min-height: 520px;
}
.t-role-func-page__roles {
  border-right: 1px solid var(--tnzi-base-border, #efeff5);
  padding-right: 16px;
}
.t-role-func-page__role-list {
  list-style: none;
  padding: 0;
  margin: 8px 0 0;
  max-height: 64vh;
  overflow: auto;
}
.t-role-func-page__role-item {
  padding: 8px 10px;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  margin-bottom: 4px;
  transition: background-color 0.15s;
}
.t-role-func-page__role-item:hover {
  background: var(--tnzi-base-fill, #f5f5f7);
}
.t-role-func-page__role-item.is-active {
  background: var(--tnzi-primary-color-suppl, rgba(6, 182, 212, 0.12));
  color: var(--tnzi-primary-color, #06B6D4);
}
.t-role-func-page__role-name {
  font-weight: 500;
  font-size: 14px;
}
.t-role-func-page__role-code {
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-role-func-page__role-item.is-active .t-role-func-page__role-code {
  color: var(--tnzi-primary-color, #06B6D4);
  opacity: 0.7;
}
.t-role-func-page__tree {
  padding: 0 4px;
}
.t-role-func-page__placeholder {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 60px 16px;
}
.t-role-func-page__tree-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
  gap: 16px;
  flex-wrap: wrap;
}
.t-role-func-page__selected-name {
  margin: 0 0 4px;
  font-size: 18px;
}
.t-role-func-page__assigned-count {
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
}
.t-role-func-page__naive-tree {
  max-height: 64vh;
  overflow: auto;
}
.t-role-func-page__empty {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
:deep(.t-role-func-page__fn-code) {
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-left: 8px;
  background: var(--tnzi-base-fill, #f5f5f7);
  padding: 1px 4px;
  border-radius: 3px;
}
</style>
