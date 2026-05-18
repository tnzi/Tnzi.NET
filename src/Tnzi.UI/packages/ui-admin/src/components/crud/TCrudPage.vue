<template>
  <!-- soybean parity layout: vertical flex column whose final child (the
       list card) claims `flex: 1` so its NDataTable can use `flex-height`
       to size the table area and keep pagination pinned at the bottom.
       Replaces the old NSpace wrapper (NSpace doesn't stretch children
       to fill remaining height). -->
  <div class="t-crud-page">
    <!-- Optional consumer-supplied page header (rare — page title usually
         lives inside the list card sub-title below). -->
    <slot v-if="$slots.header" name="header" />

    <!-- Search panel — two-mode design:
         (1) Simple (default): one long keyword input + Search/Reset on the
             same row. Hits `state.setSearch(text)` which routes to bridge's
             keyword param (consumer responsibility to wire backend support).
         (2) Advanced: multi-field form built from `searchFields` schema.
             Each field is a typed input; clicking Search commits all fields
             into `state.setFilters({...})` (precise filters, NOT keyword).
         An "Advanced" toggle button on the simple row jumps to mode (2),
         and a "Simple" link on the advanced form returns to mode (1).
         If `searchFields` isn't configured the toggle button hides and the
         simple row is the only search surface. -->
    <NCard
      v-if="$slots.search || searchFields || showDefaultSearch"
      :bordered="false"
      size="small"
      class="t-crud-page__search-card t-crud-page__search"
    >
      <slot name="search">
        <!-- ── Simple row — ALWAYS visible ──
             Keyword input + Search + (toggle) "Advanced ▾". Clicking the
             toggle expands an advanced form panel below this row;
             clicking again collapses it (chevron flips ▾↔▴). The simple
             row itself never disappears, so users always have keyword
             search at hand without losing their advanced filter state. -->
        <div class="t-crud-page__search-simple">
          <NInput
            :value="simpleQuery"
            :placeholder="effectiveSearchPlaceholder"
            clearable
            class="t-crud-page__search-simple-input"
            @update:value="(v: string) => (simpleQuery = v)"
            @keydown.enter="onApplySimpleSearch"
            @clear="onClearSimpleSearch"
          >
            <template #prefix>
              <TSvgIcon icon="mdi:magnify" :size="16" />
            </template>
          </NInput>
          <NButton type="primary" @click="onApplySimpleSearch">
            {{ t('admin.crud.search') }}
          </NButton>
          <NButton
            v-if="hasAdvancedSearch"
            text
            class="t-crud-page__search-toggle"
            @click="advancedMode = !advancedMode"
          >
            {{ t('admin.crud.advancedSearch') }}
            <template #icon>
              <TSvgIcon
                :icon="advancedMode ? 'mdi:chevron-up' : 'mdi:chevron-down'"
                :size="16"
              />
            </template>
          </NButton>
        </div>

        <!-- ── Advanced panel — collapsible ──
             Hidden by default, expands below the simple row when user
             clicks the "Advanced ▾" toggle. Compact grid (no labels,
             placeholder carries the field name) keeps it visually
             secondary to the always-on keyword search. -->
        <NCollapseTransition v-if="hasAdvancedSearch" :show="advancedMode">
          <NForm
            class="t-crud-page__search-advanced"
            label-placement="left"
            :show-feedback="false"
            :show-require-mark="false"
          >
            <NGrid responsive="screen" item-responsive :x-gap="12" :y-gap="12">
              <NGi
                v-for="field in searchFields"
                :key="field.key"
                span="24 s:12 m:6"
              >
                <NFormItem :show-label="false" :path="field.key">
                  <component :is="renderSearchField(field)" />
                </NFormItem>
              </NGi>
              <NGi
                :span="searchButtonSpan"
                class="t-crud-page__search-actions-cell"
              >
                <NSpace justify="end" class="t-crud-page__search-actions">
                  <NButton type="primary" @click="onApplySearch">
                    {{ t('admin.crud.search') }}
                  </NButton>
                </NSpace>
              </NGi>
            </NGrid>
          </NForm>
        </NCollapseTransition>
      </slot>
    </NCard>

    <!-- List card — soybean's `用户列表 | + 新增 | 批量删除 | 刷新 | 列设置`. -->
    <NCard
      :bordered="false"
      size="small"
      class="t-crud-page__list-card"
      content-style="padding: 0 16px 16px;"
    >
      <template #header>
        <span class="t-crud-page__list-title">{{ resolvedTitle }}</span>
      </template>
      <template #header-extra>
        <div class="t-crud-page__actions">
          <slot name="primary">
            <NButton
              v-if="showCreate"
              type="primary"
              tertiary
              size="small"
              class="t-crud-page__action t-crud-page__create"
              @click="props.state.openCreate"
            >
              <template #icon>
                <TSvgIcon icon="mdi:plus" :size="16" />
              </template>
              {{ t('admin.crud.create') }}
            </NButton>
          </slot>
          <!-- Batch delete moved to the table footer (next to pagination)
               so it surfaces close to the actual row selection.
               See `.t-crud-page__footer` below. -->
          <slot name="toolbarLeft" />
          <NButton
            tertiary
            size="small"
            class="t-crud-page__action t-crud-toolbar__refresh"
            @click="onRefresh"
          >
            <template #icon>
              <TSvgIcon icon="mdi:refresh" :size="16" />
            </template>
            {{ t('admin.crud.refresh') }}
          </NButton>
          <NButton
            v-if="showExport"
            tertiary
            size="small"
            class="t-crud-page__action"
            @click="onExport"
          >
            <template #icon>
              <TSvgIcon icon="mdi:download-outline" :size="16" />
            </template>
            {{ t('admin.crud.export') }}
          </NButton>
          <NButton
            v-if="showImport"
            tertiary
            size="small"
            class="t-crud-page__action"
            @click="onImport"
          >
            <template #icon>
              <TSvgIcon icon="mdi:upload-outline" :size="16" />
            </template>
            {{ t('admin.crud.import') }}
          </NButton>
          <TCrudColumnSetting
            :settings="props.state.columnSettings"
            :all-columns="allColumns"
            :show="showColumnSetting"
            :translate="props.translate"
            @update:show="(v: boolean) => (showColumnSetting = v)"
          >
            <template #trigger>
              <!-- TCrudColumnSetting uses `trigger="click"` so NPopover
                   manages open/close (including outside-click dismissal)
                   itself — no manual @click toggle needed here. -->
              <NButton
                tertiary
                size="small"
                class="t-crud-page__action"
              >
                <template #icon>
                  <TSvgIcon icon="mdi:cog-outline" :size="16" />
                </template>
                {{ t('admin.crud.columns') }}
              </NButton>
            </template>
          </TCrudColumnSetting>
          <slot name="toolbarRight" />
        </div>
      </template>

      <TBatchActions
        v-if="batchActionSlotProvided"
        :state="batchActionsState"
        :translate="props.translate"
      >
        <template #default="{ selectedIds }">
          <slot name="batchActions" :selectedIds="castSelectedIds(selectedIds)" />
        </template>
      </TBatchActions>

      <NDataTable
        class="t-crud-page__table"
        :data="dataTableData"
        :columns="dataTableColumns"
        :loading="props.state.loading.value"
        :row-key="dataTableRowKey"
        :pagination="false"
        :checked-row-keys="checkedRowKeys"
        :flex-height="true"
        remote
        @update:checked-row-keys="onUpdateCheckedRowKeys"
      />
      <!-- Footer row: batch action group on the left, pagination on the right.
           Selection count is folded INTO the delete button label ("批量删除 (3)")
           so the action and its target read as one phrase. Clearing the
           selection is a proper sibling button rather than a text link —
           same visual weight family as delete, just neutral (default type)
           so the destructive action stays visually dominant. -->
      <div class="t-crud-page__footer">
        <div class="t-crud-page__footer-left">
          <template v-if="!batchSelectionEmpty">
            <NButton
              v-if="showBatchDelete"
              type="error"
              size="small"
              ghost
              @click="onBatchDelete"
            >
              <template #icon>
                <TSvgIcon icon="mdi:trash-can-outline" :size="14" />
              </template>
              {{ t('admin.crud.batchDelete') }} ({{ props.state.batchActions.selectedCount.value }})
            </NButton>
            <NButton
              size="small"
              @click="props.state.batchActions.clear"
            >
              <template #icon>
                <TSvgIcon icon="mdi:close-circle-outline" :size="14" />
              </template>
              {{ t('admin.crud.unselectAll') }}
            </NButton>
          </template>
        </div>
        <NPagination
          v-bind="paginationConfig"
          @update:page="props.state.setPage"
          @update:page-size="props.state.setPageSize"
        />
      </div>
    </NCard>

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
import { computed, h, reactive, ref, useSlots, watch } from 'vue'
import {
  NButton,
  NCard,
  NCollapseTransition,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NGi,
  NGrid,
  NInput,
  NInputNumber,
  NPagination,
  NSelect,
  NSpace,
  NSwitch,
} from 'naive-ui'
import TSvgIcon from '../display/TSvgIcon.vue'
import TCrudColumnSetting from './TCrudColumnSetting.vue'
import TBatchActions from './TBatchActions.vue'
import TFormModal from './TFormModal.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { UseBatchActionsReturn } from '../../headless/useBatchActions'
import type { UseFormModalReturn, FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'
import { useBreakpoint } from '../../headless/useBreakpoint'

// Exported so external wrappers / higher-order components can reference
// TCrudPage's Props shape without duplicating the type.
// eslint-disable-next-line vue/prefer-define-options
export interface TCrudPageProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  allColumns: ColumnDef[]
  title?: string
  rowKey?: (row: T) => TId
  showCreate?: boolean
  showBatchDelete?: boolean
  /** Show the "Export" toolbar button. Default `false` (most pages don't expose export). */
  showExport?: boolean
  /** Show the "Import" toolbar button. Default `false`. */
  showImport?: boolean
  /**
   * When `true` the default search NInput renders inside the collapsible
   * search panel even when no `#search` slot is supplied. Default: `true` —
   * matches soybean's default page shape.
   */
  showDefaultSearch?: boolean
  /**
   * Show a dedicated `#` serial column. Default `false` — the row's
   * cross-page serial number is now surfaced as a `title` hover tooltip
   * on the selection checkbox, which avoids spending horizontal real
   * estate on data most users only need as a glance. Set to `true` to
   * restore the column.
   */
  showSerial?: boolean
  /**
   * Advanced-search field schema. When supplied AND `hideSimpleMode` is
   * false, an "Advanced" toggle button surfaces on the simple-search
   * row; clicking it swaps in a multi-field NGrid form whose values
   * commit into `state.setFilters({...})` on Search click.
   * Each field renders a typed input (text / select / date / number /
   * switch / textarea) per `field.type`.
   */
  searchFields?: FormSchemaItem[]
  /**
   * Placeholder for the simple-mode keyword box. Pass a module-specific
   * hint like `"搜索用户名 / 邮箱..."` to make the surface intent obvious.
   * Default: i18n `admin.crud.searchPlaceholder` ("Search keyword...").
   */
  searchPlaceholder?: string
  /**
   * When `true`, the search panel opens in Advanced mode initially.
   * Use this for pages where exact filters are the primary lookup
   * (audit logs, payment orders) and keyword search is secondary.
   * Only meaningful when `searchFields` is supplied. Default `false`.
   */
  defaultAdvancedMode?: boolean
  /**
   * When `true`, the simple keyword row is never rendered — only the
   * advanced form (requires `searchFields`). Use for pages where free-text
   * search isn't supported by the backend. Default `false`.
   */
  hideSimpleMode?: boolean
  /**
   * Width (px) of the right-fixed row-actions column when `#rowActions`
   * slot is supplied. Default 140 covers the canonical Edit + Delete pair;
   * pages that surface more buttons (status toggles, custom batch actions)
   * should bump this to avoid the buttons clipping under the column edge.
   */
  rowActionsWidth?: number
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TCrudPageProps<T, TId>>(), {
  title: undefined,
  rowKey: undefined,
  showCreate: true,
  showBatchDelete: true,
  showExport: false,
  showImport: false,
  showDefaultSearch: true,
  showSerial: false,
  searchFields: undefined,
  searchPlaceholder: undefined,
  defaultAdvancedMode: false,
  hideSimpleMode: false,
  rowActionsWidth: 140,
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

/**
 * Translate a value that may be either an i18n key (`columns.username`,
 * `admin.crud.list`) or a hard-coded label ("Username"). Heuristic: if the
 * string looks like a dotted lower-camel i18n key we run it through
 * `translate`; otherwise we return the label as-is so legacy page-configs
 * with English literals keep working.
 */
function maybeTranslate(value: string | undefined, fallback: string): string {
  if (!value) return fallback
  // Heuristic: an i18n key is all-ASCII, starts with a lowercase letter, and
  // contains no spaces. Plain labels like "Username" or "User Management"
  // start with an uppercase letter or contain a space and stay as-is.
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) {
    return t(value)
  }
  return value
}

const resolvedTitle = computed(() => maybeTranslate(props.title, t('admin.crud.list')))

const showColumnSetting = ref(false)
const bp = useBreakpoint()

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
  // NDataTable's column type is broad — we let TS infer per-entry and cast
  // the final array, mirroring the prior implementation.
  const base: Record<string, unknown>[] = []
  // Selection column — drives `v-model:checked-row-keys` below. Gated on
  // `showBatchDelete` so pages without batch ops don't display a useless
  // checkbox column. `cellProps` injects a native `title` attribute on
  // each row's <td>, giving the user the row's serial number as a hover
  // tooltip — replaces the dedicated `#` column (which took up real
  // horizontal real estate for information that's only useful as a
  // peek). Serial accumulates across pages: row 1 on page 2 of pageSize=20
  // shows "#21".
  if (props.showBatchDelete) {
    base.push({
      type: 'selection',
      fixed: 'left',
      width: 40,
      cellProps: (_row: unknown, rowIndex: number) => {
        const q = props.state.query.value
        const serial = (q.pageIndex - 1) * q.pageSize + rowIndex + 1
        return { title: `#${serial}` }
      },
    })
  }
  for (const c of props.state.columnSettings.visibleColumns.value) {
    base.push({
      key: c.key,
      title: maybeTranslate(c.title, c.title),
      width: c.width,
      fixed: c.fixed,
      ...(c.render
        ? {
            render: (row: unknown) => c.render!(row as Record<string, unknown>),
          }
        : {}),
    })
  }
  if (slots.rowActions) {
    base.push({
      key: '__row_actions__',
      title: t('admin.crud.actions'),
      width: props.rowActionsWidth,
      align: 'center',
      fixed: 'right',
      // NDataTable's render receives the row typed as its generic RowData
      // (loose index signature); we know the component is instantiated with T.
      render: (row: unknown) => h('div', slots.rowActions?.({ row: row as T })),
    })
  }
  return base as never[]
})

