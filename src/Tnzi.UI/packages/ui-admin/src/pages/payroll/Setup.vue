<template>
  <TTabsPage :title="title" icon="mdi:cog-outline" :translate="t" :sections="tabs" default-section="components">
    <template #components>
      <TCrudPage :state="componentCrud" :all-columns="componentColumns" :show-header="false" :row-actions="componentActions" :translate="t">
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer :schema="componentFormSchema" :model="(formData ?? {}) as Record<string, unknown>" :readonly="mode === 'view'" :field-renderers="fieldRenderers" :translate="t" />
        </template>
      </TCrudPage>
    </template>

    <template #structures>
      <TCrudPage :state="structureCrud" :all-columns="structureColumns" :show-header="false" :row-actions="structureActions" :translate="t" :form-modal-width="720">
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer :schema="structureFormSchema" :model="(formData ?? {}) as Record<string, unknown>" :readonly="mode === 'view'" :field-renderers="fieldRenderers" :translate="t" />
        </template>
      </TCrudPage>
    </template>

    <template #brackets>
      <TCrudPage :state="bracketCrud" :all-columns="bracketColumns" :show-header="false" :row-actions="bracketActions" :translate="t" :form-modal-width="720">
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer :schema="bracketFormSchema" :model="(formData ?? {}) as Record<string, unknown>" :readonly="mode === 'view'" :field-renderers="fieldRenderers" :translate="t" />
        </template>
      </TCrudPage>
    </template>

    <template #packs>
      <div class="pr-setup__packs">
        <p class="pr-setup__packs-hint">{{ t('packs.hint') }}</p>
        <TCrudPage :state="packsCrud" :all-columns="packColumns" :show-header="false" :row-actions="packActions" :translate="t" />
      </div>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { h, onMounted, reactive } from 'vue'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createPayrollBridge } from '../../services/bridges/payroll-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FieldRenderContext } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createPayrollOptionSources } from './options'
import { tsToIsoDate, isoDateToLocalTs } from '../finance/money'
import StructureLinesEditor from './components/StructureLinesEditor.vue'
import BracketRowsEditor from './components/BracketRowsEditor.vue'
import {
  buildComponentColumns, componentFormSchema, toComponentPayload, type ComponentRow,
  buildStructureColumns, structureFormSchema, toStructurePayload, type StructureRow,
  buildBracketColumns, bracketFormSchema, toBracketPayload, type BracketRow,
  buildPackColumns, type PackRow,
} from './setup-config'

const bridge = createPayrollBridge({ client: useAdminClient() })
const t = makePageTranslator('payroll.setup')
const sources = createPayrollOptionSources(bridge)
const message = useSafeMessage()
const { can } = usePermissionGuard()

const title = 'tnzi.admin.modules.payroll.setup.title'
const tabs: TabSection[] = [
  { name: 'components', label: t('tabs.components'), displayDirective: 'show' },
  { name: 'structures', label: t('tabs.structures'), displayDirective: 'show' },
  { name: 'brackets', label: t('tabs.brackets'), displayDirective: 'show' },
  { name: 'packs', label: t('tabs.packs'), displayDirective: 'show' },
]

const toIso = (v: unknown): string => (typeof v === 'number' ? tsToIsoDate(v) : typeof v === 'string' && v ? v : tsToIsoDate(Date.now()))

// ── Components ────────────────────────────────────────────────────
const componentColumns = buildComponentColumns(t)
const componentCrud = useCrudPage<ComponentRow>({
  pageId: 'payroll.setup.components',
  permission: 'payroll.config',
  columns: componentColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.components.fetch(q),
  createData: (d) => bridge.components.create(toComponentPayload(d)),
  updateData: (id, d) => bridge.components.update(String(id), toComponentPayload(d)),
  deleteData: (ids) => bridge.components.delete(ids.map(String)),
})
const componentActions: RowAction<ComponentRow>[] = [editAction(componentCrud), deleteAction(componentCrud)]

// ── Structures ────────────────────────────────────────────────────
const structureColumns = buildStructureColumns(t)
const structureCrud = useCrudPage<StructureRow>({
  pageId: 'payroll.setup.structures',
  permission: 'payroll.config',
  columns: structureColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.structures.fetch(q),
  createData: (d) => bridge.structures.create(toStructurePayload(d)),
  updateData: (id, d) => bridge.structures.update(String(id), toStructurePayload(d)),
  deleteData: (ids) => bridge.structures.delete(ids.map(String)),
})

// Edit must hydrate the lines (list projection carries none).
async function openStructureEdit(row: StructureRow) {
  const full = await bridge.structures.getById(String(row.id ?? ''))
  if (!full) return
  structureCrud.openEdit({
    id: full.id,
    name: full.name,
    description: full.description,
    frequency: full.frequency,
    isActive: full.isActive,
    lines: full.lines.map((l) => ({
      componentId: l.componentId,
      sequence: l.sequence,
      formulaOverride: l.formulaOverride ?? null,
      amountOverride: l.amountOverride ?? null,
      conditionOverride: l.conditionOverride ?? null,
    })),
  })
}

