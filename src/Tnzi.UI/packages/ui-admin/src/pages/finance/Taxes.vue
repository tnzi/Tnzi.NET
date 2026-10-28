<template>
  <TTabsPage :title="title" icon="mdi:cash-multiple" :translate="t" :sections="tabs" default-section="agencies">
    <template #agencies>
      <TCrudPage :state="agencyCrud" :all-columns="agencyColumns" :show-header="false" :row-actions="agencyActions" :translate="t">
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer :schema="agencySchema" :model="(formData ?? {}) as Record<string, unknown>" :readonly="mode === 'view'" :translate="t" />
        </template>
      </TCrudPage>
    </template>

    <template #rates>
      <TCrudPage :state="rateCrud" :all-columns="rateColumns" :show-header="false" :row-actions="rateActions" :translate="t">
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer :schema="rateSchema" :model="(formData ?? {}) as Record<string, unknown>" :readonly="mode === 'view'" :field-renderers="fieldRenderers" :translate="t" />
        </template>
      </TCrudPage>
    </template>

    <template #codes>
      <TCrudPage :state="codeCrud" :all-columns="codeColumns" :show-header="false" :row-actions="codeActions" :translate="t">
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer :schema="codeSchema" :model="(formData ?? {}) as Record<string, unknown>" :readonly="mode === 'view'" :field-renderers="fieldRenderers" :translate="t" />
        </template>
      </TCrudPage>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { h, onMounted } from 'vue'
import { NSelect } from 'naive-ui'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { createFinanceBridge, type UpsertTaxCodeDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FieldRenderContext, type FormSchemaItem } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { pagedResult } from '../../services/_mappers'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { createFinanceOptionSources } from './options'
import { amountCell } from './money'
import { filterByKeyword } from '../../utils/keyword-filter'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.taxes')
const sources = createFinanceOptionSources(bridge)

// Shared active/inactive status column for the three tax tabs.
function statusColumn<T extends { isActive?: boolean }>(): ColumnDef<T> {
  return {
    key: 'isActive',
    title: 'columns.status',
    width: 100,
    render: (r) =>
      h(TStatusBadge, {
        value: r.isActive === false ? 0 : 1,
        type: r.isActive === false ? 'default' : 'success',
        label: r.isActive === false ? t('status.inactive') : t('status.active'),
      }),
  }
}

// isActive switch shown only when editing (create defaults to active via toPayload).
const activeField: FormSchemaItem = { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', visible: (m) => !!m.id }

const title = 'tnzi.admin.modules.finance.taxes.title'
// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward.
// `show` keeps every CRUD pane mounted so switching tabs doesn't re-fetch.
const tabs: TabSection[] = [
  { name: 'agencies', label: t('tabs.agencies'), displayDirective: 'show' },
  { name: 'rates', label: t('tabs.rates'), displayDirective: 'show' },
  { name: 'codes', label: t('tabs.codes'), displayDirective: 'show' },
]

// The three lists load whole from endpoints that take no keyword, so the
// search box filters the loaded rows on the fields each tab shows.
function listFetch<T>(load: () => Promise<T[]>, searchable: (row: T) => ReadonlyArray<string | null | undefined>) {
  return async (q: { pageIndex: number; pageSize: number; searchText?: string }) => {
    const items = filterByKeyword(await load(), q.searchText, searchable)
    return pagedResult({ items, totalCount: items.length, pageIndex: q.pageIndex, pageSize: Math.max(q.pageSize, items.length || 1) })
  }
}

// ── Agencies ────────────────────────────────────────────────────
interface AgencyRow { id?: string; name?: string; description?: string | null; isActive?: boolean }

const agencyColumns: ColumnDef<AgencyRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 160, primary: true },
  { key: 'description', title: 'columns.description', minWidth: 220, render: (r) => r.description ?? EMPTY_DASH },
  statusColumn<AgencyRow>(),
]

const agencySchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  activeField,
]

const agencyCrud = useCrudPage<AgencyRow>({
  pageId: 'finance.taxes.agencies',
  // Three shells share this route (tabs are displayDirective:'show'); each
  // needs its own deep-link key or a sibling's reconcile closes this editor.
  detailUrl: 'agency',
  permission: 'finance.tax',
  columns: agencyColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: listFetch(() => bridge.taxes.agencies(), (r) => [r.name, r.description]),
  createData: async (d) => bridge.taxes.createAgency({ name: String(d.name ?? ''), description: (d.description as string | null) || null, isActive: d.isActive !== false }),
  updateData: async (id, d) => bridge.taxes.updateAgency(String(id), { name: String(d.name ?? ''), description: (d.description as string | null) || null, isActive: d.isActive !== false }),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.taxes.deleteAgency(String(id))
  },
})
const agencyActions: RowAction<AgencyRow>[] = [editAction(agencyCrud), deleteAction(agencyCrud)]

// ── Rates ───────────────────────────────────────────────────────
interface RateRow { id?: string; agencyId?: string; agencyName?: string | null; name?: string; rate?: number; isActive?: boolean }

const rateColumns: ColumnDef<RateRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 160, primary: true },
  { key: 'agencyName', title: 'columns.agency', minWidth: 140, render: (r) => r.agencyName ?? EMPTY_DASH },
  { key: 'rate', title: 'columns.rate', width: 110, render: (r) => amountCell(`${r.rate ?? 0}%`) },
  statusColumn<RateRow>(),
]

const rateSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'agencyId', labelKey: 'form.agency', label: 'Agency', type: 'finance-tax-agency', required: true },
  { key: 'rate', labelKey: 'form.rate', label: 'Rate (%)', type: 'number', required: true },
  activeField,
]

const rateCrud = useCrudPage<RateRow>({
  pageId: 'finance.taxes.rates',
  detailUrl: 'rate',
  permission: 'finance.tax',
  columns: rateColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: listFetch(() => bridge.taxes.rates(), (r) => [r.name, r.agencyName]),
  createData: async (d) => bridge.taxes.createRate({ agencyId: String(d.agencyId ?? ''), name: String(d.name ?? ''), rate: Number(d.rate ?? 0), isActive: d.isActive !== false }),
  updateData: async (id, d) => bridge.taxes.updateRate(String(id), { agencyId: String(d.agencyId ?? ''), name: String(d.name ?? ''), rate: Number(d.rate ?? 0), isActive: d.isActive !== false }),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.taxes.deleteRate(String(id))
  },
})
const rateActions: RowAction<RateRow>[] = [editAction(rateCrud), deleteAction(rateCrud)]

// ── Codes ───────────────────────────────────────────────────────
interface CodeComponentRow { taxRateId: string; rateName?: string | null; rate?: number; order?: number; isCompound?: boolean }
interface CodeRow { id?: string; name?: string; description?: string | null; isActive?: boolean; components?: CodeComponentRow[] }

/**
 * One entry of the code form's `components` model: a component object as
 * loaded from the server, or a bare rate id the multi-select just added.
 */
type ComponentEntry = string | CodeComponentRow

const componentRateId = (c: ComponentEntry) => (typeof c === 'string' ? c : c.taxRateId)

/**
 * Map the multi-select's id list back onto the entries the form already holds.
 *
 * The select only knows ids, but a component carries more than its id: the
 * compound flag (charge this rate on amount + the taxes before it) is set
 * through the API and has no control on this form. The backend rebuilds every
 * component from the payload, so an entry reduced to its id would come back
 * non-compound and every later document would carry the wrong tax.
 */
function mergeComponentSelection(selected: string[], current: ComponentEntry[]): ComponentEntry[] {
  return selected.map((id) => current.find((c) => typeof c !== 'string' && c.taxRateId === id) ?? id)
}

const codeColumns: ColumnDef<CodeRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 140, primary: true },
  {
    key: 'components',
    title: 'columns.components',
    minWidth: 220,
    render: (r) => (r.components ?? []).map((c) => `${c.rateName ?? c.taxRateId} (${c.rate}%)`).join(' + ') || EMPTY_DASH,
  },
  statusColumn<CodeRow>(),
]

const codeSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  // 组件 = 多选税率（按选择顺序生成 order）。复合标记没有界面控件、经 API 配置，但必须原样带回：
  // 后端按载荷整组重建组件，漏带 = 改个名字就把复合税码静默改成非复合（见 mergeComponentSelection）
  { key: 'components', labelKey: 'form.components', label: 'Rates', type: 'finance-tax-components', required: true },
  // 编辑时显示（创建默认可抵扣，经 toCodePayload；不显误导性 off 开关，同 activeField 约定）
  { key: 'isRecoverable', labelKey: 'form.isRecoverable', label: 'Recoverable (purchase ITC)', type: 'switch', visible: (m) => !!m.id },
  activeField,
]

function toCodePayload(d: Record<string, unknown>): UpsertTaxCodeDto {
  const entries = (d.components ?? []) as ComponentEntry[]
  return {
    name: String(d.name ?? ''),
    description: (d.description as string | null) || null,
    isActive: d.isActive !== false,
    isRecoverable: d.isRecoverable !== false,
    components: entries.map((c, index) => ({
      taxRateId: componentRateId(c),
      order: index + 1,
      isCompound: typeof c !== 'string' && c.isCompound === true,
    })),
  }
}

const codeCrud = useCrudPage<CodeRow>({
  pageId: 'finance.taxes.codes',
  detailUrl: 'code',
  permission: 'finance.tax',
  columns: codeColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: listFetch(() => bridge.taxes.codes(), (r) => [r.name, r.description, ...r.components.map((c) => c.rateName)]),
  createData: async (d) => bridge.taxes.createCode(toCodePayload(d)),
  updateData: async (id, d) => bridge.taxes.updateCode(String(id), toCodePayload(d)),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.taxes.deleteCode(String(id))
  },
})
const codeActions: RowAction<CodeRow>[] = [editAction(codeCrud), deleteAction(codeCrud)]

// ── Shared field renderers ──────────────────────────────────────
const fieldRenderers = {
  'finance-tax-agency': selectRenderer(() => sources.agencyOptions.value, { placeholder: t('form.agencyPlaceholder'), clearable: false }),
  'finance-tax-components': (ctx: FieldRenderContext) => {
    const entries = (ctx.value ?? []) as ComponentEntry[]
    return h(NSelect, {
      value: entries.map(componentRateId),
      multiple: true,
      options: sources.rateOptions.value.map((r) => ({ label: `${r.name} (${r.rate}%)`, value: r.id })),
      placeholder: t('form.componentsPlaceholder'),
      filterable: true,
      disabled: ctx.readonly,
      'onUpdate:value': (v: string[]) => ctx.onUpdate(mergeComponentSelection(v, entries)),
    })
  },
}

onMounted(() => {
  void sources.ensureRates()
  void sources.ensureAgencies()
})
</script>