/** Bridge between Naive UI's `v-model:checked-row-keys` and our
 *  `useBatchActions` store. Reading: pull selectedIds from the store.
 *  Writing: replace the store's selection set with whatever NDataTable
 *  emits (it always emits the full keys array, not a delta). */
const checkedRowKeys = computed<(string | number)[]>(
  () => props.state.batchActions.selectedIds.value as (string | number)[],
)
function onUpdateCheckedRowKeys(keys: (string | number)[]): void {
  props.state.batchActions.selectAll(keys as TId[])
}

const modalTitle = computed(() => {
  const mode = props.state.formModal.mode.value ?? 'view'
  return t(`admin.crud.${mode}Title`)
})

// ── Simple search (default mode) ───────────────────────────────────
// Keyword box bound locally — only commits on Enter / Search click, so
// users can type without firing every-keystroke fetches. Routes to
// `state.setSearch(text)` which the bridge converts into the backend's
// keyword param (consumer responsibility).
const simpleQuery = ref<string>(props.state.query.value.searchText ?? '')
function onApplySimpleSearch(): void {
  props.state.setSearch(simpleQuery.value)
  void props.state.refresh()
}
function onResetSimpleSearch(): void {
  simpleQuery.value = ''
  props.state.setSearch('')
  void props.state.refresh()
}
function onClearSimpleSearch(): void {
  // NInput's built-in clear button calls this; mirror the explicit Reset
  // so clearing the box actually refreshes the table to "no keyword".
  onResetSimpleSearch()
}

