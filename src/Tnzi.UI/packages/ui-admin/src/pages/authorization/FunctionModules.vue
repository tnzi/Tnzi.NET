<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <!--
        System-managed rows: the backend rejects Code/Name/Description/
        ParentId changes (would be reverted by the seeder on next startup
        anyway). Lock those fields in the UI so admins don't waste time
        editing and bouncing off the API. Order + IsEnabled stay editable
        for legitimate ops use (e.g. emergency-disable a code-declared
        permission without redeploying).
      -->
      <NAlert
        v-if="(formData as Row)?.isSystemManaged"
        :show-icon="false"
        type="info"
        class="mb-12px"
      >
        {{ t('systemManagedHint') }}
      </NAlert>
      <TFormSchemaRenderer
        :schema="functionModuleFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TCrudPage>

  <!--
    Cascade-disable confirmation modal — opened by the row More menu's
    "Disable" entry. The bridge computes the impact (descendant module
    count + total affected functions) so the admin sees the blast radius
    before clicking through. Permission cascade is enforced server-side;
    this is purely a UX guardrail to prevent surprise outages.
  -->
  <NModal
    v-model:show="cascadeModal.show"
    :title="t('cascade.title')"
    preset="card"
    class="w-520px"
  >
    <NSpin :show="cascadeModal.loading">
      <div class="t-fm-page__cascade-body">
        <template v-if="cascadeModal.preview">
          <p class="t-fm-page__cascade-summary">
            {{ t('cascade.summary', { module: cascadeModal.preview.rootModuleName }) }}
          </p>
          <ul class="t-fm-page__cascade-counts">
            <li>
              <TSvgIcon icon="mdi:folder-multiple-outline" :size="14" />
              <strong>{{ cascadeModal.preview.descendantModuleCount }}</strong>
              <span>{{ t('cascade.descendantModules', { n: cascadeModal.preview.descendantModuleCount }) }}</span>
            </li>
            <li>
              <TSvgIcon icon="mdi:key-outline" :size="14" />
              <strong>{{ cascadeModal.preview.affectedFunctionCount }}</strong>
              <span>{{ t('cascade.affectedFunctions', { n: cascadeModal.preview.affectedFunctionCount }) }}</span>
            </li>
          </ul>
          <details
            v-if="cascadeModal.preview.descendantModuleCount > 0"
            class="t-fm-page__cascade-details"
          >
            <summary>{{ t('cascade.descendantsHeader') }}</summary>
            <ul class="t-fm-page__cascade-mod-list">
              <li v-for="m in cascadeModal.preview.descendantModules" :key="m.id">
                <span class="t-fm-page__cascade-mod-name">{{ m.name }}</span>
                <code class="t-fm-page__cascade-mod-code">{{ m.code }}</code>
              </li>
            </ul>
          </details>
          <NAlert type="warning" :show-icon="false" :bordered="false">
            {{ t('cascade.warning') }}
          </NAlert>
        </template>
        <p v-else-if="cascadeModal.loading" class="t-fm-page__cascade-loading">
          {{ t('cascade.loading') }}
        </p>
      </div>
    </NSpin>
    <template #footer>
      <div class="flex justify-end gap-8px">
        <NButton @click="cascadeModal.show = false">{{ t('cascade.cancel') }}</NButton>
        <NButton
          type="error"
          :loading="cascadeModal.submitting"
          :disabled="!cascadeModal.preview || cascadeModal.loading"
          @click="confirmCascadeDisable"
        >
          {{ t('cascade.confirm') }}
        </NButton>
      </div>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, onMounted } from 'vue'
import {
  NAlert,
  NButton,
  NInput,
  NModal,
  NSelect,
  NSpin,
} from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TSvgIcon } from '@tnzi/ui'
import TFormSchemaRenderer, { type FieldRenderContext } from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import {
  createAuthorizationBridge,
  type ModuleCascadePreview,
} from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import { interpolate, translatePageKey } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FunctionModuleDto } from '@tnzi/core/services/authorization'
import { functionModuleFormSchema, type FunctionModuleRow } from './function-module-config'

type Row = FunctionModuleRow

const title = 'title'
const bridge = createAuthorizationBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('authorization.functionModules', key), params)
const message = useSafeMessage()

