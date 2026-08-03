<template>
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NButton size="small" @click="loadModules">{{ t('refresh') }}</NButton>
    </template>

    <NCard :bordered="false" class="t-permission-page__card">
      <TMasterDetailLayout :master-width="300">
        <template #master>
          <NInput
            v-model:value="treeFilter"
            :placeholder="t('searchModule')"
            size="small"
            clearable
          />
          <NSpin :show="modulesLoading" class="mt-8px">
            <div v-if="!modules.length && !modulesLoading" class="t-permission-page__empty">
              {{ t('noModules') }}
            </div>
            <NTree
              v-else
              :data="treeData"
              :pattern="treeFilter"
              :selected-keys="selectedModuleId ? [selectedModuleId] : []"
              :default-expand-all="true"
              :show-irrelevant-nodes="false"
              block-line
              class="t-permission-page__naive-tree"
              @update:selected-keys="onSelectModule"
            />
          </NSpin>
        </template>
        <template #detail>
          <section class="t-permission-page__detail">
            <div v-if="!selectedModule" class="t-permission-page__placeholder">
              {{ t('selectPrompt') }}
            </div>
            <template v-else>
              <header class="t-permission-page__detail-header">
                <div>
                  <h3 class="t-permission-page__module-name">{{ moduleTitle }}</h3>
                  <code class="t-permission-page__module-code">{{ selectedModule.code }}</code>
                </div>
                <span class="t-permission-page__count">
                  {{ t('stats', { codes: stats.codes, surfaces: stats.surfaces, technical: stats.technical }) }}
                </span>
              </header>

              <NAlert
                v-if="crudError"
                type="error"
                closable
                class="mb-12px"
                @close="crud.dismissError"
              >
                {{ crudError.message }}
              </NAlert>

              <div class="t-permission-page__toolbar">
                <NInput
                  v-model:value="permFilter"
                  :placeholder="t('searchPermission')"
                  size="small"
                  clearable
                  class="t-permission-page__perm-filter"
                />
                <NRadioGroup v-model:value="categoryFilter" size="small">
                  <NRadioButton value="all">{{ t('filter.all') }}</NRadioButton>
                  <NRadioButton value="business">{{ t('category.business') }}</NRadioButton>
                  <NRadioButton value="technical">{{ t('category.technical') }}</NRadioButton>
                </NRadioGroup>
                <span class="t-permission-page__toolbar-spacer" />
                <NButton v-if="crud.canCreate" type="primary" size="small" @click="openCreate">
                  {{ t('actions.create') }}
                </NButton>
              </div>

              <NSpin :show="permLoading">
                <div v-if="!surfaces.length" class="t-permission-page__empty">
                  {{ t('emptyFiltered') }}
                </div>
                <div v-else class="t-permission-page__table-wrap">
                  <table class="t-permission-page__table">
                    <thead>
                      <tr>
                        <th class="t-permission-page__col-action">{{ t('columns.action') }}</th>
                        <th>{{ t('columns.code') }}</th>
                        <th>{{ t('columns.name') }}</th>
                        <th class="t-permission-page__col-status">{{ t('columns.isEnabled') }}</th>
                        <th class="t-permission-page__col-ops"></th>
                      </tr>
                    </thead>
                    <tbody v-for="surface in surfaces" :key="surface.prefix">
                      <tr class="t-permission-page__surface-row">
                        <td colspan="5">
                          <span class="t-permission-page__surface-name">{{ surfaceTitle(surface) }}</span>
                          <code class="t-permission-page__surface-prefix">{{ surface.prefix }}</code>
                          <NTag v-if="surface.isAccess" size="tiny" :bordered="false" type="info">
                            {{ t('menuEntry') }}
                          </NTag>
                          <NTag v-if="surface.technical" size="tiny" :bordered="false" type="warning">
                            {{ t('category.technical') }}
                          </NTag>
                        </td>
                      </tr>
                      <tr
                        v-for="row in surface.rows"
                        :key="row.fn.id"
                        class="t-permission-page__row"
                        :class="{ 't-permission-page__row--off': row.fn.isEnabled === false }"
                      >
                        <td class="t-permission-page__col-action">
                          <NTag size="small" :bordered="false" :type="actionTagType(row.action)">
                            {{ actionLabel(row) }}
                          </NTag>
                        </td>
                        <td><code class="t-permission-page__code">{{ row.fn.code }}</code></td>
                        <td class="t-permission-page__name-cell">
                          <span class="t-permission-page__name">
                            {{ row.fn.name }}
                            <NTag
                              v-if="!surface.technical && isTechnical(row.fn)"
                              size="tiny"
                              :bordered="false"
                              type="warning"
                            >
                              {{ t('category.technical') }}
                            </NTag>
                            <NTag v-if="!row.fn.isSystemManaged" size="tiny" :bordered="false" type="info">
                              {{ t('customBadge') }}
                            </NTag>
                          </span>
                          <span v-if="row.fn.description" class="t-permission-page__desc">
                            {{ row.fn.description }}
                          </span>
                        </td>
                        <td class="t-permission-page__col-status">
                          <TStatusBadge :value="row.fn.isEnabled ?? false" :mapping="statusMapping" />
                        </td>
                        <td class="t-permission-page__col-ops">
                          <div class="t-permission-page__ops">
                            <template v-if="crud.canUpdate">
                              <NButton
                                v-if="row.fn.isEnabled === false"
                                size="tiny"
                                quaternary
                                type="primary"
                                :loading="togglingId === row.fn.id"
                                @click="setEnabled(row.fn, true)"
                              >
                                {{ t('actions.enable') }}
                              </NButton>
                              <NPopconfirm
                                v-else
                                placement="top-end"
                                @positive-click="setEnabled(row.fn, false)"
                              >
                                <template #trigger>
                                  <NButton
                                    size="tiny"
                                    quaternary
                                    type="warning"
                                    :loading="togglingId === row.fn.id"
                                  >
                                    {{ t('actions.disable') }}
                                  </NButton>
                                </template>
                                {{ t('confirmDisable') }}
                              </NPopconfirm>
                              <NButton size="tiny" quaternary type="primary" @click="crud.openEdit(row.fn)">
                                {{ t('actions.edit') }}
                              </NButton>
                            </template>
                            <NPopconfirm
                              v-if="crud.canDelete && !row.fn.isSystemManaged"
                              placement="top-end"
                              @positive-click="removeFn(row.fn)"
                            >
                              <template #trigger>
                                <NButton size="tiny" quaternary type="error">
                                  {{ t('actions.delete') }}
                                </NButton>
                              </template>
                              {{ t('confirmDelete') }}
                            </NPopconfirm>
                          </div>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </NSpin>
            </template>
          </section>
        </template>
      </TMasterDetailLayout>
    </NCard>

    <TFormModal :state="formState" :title="formTitle" :translate="t" @submit="crud.submit()">
      <template #default="{ formData, mode }">
        <NAlert v-if="isSystemRow(formData)" :show-icon="false" type="info" class="mb-12px">
          {{ t('systemManagedHint') }}
        </NAlert>
        <TFormSchemaRenderer
          :schema="permissionFormSchema"
          :model="asModel(formData)"
          :readonly="mode === 'view'"
          :translate="t"
          :field-renderers="fieldRenderers"
        />
      </template>
    </TFormModal>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import type { TreeOption } from 'naive-ui'