const structureActions: RowAction<StructureRow>[] = [
  { key: 'edit', show: () => structureCrud.canUpdate, onClick: (row) => void openStructureEdit(row) },
  deleteAction(structureCrud),
]

// ── Brackets ──────────────────────────────────────────────────────
const bracketColumns = buildBracketColumns(t)
const bracketCrud = useCrudPage<BracketRow>({
  pageId: 'payroll.setup.brackets',
  permission: 'payroll.config',
  columns: bracketColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.brackets.fetch(q),
  createData: (d) => bridge.brackets.create(toBracketPayload(d, toIso)),
  updateData: (id, d) => bridge.brackets.update(String(id), toBracketPayload(d, toIso)),
  deleteData: (ids) => bridge.brackets.delete(ids.map(String)),
})

async function openBracketEdit(row: BracketRow) {
  const full = await bridge.brackets.getById(String(row.id ?? ''))
  if (!full) return
  bracketCrud.openEdit({
    id: full.id,
    code: full.code,
    name: full.name,
    description: full.description,
    effectiveFrom: full.effectiveFrom ? (isoDateToLocalTs(full.effectiveFrom) as unknown as string) : undefined,
    isActive: full.isActive,
    rows: full.rows.map((r) => ({
      sequence: r.sequence,
      lowerBound: r.lowerBound,
      upperBound: r.upperBound ?? null,
      rate: r.rate,
      quickDeduction: r.quickDeduction ?? null,
    })),
  })
}

const bracketActions: RowAction<BracketRow>[] = [
  { key: 'edit', show: () => bracketCrud.canUpdate, onClick: (row) => void openBracketEdit(row) },
  deleteAction(bracketCrud),
]

// ── Country packs ─────────────────────────────────────────────────
// 框架永不内置税表内容；pack 由消费应用注册 IPayrollCountryPack，这里列出并按 code 触发幂等播种。
// 只读 useCrudPage(省略写回调)：加载失败走框架错误链而非静默空表。
const packColumns = buildPackColumns(t)
const packsCrud = useCrudPage<PackRow>({
  pageId: 'payroll.setup.packs',
  columns: packColumns,
  rowKey: (r) => String(r.code ?? ''),
  fetchData: (q) => bridge.countryPacks.fetch(q),
})

// 逐 code 忙态(单标量会被并发播种互相覆写/提前解禁)
const seeding = reactive(new Set<string>())

async function seedPack(row: PackRow) {
  const code = String(row.code ?? '')
  seeding.add(code)
  try {
    const result = await bridge.countryPacks.seed(code)
    message.success(t('packs.seedSuccess', {
      code,
      components: result.componentsSeeded,
      brackets: result.bracketTablesSeeded,
    }))
    // 播种产物落在组件/税级表：刷新两个列表并让结构编辑器的组件下拉缓存重取,
    // 否则「播种→建结构」要整页刷新才能引用新组件
    void componentCrud.refresh()
    void bracketCrud.refresh()
    void sources.refreshComponents()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    seeding.delete(code)
  }
}

const packActions: RowAction<PackRow>[] = [
  {
    key: 'seed',
    label: 'packs.seed',
    type: 'primary',
    show: () => can('payroll.pack.execute'),
    disabled: (row) => seeding.has(String(row.code ?? '')),
    confirm: 'packs.seedConfirm',
    onClick: (row) => void seedPack(row),
  },
]

// ── Shared field renderers ──────────────────────────────────────
const fieldRenderers = {
  'payroll-account': selectRenderer(() => sources.leafAccountOptions.value, { placeholder: t('form.accountPlaceholder') }),
  'payroll-structure-lines': (ctx: FieldRenderContext) =>
    h(StructureLinesEditor, {
      value: (ctx.value ?? []) as never,
      components: sources.componentList.value,
      readonly: ctx.readonly,
      translate: t,
      'onUpdate:value': (v: unknown) => ctx.onUpdate(v),
    }),
  'payroll-bracket-rows': (ctx: FieldRenderContext) =>
    h(BracketRowsEditor, {
      value: (ctx.value ?? []) as never,
      readonly: ctx.readonly,
      translate: t,
      'onUpdate:value': (v: unknown) => ctx.onUpdate(v),
    }),
}

onMounted(() => {
  void sources.ensureComponents()
  void sources.ensureLeafAccounts()
})
</script>

<style scoped>
.pr-setup__packs {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  min-height: 0;
}

.pr-setup__packs > :last-child {
  flex: 1;
  min-height: 0;
}

.pr-setup__packs-hint {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--tnzi-text-3, #999);
}
</style>
