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

    <!-- Search panel — two-mode design (simple keyword + collapsible
         advanced grid). The 200-line internal layout was extracted to
         `TCrudSearch.vue` in 0.2.72+ (B5) so the simple/advanced surface
         can be reused and tested in isolation. The `#search` slot still
         takes precedence to let pages drop a fully custom search UI. -->
    <NCard
      v-if="$slots.search"
      :bordered="false"
      size="small"
      class="t-crud-page__search-card t-crud-page__search"
    >
      <slot name="search" />
    </NCard>
    <TCrudSearch
      v-else-if="searchFields || showDefaultSearch"
      :state="props.state"
      :search-fields="searchFields"
      :search-placeholder="searchPlaceholder"
      :default-advanced-mode="defaultAdvancedMode"
      :hide-simple-mode="hideSimpleMode"
      :translate="translate"
    />

    <!-- Inline error banner — surfaces a transient `state.error` (set when
         all `retryFetch` attempts have been exhausted) so the user can
         click Retry without scrolling. The `#error` slot lets a page
         override the default NAlert (e.g. show a contextual recovery
         hint or a custom retry CTA). Closing the banner only dismisses
         it locally via `state.dismissError()`; the next call to
         `state.refresh()` clears `error` for real. -->
    <slot
      v-if="props.state.error.value"
      name="error"
      :error="props.state.error.value"
      :retry="() => props.state.refresh()"
      :dismiss="() => props.state.dismissError()"
    >
      <NAlert
        type="error"
        class="t-crud-page__error"
        :title="t('admin.crud.fetchError')"
        closable
        @close="props.state.dismissError"
      >
        <div class="t-crud-page__error-body">
          <span class="t-crud-page__error-msg">
            {{ props.state.error.value.message }}
          </span>
          <NButton size="small" tertiary @click="() => props.state.refresh()">
            {{ t('admin.crud.retry') }}
          </NButton>
        </div>
      </NAlert>
    </slot>

    <!-- List card — soybean's `用户列表 | + 新增 | 批量删除 | 刷新 | 列设置`. -->
    <NCard
      :bordered="false"
      size="small"
      class="t-crud-page__list-card"
      content-style="padding: 0 16px 16px;"
    >
      <template #header>
        <div class="t-crud-page__header-left">
          <span class="t-crud-page__list-title">{{ resolvedTitle }}</span>
          <!-- Optional help blurb — replaces top-of-page NAlert banners
               with a discreet (i) icon next to the title. NPopover stays
               closed until the user hovers/clicks, so power-user notes
               (e.g. "this is a diagnostic view") don't steal real
               estate from the data table. -->
          <NPopover v-if="resolvedTitleHelp" trigger="hover" placement="bottom-start">
            <template #trigger>
              <button
                type="button"
                class="t-crud-page__help-trigger"
                :title="resolvedTitleHelpTitle"
                :aria-label="resolvedTitleHelpTitle"
              >
                <TSvgIcon icon="mdi:information-outline" :size="16" />
              </button>
            </template>
            <div class="t-crud-page__help-content">
              <div v-if="resolvedTitleHelpTitle" class="t-crud-page__help-title">
                {{ resolvedTitleHelpTitle }}
              </div>
              <div class="t-crud-page__help-body">{{ resolvedTitleHelp }}</div>
            </div>
          </NPopover>
          <!-- `#toolbarLeft` lives beside the list title — typical use is
               a quick filter input (e.g. dictionary/parameter group
               prefix) that belongs visually with "which slice of the
               data am I looking at?". Action buttons stay on the right
               in `#header-extra` via `#toolbarRight`. -->
          <div v-if="$slots.toolbarLeft" class="t-crud-page__header-filters">
            <slot name="toolbarLeft" />
          </div>
        </div>
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
           Selection count is folded INTO the delete button label ("删除 (3)")
           so the action and its target read as one phrase. The delete button
           is wrapped in NPopconfirm so it always demands a "are you sure?"
           gesture before invoking the destructive bulk operation — matches
           the per-row delete confirm flow in TRowActions. -->
      <div class="t-crud-page__footer">
        <div class="t-crud-page__footer-left">
          <template v-if="!batchSelectionEmpty">
            <NPopconfirm
              v-if="showBatchDelete"
              @positive-click="onBatchDelete"
            >
              <template #trigger>
                <NButton
                  type="error"
                  size="small"
                  ghost
                >
                  <template #icon>
                    <TSvgIcon icon="mdi:trash-can-outline" :size="14" />
                  </template>
                  {{ t('admin.crud.batchDelete') }} ({{ props.state.batchActions.selectedCount.value }})
                </NButton>
              </template>
              {{ batchDeleteConfirmText }}
            </NPopconfirm>
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
      :width="formModalWidth"
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
import {
  NAlert,
  NButton,
  NCard,
  NDataTable,
  NPagination,
  NPopconfirm,
  NPopover,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudColumnSetting from './TCrudColumnSetting.vue'
import TBatchActions from './TBatchActions.vue'
import TFormModal from './TFormModal.vue'
import TCrudSearch from './TCrudSearch.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { UseBatchActionsReturn } from '../../headless/useBatchActions'
import type { UseFormModalReturn, FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'
import { useBreakpoint } from '../../headless/useBreakpoint'

// Exported so external wrappers / higher-order components can reference
// TCrudPage's Props shape without duplicating the type.
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
   * Pixel width of the form modal opened on Create/Edit/View. Default 560
   * fits a single-column form with up to ~5 fields without scrolling.
   * Bump to 760-800 for 2-column forms (6-10 fields) and to 960-1080 for
   * 3-column forms (11+ fields). The TFormModal auto-falls-back to
   * fullscreen on viewports narrower than `max(width + 32, 640)`.
   */
  formModalWidth?: number
  /**
   * Width (px) of the right-fixed row-actions column when `#rowActions`
   * slot is supplied. Default 150 fits the canonical strict-2-button
   * shape `[Edit] [More▾]` introduced in the 2026-05-18 row-actions
   * refactor (Edit ~52px + 8px gap + More ~62px + 24px cell padding ≈ 146px).
   * Pages that legitimately surface 3+ inline buttons (rare — prefer
   * folding into `moreOptions`) should bump this prop locally.
   */
  rowActionsWidth?: number
  /**
   * Help blurb shown when the user clicks/hovers the (i) icon next to
   * the list title. Use for diagnostic / audit / advanced pages that
   * benefit from a one-liner explaining what the table is and when to
   * use it. Replaces the older "banner NAlert at the top of the page"
   * pattern with something less visually noisy. Pass raw text or an
   * i18n key (auto-resolved via the page namespace).
   */
  titleHelp?: string
  /**
   * Title for the help popover (defaults to "Tip"). Pass an i18n key
   * (e.g. `'banner.title'`) or a literal string.
   */
  titleHelpTitle?: string
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
  rowActionsWidth: 150,
  formModalWidth: 560,
  titleHelp: undefined,
  titleHelpTitle: undefined,
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
  /**
   * Replace the default error banner shown after fetchData rejects. Bind
   * `error` (the raw Error), `retry` (re-runs the query), and `dismiss`
   * (clears `state.error` without re-fetching). Default: an NAlert with
   * the error message + a Retry button.
   */
  error?: (props: { error: Error; retry: () => Promise<void>; dismiss: () => void }) => unknown
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

// `titleHelp` accepts an i18n key (page-scoped or absolute) or raw text.
// We resolve through the same maybeTranslate heuristic the title uses so
// pages can pass `'banner.body'` and get a localised string back without
// re-implementing the lookup logic in every consumer.
const resolvedTitleHelp = computed<string>(() => {
  const v = props.titleHelp
  if (!v) return ''
  return maybeTranslate(v, v)
})
const resolvedTitleHelpTitle = computed<string>(() => {
  const v = props.titleHelpTitle
  if (!v) return t('admin.common.tip') || 'Tip'
  return maybeTranslate(v, v)
})

const showColumnSetting = ref(false)
const bp = useBreakpoint()

const effectiveRowKey = computed<(row: T) => TId>(
  () => props.rowKey ?? props.state.rowKey,
)

// --- Template-bound adapters.
//
// Vue templates can't host TS casts, so the few unavoidable variance
// bridges live here as named expressions bound by `:prop`. Two groups:
//   1. NDataTable / NCheckboxRowKeys take a `Record<string, unknown>` row
//      type — wider than our T. We cast once for data + rowKey and the
//      table internals walk the dataset via index signature.
//   2. TBatchActions / TFormModal are generic over `unknown` (they don't
//      carry T through their public surface) — the bridge stays.
//
// 0.2.72+ (B1): attempted to drop these via `T extends Record<string, unknown>`,
// but the constraint cascades 64 typecheck errors across 30+ pages whose
// DTOs are plain `interface`s (no implicit index signature). Eliminating
// those would require adding `& Record<string, unknown>` to every DTO in
// @tnzi/core/services — out of scope here. The casts are kept but
// consolidated below to a single `castRow` helper instead of repeated
// `as unknown as ...` chains so the intent reads cleanly.
function castRow<U>(value: unknown): U {
  return value as U
}
const batchActionsState = computed(() =>
  castRow<UseBatchActionsReturn<unknown>>(props.state.batchActions),
)
const formModalState = computed(() =>
  castRow<UseFormModalReturn<unknown>>(props.state.formModal),
)
const dataTableRowKey = computed(() =>
  castRow<(row: Record<string, unknown>) => string | number>(effectiveRowKey.value),
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
const dataTableData = computed(() => castRow<Record<string, unknown>[]>(props.state.items.value))

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
      ...(c.ellipsis !== undefined ? { ellipsis: c.ellipsis } : {}),
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

// Simple + advanced search logic was relocated to `TCrudSearch.vue` in
// 0.2.72+ (B5). TCrudPage now mounts TCrudSearch as a child when the
// `#search` slot isn't provided, see template above.

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

/** Popconfirm body — uses `confirmBatchDelete` with the `{n}` placeholder
 *  replaced by the current selection count. Falls back to the per-row
 *  `confirmDelete` text if the consumer hasn't supplied the new key
 *  (defensive — every shipped locale has both keys). */
const batchDeleteConfirmText = computed(() => {
  const count = props.state.batchActions.selectedCount.value
  const tpl = t('admin.crud.confirmBatchDelete')
  if (tpl && tpl !== 'admin.crud.confirmBatchDelete') {
    return tpl.replace('{n}', String(count))
  }
  return t('admin.crud.confirmDelete')
})

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
/* Error banner sits between the search card and the list card. Doesn't
   shrink so the message stays visible even when the table fills up. */
.t-crud-page__error {
  flex-shrink: 0;
}
.t-crud-page__error-body {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.t-crud-page__error-msg {
  word-break: break-word;
  flex: 1 1 auto;
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
/* 0.2.72+ (B5): search-simple / search-advanced / search-toggle /
   search-actions styles moved to TCrudSearch.vue when the markup
   was extracted. Vue `<style scoped>` doesn't reach child SFCs, so
   the rules had to follow the elements. `.t-crud-page__search-card`
   below is still here because a consumer-supplied `#search` slot
   renders into TCrudPage's own NCard wrapper. */
.t-crud-page__list-title {
  font-size: 16px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-crud-page__header-left {
  display: flex;
  align-items: center;
  gap: 16px;
  min-width: 0;
  flex: 1;
}
.t-crud-page__header-filters {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-crud-page__help-trigger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  border: none;
  background: transparent;
  color: var(--tnzi-base-text-muted, #888);
  cursor: pointer;
  padding: 0;
  transition: color 0.15s ease, background 0.15s ease;
}
.t-crud-page__help-trigger:hover {
  color: var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08);
}
.t-crud-page__help-content {
  max-width: 320px;
}
.t-crud-page__help-title {
  font-weight: 600;
  margin-bottom: 6px;
  color: var(--tnzi-base-text);
}
.t-crud-page__help-body {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted, #888);
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