import {
  NAlert, NButton, NCard, NInput, NPopconfirm, NRadioButton, NRadioGroup,
  NSelect, NSpin, NTag, NTree,
} from 'naive-ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../components/layout/TMasterDetailLayout.vue'
import TFormModal from '../../components/crud/TFormModal.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TFormSchemaRenderer, { type FieldRenderContext } from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import type { UseFormModalReturn } from '../../headless/useFormModal'
import { useSafeMessage } from '../_shared/safe-message'
import { makePageTranslator } from '../_shared/translate'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { ZH_SURFACE_LABELS } from './surface-labels'
import { permissionFormSchema } from './permission-config'
import type { FunctionModuleDto, ModuleFunctionDto } from '@tnzi/core/services/authorization'
import { PermissionCategory } from '@tnzi/core/services/authorization'

type PermissionRow = ModuleFunctionDto

/**
 * One grouped "surface" (panel) - every permission code sharing a prefix
 * (`user.view` / `user.create` / … → `user`), mirroring TPermissionMatrix's
 * grouping so operators see the same mental model in the catalogue browser
 * and the assignment matrix.
 */
interface SurfaceRowEntry {
  action: string | null
  fn: PermissionRow
}
interface SurfaceGroup {
  prefix: string
  label: string
  technical: boolean
  isAccess: boolean
  order: number
  rows: SurfaceRowEntry[]
}