// ── Mode toggle ────────────────────────────────────────────────────
// `false` = simple keyword search row (default).
// `true`  = multi-field advanced form (only available when `searchFields`
//           is supplied — toggle button is hidden otherwise).
// Initial: respect `defaultAdvancedMode` consumer prop; also force-on
// when `hideSimpleMode` is set so the panel isn't blank.
const advancedMode = ref(
  props.hideSimpleMode || (props.defaultAdvancedMode && !!props.searchFields?.length),
)

/** Whether the Advanced toggle button is reachable (needs both a schema
 *  and a non-locked simple mode). Drives the toggle button's v-if. */
const hasAdvancedSearch = computed(
  () => !props.hideSimpleMode && !!props.searchFields?.length,
)

/** Placeholder for the simple keyword box. Consumer-supplied wins;
 *  otherwise default i18n string. */
const effectiveSearchPlaceholder = computed(
  () => props.searchPlaceholder ?? t('admin.crud.searchPlaceholder'),
)

// ── Multi-field search (advanced mode) ─────────────────────────────
// `searchModel` is the live form state for `searchFields`. It mirrors
// the keys of the supplied schema and lives outside `state.query.filters`
// so the user can type without triggering refetches — filters commit on
// Search click only (matches soybean's UX).
const searchModel = reactive<Record<string, unknown>>({})

