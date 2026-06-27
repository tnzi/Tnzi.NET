<template>
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NButton size="small" @click="loadAll">{{ t('refresh') }}</NButton>
    </template>

    <NCard :bordered="false" class="t-role-func-page__card">
      <TMasterDetailLayout :master-width="280">
        <template #master>
          <NInput
            v-model:value="roleFilter"
            :placeholder="t('searchRole')"
            size="small"
            clearable
          />
          <NSpin :show="rolesLoading" class="mt-8px">
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
        </template>
        <template #detail>
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
                <NButton size="small" :disabled="treeLoading" @click="openCompareModal">
                  <template #icon><TSvgIcon icon="mdi:compare-horizontal" :size="14" /></template>
                  {{ t('compare.button') }}
                </NButton>
                <NButton size="small" :disabled="treeLoading" @click="openCloneModal">
                  <template #icon><TSvgIcon icon="mdi:content-copy" :size="14" /></template>
                  {{ t('clone.button') }}
                </NButton>
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
              <TPermissionTree
                v-else
                :data="treeData"
                :checked-keys="checkedFunctionIds"
                check-strategy="all"
                class="t-role-func-page__naive-tree"
                @update:checked-keys="onCheckedChange"
              />
            </NSpin>
          </div>
          </section>
        </template>
      </TMasterDetailLayout>
    </NCard>

    <!--
      Compare-roles modal — three-column read-only diff.
      Source role A is fixed to the currently-selected role; admin picks
      role B from a select populated from the role catalogue. The compare
      result comes from the backend (PermissionComparisonDto).
    -->
    <NModal
      v-model:show="compareModal.show"
      :title="t('compare.title')"
      preset="card"
      class="w-[min(1080px,96vw)]"
    >
      <div class="t-role-func-page__compare-picker">
        <div class="t-role-func-page__compare-pair">
          <span class="t-role-func-page__compare-label">{{ t('compare.roleA') }}:</span>
          <NTag :bordered="false">{{ selectedRole?.name ?? '—' }}</NTag>
        </div>
        <div class="t-role-func-page__compare-pair">
          <span class="t-role-func-page__compare-label">{{ t('compare.roleB') }}:</span>
          <NSelect
            v-model:value="compareModal.targetRoleId"
            :options="otherRoleOptions"
            :placeholder="t('compare.pickRole')"
            filterable
            clearable
            size="small"
            class="w-280px"
          />
          <NButton
            type="primary"
            size="small"
            :disabled="!compareModal.targetRoleId"
            :loading="compareModal.loading"
            @click="runCompare"
          >
            {{ t('compare.run') }}
          </NButton>
        </div>
      </div>

      <NSpin :show="compareModal.loading">
        <div v-if="compareModal.result" class="t-role-func-page__compare-grid">
          <section
            v-for="bucket in compareBuckets"
            :key="bucket.id"
            class="t-role-func-page__compare-bucket"
          >
            <header class="t-role-func-page__compare-bucket-header">
              <h4 class="t-role-func-page__compare-bucket-title">{{ bucket.title }}</h4>
              <NTag size="tiny" :bordered="false" :type="bucket.tone">
                {{ bucket.rows.length }}
              </NTag>
            </header>
            <div v-if="!bucket.rows.length" class="t-role-func-page__compare-empty">
              {{ t('compare.empty') }}
            </div>
            <ul v-else class="t-role-func-page__compare-list">
              <li v-for="row in bucket.rows" :key="row.functionId">
                <div class="t-role-func-page__compare-fn-name">{{ row.functionName }}</div>
                <code class="t-role-func-page__compare-fn-code">{{ row.functionCode }}</code>
                <div v-if="row.moduleName" class="t-role-func-page__compare-fn-module">
                  {{ row.moduleName }}
                </div>
              </li>
            </ul>
          </section>
        </div>
      </NSpin>

      <template #footer>
        <div class="flex justify-end">
          <NButton @click="compareModal.show = false">{{ t('compare.close') }}</NButton>
        </div>
      </template>
    </NModal>

    <!--
      Clone-from-role modal — picks a source role, calls the backend
      clone endpoint, and refreshes the current role's assignment set.
      Idempotent on the backend (Existing assignments are not duplicated).
    -->
    <NModal
      v-model:show="cloneModal.show"
      :title="t('clone.title')"
      preset="card"
      class="w-480px"
    >
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem :label="t('clone.sourceRole')" required>
          <NSelect
            v-model:value="cloneModal.sourceRoleId"
            :options="otherRoleOptions"
            :placeholder="t('clone.pickSource')"
            filterable
            clearable
          />
        </NFormItem>
        <p class="t-role-func-page__hint">{{ t('clone.hint') }}</p>
      </NForm>
      <template #footer>
        <div class="flex justify-end gap-8px">
          <NButton @click="cloneModal.show = false">{{ t('compare.close') }}</NButton>
          <NButton
            type="primary"
            :loading="cloneModal.saving"
            :disabled="!cloneModal.sourceRoleId"
            @click="runClone"
          >
            {{ t('clone.confirm') }}
          </NButton>
        </div>
      </template>
    </NModal>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import type { SelectOption, TreeOption } from 'naive-ui'