const ACTION_SUFFIXES = ['view', 'create', 'update', 'delete', 'execute', 'assign', 'use'] as const
const ACTION_ORDER: Record<string, number> = { view: 0, create: 1, update: 2, delete: 3, execute: 4, assign: 5, use: 6 }
const ACTION_TAG_TYPE: Record<string, 'default' | 'success' | 'info' | 'error' | 'warning'> = {
  view: 'default', create: 'success', update: 'info', delete: 'error', execute: 'warning', assign: 'warning', use: 'info',
}

const bridge = createAuthorizationBridge({ client: useAdminClient() })
const t = makePageTranslator('authorization.permissions')
const message = useSafeMessage()

// ─── Module tree (master pane) ────────────────────────────────────────────────

const modules = ref<FunctionModuleDto[]>([])
const selectedModuleId = ref<string | null>(null)
const treeFilter = ref('')
const modulesLoading = ref(false)

const flatById = computed(() => {
  const m = new Map<string, FunctionModuleDto>()
  for (const x of modules.value) m.set(x.id, x)
  return m
})

const selectedModule = computed(() =>
  selectedModuleId.value ? flatById.value.get(selectedModuleId.value) ?? null : null,
)

const treeData = computed<TreeOption[]>(() => {
  // Build parent → children map.
  const byParent = new Map<string | undefined, FunctionModuleDto[]>()
  for (const m of modules.value) {
    const key = m.parentId ?? undefined
    if (!byParent.has(key)) byParent.set(key, [])
    byParent.get(key)!.push(m)
  }
  // Sort each level by `order` then `name`.
  for (const list of byParent.values()) {
    list.sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name))
  }
  const adapt = (parentId?: string): TreeOption[] =>
    (byParent.get(parentId) ?? []).map((m) => ({
      key: m.id,
      label: zhLabels.value?.[`module:${m.code}`] ?? m.name,
      children: byParent.has(m.id) ? adapt(m.id) : undefined,
      suffix: () =>
        h(
          NTag,
          {
            size: 'small',
            bordered: false,
            type: m.isEnabled ? 'success' : 'warning',
            class: 'ml-6px',
          },
          { default: () => m.code },
        ),
    }))
  return adapt(undefined)
})

async function loadModules(): Promise<void> {
  modulesLoading.value = true
  try {
    modules.value = await bridge.functionModules.getAll()
    // Auto-select the first root module (same order/name sort as the tree,
    // so the selection matches the tree's visible first node).
    if (!selectedModuleId.value && modules.value.length) {
      const roots = modules.value
        .filter((m) => !m.parentId)
        .sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name))
      const firstRoot = roots[0] ?? modules.value[0]
      if (firstRoot) {
        selectedModuleId.value = firstRoot.id
        await crud.refresh()
      }
    } else if (selectedModuleId.value) {
      await crud.refresh()
    }
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    modulesLoading.value = false
  }
}

function onSelectModule(keys: Array<string | number>): void {
  const key = keys[0] as string | undefined
  selectedModuleId.value = key ?? null
  permFilter.value = ''
  void crud.refresh()
}

// ─── Permission list (headless useCrudPage: data + form modal + permission
//     gating via authorization.permission.* - the grouped table below renders
//     crud.items itself instead of TCrudPage) ─────────────────────────────────

