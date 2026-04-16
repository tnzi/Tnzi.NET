<template>
  <div class="t-crud-page">
    <div class="t-crud-page__header">
      <slot name="header">
        <h2 v-if="title" class="t-crud-page__title">{{ title }}</h2>
      </slot>
    </div>

    <div class="t-crud-page__search">
      <slot name="search">
        <NInput
          :value="props.state.query.value.searchText"
          :placeholder="t('admin.crud.searchPlaceholder')"
          clearable
          @update:value="onSearchInput"
        />
      </slot>
    </div>

    <TCrudToolbar
      :translate="props.translate"
      @refresh="onRefresh"
      @export="onExport"
      @import="onImport"
      @open-columns="showColumnSetting = !showColumnSetting"
    >
      <template #primary>
        <slot name="primary">
          <NButton
            v-if="showCreate"
            type="primary"
            class="t-crud-page__create"
            @click="props.state.openCreate"
          >
            {{ t('admin.crud.create') }}
          </NButton>
        </slot>
      </template>
      <template #left>
        <slot name="toolbarLeft" />
      </template>
      <template #right>
        <slot name="toolbarRight" />
        <TCrudColumnSetting
          :settings="props.state.columnSettings"
          :all-columns="allColumns"
          :show="showColumnSetting"
          :translate="props.translate"
          @update:show="(v: boolean) => (showColumnSetting = v)"
        >
          <template #trigger>
            <slot name="columnExtra" />
          </template>
        </TCrudColumnSetting>
      </template>
    </TCrudToolbar>

    <TBatchActions
      :state="batchActionsState"
      :translate="props.translate"
    >
      <template #default="{ selectedIds }">
        <slot name="batchActions" :selectedIds="castSelectedIds(selectedIds)" />
      </template>
    </TBatchActions>

    <NDataTable
      :data="dataTableData"
      :columns="dataTableColumns"
      :loading="props.state.loading.value"
      :row-key="dataTableRowKey"
    />

    <NPagination
      :page="props.state.query.value.pageIndex"
      :item-count="props.state.total.value"
      :page-size="props.state.query.value.pageSize"
      @update:page="props.state.setPage"
      @update:page-size="props.state.setPageSize"
    />

    <TFormModal
      :state="formModalState"
      :title="modalTitle"
      :translate="props.translate"
      @submit="props.state.submit"
    >
      <template #default="{ formData, mode }">
        <slot
          name="form"
          :formData="castFormData(formData)"
          :mode="mode"
        />
      </template>
      <template #footer>
        <slot name="formFooter" />
      </template>
    </TFormModal>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed, h, ref, useSlots } from 'vue'
import { NButton, NDataTable, NInput, NPagination } from 'naive-ui'
import TCrudToolbar from './TCrudToolbar.vue'
import TCrudColumnSetting from './TCrudColumnSetting.vue'
import TBatchActions from './TBatchActions.vue'
import TFormModal from './TFormModal.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { UseBatchActionsReturn } from '../../headless/useBatchActions'
import type { UseFormModalReturn, FormModalMode } from '../../headless/useFormModal'

// Exported so external wrappers / higher-order components can reference
// TCrudPage's Props shape without duplicating the type.
// eslint-disable-next-line vue/prefer-define-options
export interface TCrudPageProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  allColumns: ColumnDef[]
  title?: string
  rowKey?: (row: T) => TId
  showCreate?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TCrudPageProps<T, TId>>(), {
  title: undefined,
  rowKey: undefined,
  showCreate: true,
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  search?: () => unknown
  primary?: () => unknown
  toolbarLeft?: () => unknown
  toolbarRight?: () => unknown
  columnExtra?: () => unknown
  batchActions?: (props: { selectedIds: TId[] }) => unknown
  form?: (props: { formData: Partial<T> | null; mode: FormModalMode | null }) => unknown
  formFooter?: () => unknown
  rowActions?: (props: { row: T }) => unknown
}>()

const slots = useSlots()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

const showColumnSetting = ref(false)

const effectiveRowKey = computed<(row: T) => TId>(
  () => props.rowKey ?? props.state.rowKey,
)

// --- Template-bound adapters (Vue templates can't host TS casts, so we
// predefine the few unavoidable variance bridges here and bind them by name).
const batchActionsState = computed(
  () => props.state.batchActions as unknown as UseBatchActionsReturn<unknown>,
)
const formModalState = computed(
  () => props.state.formModal as unknown as UseFormModalReturn<unknown>,
)
const dataTableRowKey = computed(
  () =>
    effectiveRowKey.value as unknown as (
      row: Record<string, unknown>,
    ) => string | number,
)
function castSelectedIds(ids: unknown[]): TId[] {
  return ids as TId[]
}
function castFormData(data: unknown): Partial<T> | null {
  return data as Partial<T> | null
}

// NDataTable's `data` prop is typed as RowData[] where RowData is a loose
// index signature; cast once here to keep the internal T contract while
// satisfying naive-ui's type surface.
const dataTableData = computed(
  () => props.state.items.value as unknown as Record<string, unknown>[],
)

const dataTableColumns = computed(() => {
  const base = props.state.columnSettings.visibleColumns.value.map((c) => ({
    key: c.key,
    title: c.title,
    width: c.width,
    fixed: c.fixed,
  }))
  if (slots.rowActions) {
    base.push({
      key: '__row_actions__',
      title: t('admin.crud.actions'),
      width: 140,
      fixed: 'right',
      // NDataTable's render receives the row typed as its generic RowData
      // (loose index signature); we know the component is instantiated with T.
      render: (row: unknown) => h('div', slots.rowActions?.({ row: row as T })),
    } as (typeof base)[number] & { render: (row: unknown) => unknown })
  }
  return base
})

const modalTitle = computed(() => {
  const mode = props.state.formModal.mode.value ?? 'view'
  return t(`admin.crud.${mode}Title`)
})

function onSearchInput(value: string): void {
  props.state.setSearch(value)
}

function onRefresh(): void {
  void props.state.refresh()
}

async function onExport(): Promise<void> {
  const blob = await props.state.exportAll()
  if (blob && typeof URL !== 'undefined' && typeof document !== 'undefined') {
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${props.title ?? 'export'}.csv`
    document.body.appendChild(anchor)
    anchor.click()
    document.body.removeChild(anchor)
    URL.revokeObjectURL(url)
  }
}

function onImport(): void {
  if (typeof document === 'undefined') return
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.csv,.xlsx,.json'
  input.onchange = (e: Event) => {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (file) void props.state.importFile(file)
  }
  input.click()
}
</script>

<style scoped>
.t-crud-page {
  display: flex;
  flex-direction: column;
  gap: var(--tnzi-spacing-md, 12px);
  padding: var(--tnzi-spacing-md, 12px);
}

.t-crud-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.t-crud-page__title {
  margin: 0;
  font-size: var(--tnzi-font-size-lg, 18px);
  font-weight: 600;
  color: var(--tnzi-color-text, inherit);
}

.t-crud-page__search {
  max-width: 320px;
}
</style>