// Build lookup for parentId → parent module (used by both parentName column
// and the parent picker). Loaded once on mount + after CRUD writes so the
// picker reflects fresh state.
const allModules = ref<FunctionModuleDto[]>([])
const moduleById = computed(() => {
  const m = new Map<string, FunctionModuleDto>()
  for (const x of allModules.value) m.set(x.id, x)
  return m
})

async function loadModuleOptions(): Promise<void> {
  try {
    allModules.value = await bridge.functionModules.getAll()
  } catch {
    allModules.value = []
  }
}

// Columns defined here (not in config.ts) so the parentName cell can resolve
// the parent module via the in-memory lookup — backend's GetModulesAsync
// doesn't .Include(Parent), so a plain `row.parentName` field would always
// be empty. Render functions close over the `moduleById` ref, so the cell
// re-renders when the modules list refreshes.
const columns: ColumnDef<Row>[] = [
  {
    key: 'name',
    title: 'columns.name',
    minWidth: 160,
    // System-managed rows get a small badge next to the name so admins
    // see at a glance that these are code-owned (and the Edit form will
    // be partly read-only). Cheap visual cue beats a separate column.
    render: (row) =>
      h('span', { class: 'inline-flex items-center gap-6px' }, [
        row.name ?? '',
        row.isSystemManaged
          ? h(
              TStatusBadge,
              {
                value: 'system',
                mapping: {
                  system: { type: 'info', labelKey: 'admin.shared.status.system' },
                },
              },
            )
          : null,
      ]),
  },
  { key: 'code', title: 'columns.code', minWidth: 150 },
  {
    key: 'parentName',
    title: 'columns.parentName',
    minWidth: 140,
    render: (row) => {
      if (!row.parentId) return h('span', { class: 'text-muted' }, '—')
      const p = moduleById.value.get(row.parentId)
      return p?.name ?? row.parentId
    },
  },
  { key: 'description', title: 'columns.description', minWidth: 160, ellipsis: { tooltip: true } },
  { key: 'order', title: 'columns.order', width: 80 },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isEnabled ?? false,
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
]

const crud = useCrudPage<Row>({
  pageId: 'authorization.functionModules',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.functionModules.fetch(query),
  createData: async (data) => {
    const created = await bridge.functionModules.create(data as never)
    await loadModuleOptions()
    return created as Row
  },
  updateData: async (id, data) => {
    const updated = await bridge.functionModules.update(String(id), data as never)
    await loadModuleOptions()
    return updated as Row
  },
  deleteData: async (ids) => {
    await bridge.functionModules.delete(ids.map(String))
    await loadModuleOptions()
  },
})

crud.refresh().catch(() => undefined)

const editingId = computed(() => (crud.formModal.formData.value as Row | null)?.id)

const parentOptions = computed(() => {
  /**
   * Walk the parent chain, counting hops. Uses a `visited` set so a cyclic
   * chain (corrupted data) terminates instead of looping forever — depth
   * cap alone wouldn't catch a 3-node cycle that re-hits the same nodes.
   */
  const depthOf = (id: string | undefined): number => {
    if (!id) return 0
    const visited = new Set<string>()
    let d = 0
    let cur = moduleById.value.get(id)
    while (cur?.parentId) {
      if (visited.has(cur.parentId)) break
      visited.add(cur.parentId)
      d += 1
      cur = moduleById.value.get(cur.parentId)
      if (d > 32) break // hard safety net for unreasonably deep trees
    }
    return d
  }
  return allModules.value
    .filter((m) => m.id !== editingId.value)
    .map((m) => ({
      label: `${'  '.repeat(depthOf(m.id))}${m.name} (${m.code})`,
      value: m.id,
    }))
})

// Code-managed rows lock Code/Name/ParentId/Description (the backend reverts
// such edits anyway); the form-schema renderer has no per-field disable, so
// the lockable fields go through custom renderers that read this flag off the
// live form model. Order stays a plain editable number field.
const locked = computed(() => !!(crud.formModal.formData.value as Row | null)?.isSystemManaged)