const crud = useCrudPage<PermissionRow>({
  pageId: 'authorization.permissions',
  permission: 'authorization.permission',
  columns: [],
  rowKey: (r) => String(r.id ?? ''),
  autoLoad: false, // waits for the module tree to load + auto-select
  fetchData: (q) =>
    bridge.permissions.fetch({
      ...q,
      pageIndex: 1,
      // One module's catalogue is small (largest framework module < 60 codes);
      // the grouped view needs the full set, so pagination is bypassed.
      pageSize: 500,
      filters: { moduleId: selectedModuleId.value ?? '' },
    }),
  createData: (data) => {
    const d = data as Partial<PermissionRow>
    return bridge.permissions.create({
      name: String(d.name ?? '').trim(),
      code: String(d.code ?? '').trim(),
      moduleId: selectedModuleId.value ?? '',
      description: d.description || undefined,
      order: typeof d.order === 'number' ? d.order : 0,
      category: d.category ?? PermissionCategory.Business,
    })
  },
  updateData: (id, data) => {
    const d = data as Partial<PermissionRow>
    return bridge.permissions.update(String(id), {
      name: String(d.name ?? ''),
      code: String(d.code ?? ''),
      moduleId: d.moduleId ?? selectedModuleId.value ?? '',
      description: d.description || undefined,
      order: typeof d.order === 'number' ? d.order : 0,
      category: d.category ?? null,
    })
  },
  deleteData: (ids) => bridge.permissions.delete(ids.map(String)),
})

const permissions = crud.items
const permLoading = crud.loading
const crudError = crud.error
// TFormModal's prop is typed UseFormModalReturn<unknown>; the concrete
// UseFormModalReturn<ModuleFunctionDto> is method-contravariant on `open`,
// so a widening cast is required (same shape TCrudPage passes generically).
const formState = crud.formModal as unknown as UseFormModalReturn<unknown>

// ─── Filters + grouping ───────────────────────────────────────────────────────

const permFilter = ref('')
const categoryFilter = ref<'all' | 'business' | 'technical'>('all')

const appStore = useAdminAppStore()
const zhLabels = computed(() => (appStore.locale === 'zh-cn' ? ZH_SURFACE_LABELS : null))

function isTechnical(fn: PermissionRow): boolean {
  return fn.category === PermissionCategory.Technical
}

function splitCode(code: string): { prefix: string; action: string | null } {
  const idx = code.lastIndexOf('.')
  if (idx < 0) return { prefix: code, action: null }
  const seg = code.slice(idx + 1).toLowerCase()
  if ((ACTION_SUFFIXES as readonly string[]).includes(seg)) {
    return { prefix: code.slice(0, idx), action: seg }
  }
  return { prefix: code, action: null }
}

const stats = computed(() => {
  const prefixes = new Set(permissions.value.map((p) => splitCode(p.code ?? '').prefix))
  return {
    codes: permissions.value.length,
    surfaces: prefixes.size,
    technical: permissions.value.filter(isTechnical).length,
  }
})

const filteredPermissions = computed(() => {
  const q = permFilter.value.trim().toLowerCase()
  return permissions.value.filter((p) => {
    if (categoryFilter.value === 'technical' && !isTechnical(p)) return false
    if (categoryFilter.value === 'business' && isTechnical(p)) return false
    if (!q) return true
    const zh = zhLabels.value?.[splitCode(p.code ?? '').prefix] ?? ''
    return (
      (p.name ?? '').toLowerCase().includes(q) ||
      (p.code ?? '').toLowerCase().includes(q) ||
      (p.description ?? '').toLowerCase().includes(q) ||
      zh.toLowerCase().includes(q)
    )
  })
})