import {
  NCard, NSpace, NButton, NInput, NSpin, NTag, NPopconfirm,
  NModal, NForm, NFormItem, NSelect,
} from 'naive-ui'
import TPermissionTree from '../../components/forms/TPermissionTree.vue'
import { useSafeMessage } from '../_shared/safeMessage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { TSvgIcon } from '@tnzi/ui'
import type {
  FunctionModuleDto,
  ModuleFunctionDto,
  PermissionComparisonDto,
  PermissionDifferenceDto,
} from '@tnzi/core/services/authorization'
import type { RoleDto } from '@tnzi/core/services/identity'
import TContentPage from '../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../components/layout/TMasterDetailLayout.vue'

interface Role { id: string; code: string; name: string; description?: string | null }

const client = useAdminClient()
const authBridge = createAuthorizationBridge({ client })
const idBridge = createIdentityBridge({ client })
const t = makePageTranslator('authorization.roleFunctions')

const message = useSafeMessage()

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
            { size: 'small', bordered: false, type: 'default', class: 'ml-6px' },
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

// ─── Compare / Clone roles ────────────────────────────────────────────────
// Both modals share the role catalogue (the same `roles.value` that powers
// the left rail). We expose a "other roles" computed so the source/target
// dropdowns can't accidentally pick the currently-selected role.

const otherRoleOptions = computed<SelectOption[]>(() =>
  roles.value
    .filter((r) => r.id !== selectedRoleId.value)
    .map((r) => ({ value: r.id, label: r.name })),
)

const compareModal = reactive({
  show: false,
  loading: false,
  targetRoleId: null as string | null,
  result: null as PermissionComparisonDto | null,
})

const compareBuckets = computed(() => {
  const r = compareModal.result
  if (!r) return []
  const roleAName = selectedRole.value?.name ?? '—'
  const roleBName = roles.value.find((x) => x.id === compareModal.targetRoleId)?.name ?? '—'
  return [
    {
      id: 'onlyInA',
      title: t('compare.onlyInA', { role: roleAName }),
      tone: 'info' as const,
      rows: r.onlyInRoleA ?? [],
    },
    {
      id: 'onlyInB',
      title: t('compare.onlyInB', { role: roleBName }),
      tone: 'warning' as const,
      rows: r.onlyInRoleB ?? [],
    },
    {
      id: 'common',
      title: t('compare.common'),
      tone: 'success' as const,
      rows: r.common ?? [],
    },
  ] as Array<{
    id: string
    title: string
    tone: 'info' | 'warning' | 'success'
    rows: PermissionDifferenceDto[]
  }>
})

function openCompareModal(): void {
  compareModal.show = true
  compareModal.targetRoleId = null
  compareModal.result = null
}

async function runCompare(): Promise<void> {
  const target = compareModal.targetRoleId
  if (!target || !selectedRoleId.value) return
  compareModal.loading = true
  try {
    compareModal.result = await authBridge.roleFunctions.compare(
      selectedRoleId.value,
      target,
    )
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    compareModal.result = null
  } finally {
    compareModal.loading = false
  }
}

const cloneModal = reactive({
  show: false,
  saving: false,
  sourceRoleId: null as string | null,
})