/**
 * Span string for the button row. Fields are span="24 s:12 m:6" so the
 * grid is 1/2/4 columns wide at xs/s/m+ breakpoints. We want the
 * buttons to share the last row with whatever field cells fit; if the
 * last row is exactly full, the button row starts a fresh row taking
 * up just the right-hand half (mirrors soybean's 6-field UX where
 * "24 m:12" lands the buttons on row3-right).
 *
 * Formula: spanForCols(cellsPerRow) = remainder ? remainder * (24 / cellsPerRow) : (24 / cellsPerRow * (cellsPerRow / 2))
 */
const searchButtonSpan = computed<string>(() => {
  const count = props.searchFields?.length ?? 0
  if (count === 0) return '24'
  // s: 2 cells / row, m+: 4 cells / row
  const sUsed = count % 2
  const mUsed = count % 4
  const sSpan = sUsed === 0 ? 12 : (2 - sUsed) * 12
  const mSpan = mUsed === 0 ? 12 : (4 - mUsed) * 6
  return `24 s:${sSpan} m:${mSpan}`
})
watch(
  () => props.searchFields,
  (fields) => {
    if (!fields) return
    for (const key of Object.keys(searchModel)) {
      if (!fields.find((f) => f.key === key)) delete searchModel[key]
    }
    for (const f of fields) {
      if (!(f.key in searchModel)) searchModel[f.key] = null
    }
  },
  { immediate: true, deep: false },
)