const surfaces = computed<SurfaceGroup[]>(() => {
  const byPrefix = new Map<string, SurfaceGroup>()
  for (const fn of filteredPermissions.value) {
    const { prefix, action } = splitCode(fn.code ?? '')
    let s = byPrefix.get(prefix)
    if (!s) {
      s = { prefix, label: prefix, technical: true, isAccess: false, order: fn.order ?? 0, rows: [] }
      byPrefix.set(prefix, s)
    }
    s.order = Math.min(s.order, fn.order ?? 0)
    if (action === 'view') {
      // Surface label from the view code's display name: "View Users" → "Users".
      s.label = (fn.name ?? '').replace(/^View\s+/i, '') || prefix
    } else if (s.label === prefix && !s.rows.some((r) => r.action === 'view') && fn.name) {
      // Surfaces with no view code (custom standalone codes) label from the name.
      s.label = fn.name
    }
    if (!isTechnical(fn)) s.technical = false
    s.rows.push({ action, fn })
  }
  const prefixes = [...byPrefix.keys()]
  for (const s of byPrefix.values()) {
    // Module ACCESS codes: a view-only surface whose prefix parents other
    // surfaces (`ai` for `ai.agent`) gates the sidebar group, not an entity.
    const viewOnly = s.rows.length > 0 && s.rows.every((r) => r.action === 'view')
    s.isAccess = viewOnly && prefixes.some((p) => p !== s.prefix && p.startsWith(`${s.prefix}.`))
    s.rows.sort(
      (a, b) =>
        (ACTION_ORDER[a.action ?? ''] ?? 9) - (ACTION_ORDER[b.action ?? ''] ?? 9) ||
        (a.fn.order ?? 0) - (b.fn.order ?? 0),
    )
  }
  return [...byPrefix.values()].sort(
    (a, b) =>
      Number(b.isAccess) - Number(a.isAccess) || a.order - b.order || a.prefix.localeCompare(b.prefix),
  )
})

function surfaceTitle(s: SurfaceGroup): string {
  return zhLabels.value?.[s.prefix] ?? s.label
}

const moduleTitle = computed(() => {
  const m = selectedModule.value
  if (!m) return ''
  return zhLabels.value?.[`module:${m.code}`] ?? m.name
})

function actionTagType(action: string | null): 'default' | 'success' | 'info' | 'error' | 'warning' {
  return action ? ACTION_TAG_TYPE[action] ?? 'default' : 'default'
}

function actionLabel(row: SurfaceRowEntry): string {
  if (row.action) return t(`action.${row.action}`)
  // Standalone codes (no recognised action suffix) show their raw last segment.
  const code = row.fn.code ?? ''
  const i = code.lastIndexOf('.')
  return i >= 0 ? code.slice(i + 1) : code
}

const statusMapping = {
  true: { type: 'success' as const, labelKey: 'admin.shared.status.enabled' },
  false: { type: 'warning' as const, labelKey: 'admin.shared.status.disabled' },
}

// ─── Row operations ───────────────────────────────────────────────────────────

const togglingId = ref<string | null>(null)

