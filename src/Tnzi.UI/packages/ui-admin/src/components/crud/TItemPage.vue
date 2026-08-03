<template>
  <TListShell
    :state="props.state"
    :title="title"
    :mode="mode"
    :show-header="showHeader"
    :show-search="showSearch"
    :show-default-search="showDefaultSearch"
    :search-fields="searchFields"
    :search-placeholder="searchPlaceholder"
    :default-advanced-mode="defaultAdvancedMode"
    :hide-simple-mode="hideSimpleMode"
    :show-create="showCreate"
    :show-export="showExport"
    :show-import="showImport"
    :show-refresh="showRefresh"
    :show-batch="showBatch"
    :show-pagination="showPagination"
    :form-modal-width="formModalWidth"
    :detail-width="detailWidth"
    :detail-title="detailTitle"
    :title-help="titleHelp"
    :title-help-title="titleHelpTitle"
    :translate="translate"
  >
    <template #renderer>
      <TItemRenderer
        :state="props.state"
        :item-key="itemKey"
        :show-selection="showBatch"
        :row-actions="rowActions"
        :translate="translate"
      >
        <template #item="ctx">
          <slot name="item" v-bind="ctx" />
        </template>
        <template v-if="$slots.empty" #empty>
          <slot name="empty" />
        </template>
      </TItemRenderer>
    </template>

    <template v-if="$slots.header" #header><slot name="header" /></template>
    <template v-if="$slots.kpis" #kpis><slot name="kpis" /></template>
    <template v-if="$slots.search" #search><slot name="search" /></template>
    <template v-if="$slots.primary" #primary><slot name="primary" /></template>
    <template v-if="$slots.toolbar" #toolbar><slot name="toolbar" /></template>
    <template v-if="$slots.toolbarLeft" #toolbarLeft><slot name="toolbarLeft" /></template>
    <template v-if="$slots.toolbarRight" #toolbarRight><slot name="toolbarRight" /></template>
    <template v-if="$slots.batchActions" #batchActions="{ selectedIds }">
      <slot name="batchActions" :selected-ids="selectedIds" />
    </template>
    <template v-if="$slots.error" #error="e"><slot name="error" v-bind="e" /></template>
    <template #form="f"><slot name="form" v-bind="f" /></template>
    <template v-if="$slots.formFooter" #formFooter><slot name="formFooter" /></template>
    <template v-if="$slots.detail" #detail="d"><slot name="detail" v-bind="d" /></template>
    <template v-if="$slots.detailFooter" #detailFooter="d"><slot name="detailFooter" v-bind="d" /></template>
  </TListShell>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
/**
 * TItemPage - `TListShell` + `TItemRenderer`: a searchable, paged CRUD list
 * whose rows are full-width document cards instead of table rows.
 *
 * The third member of the list family:
 *   - `TCrudPage` → table    (dense, column-comparable records: ledgers, logs)
 *   - `TCardPage` → tile grid (visual/short records: agents, templates, files)
 *   - `TItemPage` → row cards (document records: invoices, payments, messages)
 *
 * Everything else - search, toolbar, batch, pagination, the create/edit modal
 * and the view drawer - is the same shell, so switching a page between the
 * three is a one-line change with no loss of behaviour.
 */
import TListShell from './TListShell.vue'
import TItemRenderer from './renderers/TItemRenderer.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { RowAction } from '../../headless/row-actions'
import type { FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '@tnzi/ui'

export interface TItemPageProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  title?: string
  /** Defaults to `'page'` (the row list is normally the whole page). */
  mode?: 'page' | 'container'
  itemKey?: (row: T) => string | number
  showHeader?: boolean
  showSearch?: boolean
  showDefaultSearch?: boolean
  searchFields?: FormSchemaItem[]
  searchPlaceholder?: string
  defaultAdvancedMode?: boolean
  hideSimpleMode?: boolean
  showCreate?: boolean
  showExport?: boolean
  showImport?: boolean
  showRefresh?: boolean
  /** Selection checkboxes + batch delete. The row card draws the checkbox
   *  inline (see {@link TItemCard}'s `selectable`). */
  showBatch?: boolean
  /**
   * Declarative row operations, exactly as on `TCrudPage`, so moving a page
   * between the table and row-card shapes keeps one `RowAction[]` declaration.
   *
   * Unlike the table (which owns an operation column), a row card chooses its
   * own placement, so the array arrives back through the `#item` slot scope as
   * `rowActions` for the card to render where it wants.
   */
  rowActions?: RowAction<T>[]
  showPagination?: boolean
  formModalWidth?: number
  detailWidth?: number | string
  detailTitle?: (data: T) => string
  titleHelp?: string
  titleHelpTitle?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TItemPageProps<T, TId>>(), {
  title: undefined,
  mode: 'page',
  itemKey: undefined,
  showHeader: true,
  showSearch: true,
  showDefaultSearch: true,
  searchFields: undefined,
  searchPlaceholder: undefined,
  defaultAdvancedMode: false,
  hideSimpleMode: false,
  showCreate: true,
  showExport: false,
  showImport: false,
  showRefresh: true,
  showBatch: false,
  rowActions: undefined,
  showPagination: true,
  formModalWidth: 620,
  detailWidth: 640,
  detailTitle: undefined,
  titleHelp: undefined,
  titleHelpTitle: undefined,
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  kpis?: () => unknown
  item?: (props: {
    item: T
    index: number
    selected: boolean
    selectable: boolean
    toggleSelect: () => void
    rowActions: RowAction<T>[] | undefined
  }) => unknown
  empty?: () => unknown
  search?: () => unknown
  primary?: () => unknown
  toolbar?: () => unknown
  toolbarLeft?: () => unknown
  toolbarRight?: () => unknown
  /** Extra operations over the current selection (batch post, batch export …). */
  batchActions?: (props: { selectedIds: TId[] }) => unknown
  error?: (props: { error: Error; retry: () => Promise<void>; dismiss: () => void }) => unknown
  form?: (props: { formData: Partial<T> | null; mode: FormModalMode | null }) => unknown
  formFooter?: () => unknown
  detail?: (props: { data: Partial<T> | null; mode: FormModalMode | null }) => unknown
  detailFooter?: (props: { data: Partial<T> | null }) => unknown
}>()
</script>