function renderSearchField(item: FormSchemaItem): unknown {
  const value = searchModel[item.key]
  const onUpdate = (v: unknown) => {
    searchModel[item.key] = v
  }
  const effectiveType = item.typeFn ? item.typeFn(searchModel) : item.type
  const placeholder = maybeTranslate(item.placeholder ?? item.label, item.label)
  switch (effectiveType) {
    case 'text':
      return h(NInput, {
        value: (value as string | null) ?? null,
        placeholder,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'textarea':
      return h(NInput, {
        value: (value as string | null) ?? null,
        type: 'textarea',
        placeholder,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'number':
      return h(NInputNumber, {
        value: (value as number | null) ?? null,
        min: item.min,
        max: item.max,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'switch':
      return h(NSwitch, { value: value as boolean, 'onUpdate:value': onUpdate })
    case 'select':
      return h(NSelect, {
        value: (value as string | number | null) ?? null,
        options: item.options ?? [],
        clearable: true,
        placeholder,
        'onUpdate:value': onUpdate,
      })
    case 'date':
      return h(NDatePicker, { value: value as number | null, clearable: true, type: 'date', 'onUpdate:value': onUpdate })
    default:
      return null
  }
}

function onApplySearch(): void {
  const filters: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(searchModel)) {
    if (v !== null && v !== undefined && v !== '') filters[k] = v
  }
  props.state.setFilters(filters)
  void props.state.refresh()
}

function onResetSearch(): void {
  for (const k of Object.keys(searchModel)) searchModel[k] = null
  props.state.setFilters({})
  void props.state.refresh()
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

const batchSelectionEmpty = computed(
  () => props.state.batchActions.selectedCount.value === 0,
)
const batchActionSlotProvided = computed(() => !!slots.batchActions)

function onBatchDelete(): void {
  const ids = props.state.batchActions.selectedIds.value
  if (ids.length === 0) return
  void props.state.handleDelete(ids as TId[])
}

/** Naive-UI NDataTable built-in pagination — matches soybean's
 *  "共 N 条 / 1 / 2 / ... / 10/页" footer layout.
 *  Mobile (<sm): switch to `simple` mode, hide the size picker and
 *  drop the "Total N" prefix so the pagination fits inside the footer
 *  without wrapping past the screen edge. */
const paginationConfig = computed(() => {
  const compact = bp.isSm.value
  const base = {
    page: props.state.query.value.pageIndex,
    pageSize: props.state.query.value.pageSize,
    itemCount: props.state.total.value,
  }
  if (compact) {
    return {
      ...base,
      simple: true,
      showSizePicker: false,
    }
  }
  return {
    ...base,
    showSizePicker: true,
    pageSizes: [10, 20, 50, 100],
    prefix: ({ itemCount }: { itemCount: number | undefined }) =>
      `${t('admin.crud.total')} ${itemCount ?? 0}`,
  }
})
</script>

<style scoped>
/* Vertical flex column anchored to TAdminContent's `height: 100% + flex
   column` page wrapper. Gap matches soybean (`gap-16px`). overflow:hidden
   so the search collapse / list card don't blow through TAdminContent's
   boundary. min-height: 0 lets the list-card actually claim flex:1. */
.t-crud-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}
/* soybean parity — bordered=false Cards still carry a soft box-shadow
   and 8px border-radius (NCard default theme override target). */
.t-crud-page__search-card,
.t-crud-page__list-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
/* Search card never shrinks — keeps its natural height even when the
   list card competes for vertical space. */
.t-crud-page__search-card {
  flex-shrink: 0;
}
/* List card claims all remaining vertical space + traps overflow so
   the NDataTable inside can use `flex-height` to size its scroll area
   correctly. `n-card__content` is forced into a flex column so the
   table can `flex: 1` against the toolbar / batch-actions strips. */
.t-crud-page__list-card {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.t-crud-page__list-card :deep(.n-card-content) {
  flex: 1 1 auto;
  min-height: 0;
  padding-top: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
/* NDataTable claims the residual height of n-card-content. flex-height
   needs the parent to be a flex item with a definite size — this is it. */
.t-crud-page__table {
  flex: 1 1 auto;
  min-height: 0;
}
/* Bottom toolbar — selection summary + batch actions on the left,
   pagination on the right. Sits below the NDataTable inside the list
   card's content slot. flex-shrink:0 so it always stays visible
   regardless of how much vertical space the table claims. */
.t-crud-page__footer {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 4px 4px;
}
.t-crud-page__footer-left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 28px;
  min-width: 0;
}

/* Mobile: pagination stacks below batch actions so the row never
   collides with the screen edge. The list-card actions toolbar also
   becomes a horizontal scroll strip so 5-7 buttons don't wrap and
   double the card header height. */
@media (max-width: 640px) {
  .t-crud-page__footer {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }
  .t-crud-page__footer-left {
    justify-content: center;
    flex-wrap: wrap;
  }
  .t-crud-page__list-card :deep(.n-card-header) {
    padding: 12px 12px 8px;
  }
  .t-crud-page__list-card :deep(.n-card-header__main) {
    /* Title can break a long phrase so it doesn't push actions to the
       next row when both compete for header space. */
    min-width: 0;
  }
  .t-crud-page__actions {
    flex-wrap: nowrap;
    overflow-x: auto;
    -webkit-overflow-scrolling: touch;
    scrollbar-width: none;
  }
  .t-crud-page__actions::-webkit-scrollbar {
    display: none;
  }
}
/* Simple-mode row — horizontally centered cluster:
     [🔍 keyword input (clearable)]  [Search]  [Advanced (link)]
   Reset is intentionally omitted — NInput's built-in × clears and
   refreshes (see `@clear="onClearSimpleSearch"`). */
.t-crud-page__search-simple {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  flex-wrap: wrap;
}
.t-crud-page__search-simple-input {
  flex: 0 1 420px;
  min-width: 240px;
}

/* Mobile: search input gives up its 240px minimum and stretches to fill
   the card so the Search + Advanced buttons drop to a second row without
   the input clipping the card edge. The buttons themselves stay inline. */
@media (max-width: 640px) {
  .t-crud-page__search-simple {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }
  .t-crud-page__search-simple-input {
    flex: 1 1 100%;
    min-width: 0;
    width: 100%;
  }
}
.t-crud-page__search-toggle :deep(.n-button__content) {
  color: var(--tnzi-base-text-muted, #6b7280);
}
.t-crud-page__search-toggle:hover :deep(.n-button__content) {
  color: var(--tnzi-primary, #06b6d4);
}
.t-crud-page__search-actions-cell {
  /* Modest right indent so Search button doesn't kiss the card edge. */
  padding-right: 12px;
}
.t-crud-page__search-actions {
  width: 100%;
}
/* Advanced form panel — sits below the simple row, NCollapseTransition
   handles the height animation. Top margin separates it visually from
   the always-visible simple row above. */
.t-crud-page__search-advanced {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px dashed var(--tnzi-border, #e5e7eb);
}
.t-crud-page__list-title {
  font-size: 16px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-crud-page__actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.t-crud-page__action {
  font-weight: 400;
}
/* soybean parity: table header is 39px tall (size="small" default is
   too tight after the NCard size=small change, while medium was too
   loose — manually pin the padding so it lands at 39 either way). */
.t-crud-page__list-card :deep(.n-data-table-th) {
  padding: 8px 12px;
}
.t-crud-page__list-card :deep(.n-data-table-td) {
  padding: 9px 12px;
}
</style>