async function setEnabled(fn: PermissionRow, enabled: boolean): Promise<void> {
  togglingId.value = fn.id
  try {
    if (enabled) await bridge.permissions.enable(String(fn.id))
    else await bridge.permissions.disable(String(fn.id))
    message.success(t(enabled ? 'actions.enableSuccess' : 'actions.disableSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    togglingId.value = null
  }
}

async function removeFn(fn: PermissionRow): Promise<void> {
  try {
    await bridge.permissions.delete([String(fn.id)])
    message.success(t('actions.deleteSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// ─── Create / edit form ───────────────────────────────────────────────────────

const formTitle = computed(() =>
  crud.formModal.mode.value === 'create' ? t('formTitleCreate') : t('formTitleEdit'),
)

function openCreate(): void {
  crud.openCreate()
  // Seed create defaults (openCreate starts from an empty object).
  const fd = crud.formModal.formData.value as Record<string, unknown> | null
  if (fd) {
    if (fd.category == null) fd.category = PermissionCategory.Business
    if (fd.order == null) fd.order = 0
  }
}

function isSystemRow(formData: unknown): boolean {
  return !!(formData as PermissionRow | null)?.isSystemManaged
}

function asModel(formData: unknown): Record<string, unknown> {
  return (formData ?? {}) as Record<string, unknown>
}

// Code-managed rows lock Code/Name/Description/Category (backend rejects or
// reverts such edits; category is a code-owned contract). Order stays a plain
// editable number field for both kinds of rows.
const locked = computed(() => !!(crud.formModal.formData.value as PermissionRow | null)?.isSystemManaged)

const fieldRenderers = {
  'perm-text': (ctx: FieldRenderContext) =>
    h(NInput, {
      value: (ctx.value as string) ?? '',
      disabled: ctx.readonly || locked.value,
      'onUpdate:value': (v: string) => ctx.onUpdate(v),
    }),
  'perm-textarea': (ctx: FieldRenderContext) =>
    h(NInput, {
      type: 'textarea',
      rows: 2,
      value: (ctx.value as string) ?? '',
      disabled: ctx.readonly || locked.value,
      'onUpdate:value': (v: string) => ctx.onUpdate(v),
    }),
  'perm-category': (ctx: FieldRenderContext) =>
    h(NSelect, {
      value: (ctx.value as string | null) ?? PermissionCategory.Business,
      options: [
        { label: t('category.business'), value: PermissionCategory.Business },
        { label: t('category.technical'), value: PermissionCategory.Technical },
      ],
      disabled: ctx.readonly || locked.value,
      'onUpdate:value': (v: string | null) => ctx.onUpdate(v ?? PermissionCategory.Business),
    }),
}

onMounted(() => {
  void loadModules()
})
</script>

<style scoped>
/* Fill-height chain (content-page iron rule): TContentPage scroll="fill"
   → card flex-fills the body → n-card__content flex column → layout grid
   claims the residual height → each pane scrolls internally. The white
   container always reaches the viewport bottom; no dead grey canvas. */
.t-permission-page__card {
  flex: 1 1 auto;
  min-height: 0;
}
.t-permission-page__card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
/* Master/detail grid, responsive stacking and pane scroll come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
.t-permission-page__naive-tree {
  margin-top: 8px;
  max-height: 64vh;
  overflow: auto;
}
.t-permission-page__detail {
  padding: 0 4px;
}
.t-permission-page__placeholder {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 60px 16px;
}
.t-permission-page__detail-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 12px;
  gap: 16px;
}
.t-permission-page__module-name {
  margin: 0;
  font-size: 18px;
}
.t-permission-page__module-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-permission-page__count {
  color: var(--tnzi-base-text-muted);
  font-size: 13px;
  white-space: nowrap;
}
.t-permission-page__toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
}
.t-permission-page__perm-filter {
  width: 240px;
  max-width: 100%;
}
.t-permission-page__toolbar-spacer {
  flex: 1 1 auto;
}
.t-permission-page__table-wrap {
  overflow-x: auto;
}
.t-permission-page__table {
  width: 100%;
  min-width: 680px;
  border-collapse: collapse;
  font-size: 13px;
}
.t-permission-page__table th {
  text-align: left;
  font-weight: 500;
  color: var(--tnzi-base-text-muted);
  padding: 6px 10px;
  border-bottom: 1px solid var(--tnzi-border);
  white-space: nowrap;
}
.t-permission-page__table td {
  padding: 7px 10px;
  border-bottom: 1px solid var(--tnzi-border);
  vertical-align: middle;
}
.t-permission-page__surface-row td {
  background: var(--tnzi-layout-bg);
  padding: 6px 10px;
}
.t-permission-page__surface-name {
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-permission-page__surface-prefix {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin: 0 8px 0 10px;
}
.t-permission-page__surface-row .n-tag {
  margin-right: 6px;
}
.t-permission-page__row--off > td:not(:last-child) {
  opacity: 0.55;
}
.t-permission-page__col-action {
  width: 88px;
  white-space: nowrap;
}
.t-permission-page__col-status {
  width: 84px;
  white-space: nowrap;
}
.t-permission-page__col-ops {
  width: 200px;
  text-align: right;
  white-space: nowrap;
}
.t-permission-page__ops {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}
.t-permission-page__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  background: var(--tnzi-layout-bg);
  padding: 1px 6px;
  border-radius: 3px;
  white-space: nowrap;
}
.t-permission-page__name-cell {
  min-width: 160px;
}
.t-permission-page__name {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--tnzi-base-text);
}
.t-permission-page__desc {
  display: block;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin-top: 2px;
}
.t-permission-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
</style>