function openCloneModal(): void {
  cloneModal.show = true
  cloneModal.sourceRoleId = null
}

async function runClone(): Promise<void> {
  const source = cloneModal.sourceRoleId
  if (!source || !selectedRoleId.value) return
  if (source === selectedRoleId.value) {
    message.warning(t('clone.sameRoleError'))
    return
  }
  cloneModal.saving = true
  try {
    const count = await authBridge.roleFunctions.clone(selectedRoleId.value, source)
    message.success(t('clone.success', { n: count }))
    cloneModal.show = false
    // Refetch assignments so the tree reflects the merged permissions.
    await loadAssignedForRole(selectedRoleId.value)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    cloneModal.saving = false
  }
}

// Pre-select role from `?roleId=` query — used by Roles detail
// drawer's "Open Permission Editor" deep-link. Wait for the role list to
// load before selecting so the active highlight + tree render in one paint.
const route = useRoute()

onMounted(async () => {
  await loadAll()
  // Optional-chain `route`: useRoute() is undefined when the component is
  // mounted without a router (SSR / bare test mount), and reading `.query`
  // off undefined inside this async hook would surface as an unhandled
  // rejection. The deep-link pre-select is simply skipped when there's no route.
  const queryRoleId = typeof route?.query?.roleId === 'string' ? route.query.roleId : null
  if (queryRoleId && roles.value.some((r) => r.id === queryRoleId)) {
    selectedRoleId.value = queryRoleId
    await loadAssignedForRole(queryRoleId)
  }
})
</script>

<style scoped>
/* Fill-height chain (content-page iron rule): TContentPage scroll="fill"
   → card flex-fills the body → n-card__content flex column → layout grid
   claims the residual height → each pane scrolls internally. The white
   container always reaches the viewport bottom; no dead grey canvas. */
.t-role-func-page__card {
  flex: 1 1 auto;
  min-height: 0;
}
.t-role-func-page__card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
/* Master/detail grid, responsive stacking and pane scroll come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
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
  background: var(--tnzi-layout-bg);
}
.t-role-func-page__role-item.is-active {
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
  color: var(--tnzi-primary);
}
.t-role-func-page__role-name {
  font-weight: 500;
  font-size: 14px;
}
.t-role-func-page__role-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__role-item.is-active .t-role-func-page__role-code {
  color: var(--tnzi-primary);
  opacity: 0.7;
}
.t-role-func-page__tree {
  padding: 0 4px;
}
.t-role-func-page__placeholder {
  color: var(--tnzi-base-text-muted);
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
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-role-func-page__naive-tree {
  max-height: 64vh;
  overflow: auto;
}
.t-role-func-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
:deep(.t-role-func-page__fn-code) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  margin-left: 8px;
  background: var(--tnzi-layout-bg);
  padding: 1px 4px;
  border-radius: 3px;
}

/* Compare modal — three columns side by side on desktop, single column
   on phones. Each column lists the functions in that bucket as compact
   tiles (name + monospaced code + optional module hint). */
.t-role-func-page__compare-picker {
  display: flex;
  gap: 24px;
  align-items: center;
  flex-wrap: wrap;
  padding-bottom: 12px;
  margin-bottom: 12px;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-role-func-page__compare-pair {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-role-func-page__compare-label {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__compare-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}
@media (max-width: 900px) {
  .t-role-func-page__compare-grid { grid-template-columns: 1fr; }
}
.t-role-func-page__compare-bucket {
  background: var(--tnzi-layout-bg);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  padding: 12px;
  max-height: 60vh;
  overflow-y: auto;
}
.t-role-func-page__compare-bucket-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}
.t-role-func-page__compare-bucket-title {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-role-func-page__compare-empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 12px;
}
.t-role-func-page__compare-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-role-func-page__compare-list > li {
  padding: 8px 10px;
  background: var(--tnzi-container-bg);
  border-radius: 4px;
  border: 1px solid var(--tnzi-border);
}
.t-role-func-page__compare-fn-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-role-func-page__compare-fn-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  display: block;
  margin-top: 2px;
}
.t-role-func-page__compare-fn-module {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  margin-top: 2px;
}
.t-role-func-page__hint {
  margin: 0 0 12px;
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  line-height: 1.5;
}
</style>