const fieldRenderers = {
  'fm-text': (ctx: FieldRenderContext) =>
    h(NInput, {
      value: (ctx.value as string) ?? '',
      disabled: ctx.readonly || locked.value,
      'onUpdate:value': (v: string) => ctx.onUpdate(v),
    }),
  'fm-textarea': (ctx: FieldRenderContext) =>
    h(NInput, {
      type: 'textarea',
      rows: 2,
      value: (ctx.value as string) ?? '',
      disabled: ctx.readonly || locked.value,
      'onUpdate:value': (v: string) => ctx.onUpdate(v),
    }),
  'fm-parent': (ctx: FieldRenderContext) =>
    h(NSelect, {
      value: (ctx.value as string | null) ?? null,
      options: parentOptions.value,
      placeholder: t('form.parentIdPlaceholder'),
      clearable: true,
      filterable: true,
      disabled: ctx.readonly || locked.value,
      'onUpdate:value': (v: string | null) => ctx.onUpdate(v ?? undefined),
    }),
}

// Row actions: Enable/Disable via the backend's dedicated cascading endpoints.
// Enable is purely additive (no cascade modal needed); Disable opens the
// cascade-confirmation modal — that modal IS the confirmation, so neither
// action carries an inline confirm. Delete stays appended at the end.
async function withRefresh(action: () => Promise<void>, successKey: string): Promise<void> {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
    await loadModuleOptions()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

const rowActions: RowAction<Row>[] = [
  editAction(crud),
  {
    key: 'enable',
    label: 'actions.enable',
    show: (row) => !row.isEnabled,
    onClick: (row) => {
      if (!row.id) return
      void withRefresh(() => bridge.functionModules.enable(row.id!), 'actions.enableSuccess')
    },
  },
  {
    key: 'disable',
    label: 'actions.disable',
    show: (row) => !!row.isEnabled,
    onClick: (row) => {
      if (!row.id) return
      void openCascadeModal(row.id)
    },
  },
  deleteAction(crud),
]

// ─── Cascade-disable modal ─────────────────────────────────────────────────
// Disabling a parent module flips every descendant module + all their
// functions off in one transaction. The bridge's `previewCascade` walks
// the cached module tree and returns the impact counts — we render them
// in a confirmation modal so the admin can see what they're about to do.
const cascadeModal = reactive({
  show: false,
  loading: false,
  submitting: false,
  targetId: '' as string,
  preview: null as ModuleCascadePreview | null,
})

async function openCascadeModal(moduleId: string): Promise<void> {
  cascadeModal.show = true
  cascadeModal.loading = true
  cascadeModal.targetId = moduleId
  cascadeModal.preview = null
  try {
    cascadeModal.preview = await bridge.functionModules.previewCascade(moduleId)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    cascadeModal.show = false
  } finally {
    cascadeModal.loading = false
  }
}

async function confirmCascadeDisable(): Promise<void> {
  const id = cascadeModal.targetId
  if (!id) return
  cascadeModal.submitting = true
  try {
    await bridge.functionModules.disable(id)
    message.success(t('actions.disableSuccess'))
    cascadeModal.show = false
    await crud.refresh()
    await loadModuleOptions()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    cascadeModal.submitting = false
  }
}

onMounted(() => {
  void loadModuleOptions()
})
</script>

<style scoped>
.t-fm-page__cascade-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 80px;
}
.t-fm-page__cascade-summary {
  margin: 0;
  font-size: 14px;
  color: var(--tnzi-base-text);
}
.t-fm-page__cascade-counts {
  list-style: none;
  margin: 0;
  padding: 12px 16px;
  background: var(--tnzi-layout-bg);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-fm-page__cascade-counts > li {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-fm-page__cascade-counts strong {
  color: var(--tnzi-warning);
  font-size: 16px;
  min-width: 28px;
}
.t-fm-page__cascade-details {
  font-size: 12px;
}
.t-fm-page__cascade-details summary {
  cursor: pointer;
  color: var(--tnzi-base-text-muted);
  user-select: none;
}
.t-fm-page__cascade-details summary:hover {
  color: var(--tnzi-primary);
}
.t-fm-page__cascade-mod-list {
  list-style: none;
  margin: 8px 0 0;
  padding: 0;
  max-height: 200px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.t-fm-page__cascade-mod-list > li {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  background: var(--tnzi-layout-bg);
  border-radius: 4px;
}
.t-fm-page__cascade-mod-name {
  font-size: 12px;
  color: var(--tnzi-base-text);
}
.t-fm-page__cascade-mod-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-fm-page__cascade-loading {
  text-align: center;
  color: var(--tnzi-base-text-muted);
  font-size: 13px;
  padding: 24px 8px;
  margin: 0;
}
</style>
