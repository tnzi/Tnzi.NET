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
      <TCardRenderer
        :state="props.state"
        :cols="cols"
        :gap="gap"
        :card-key="cardKey"
        :show-selection="showBatch"
        :row-actions="rowActions"
        :translate="translate"
      >
        <template #card="ctx">
          <slot name="card" v-bind="ctx" />
        </template>
        <template v-if="$slots.empty" #empty>
          <slot name="empty" />
        </template>
      </TCardRenderer>
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
import TListShell from './TListShell.vue'
import TCardRenderer from './renderers/TCardRenderer.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { RowAction } from '../../headless/row-actions'
import type { FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '@tnzi/ui'

export interface TCardPageProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  title?: string
  /**
   * Layout mode. Defaults to `'container'` (content-sized) rather than
   * TListShell's `'page'` default, because card grids are typically embedded
   * inside a scrollable page rather than being the sole page content.
   */
  mode?: 'page' | 'container'
  cols?: number | { xs?: number; sm?: number; md?: number; lg?: number; xl?: number }
  gap?: number
  cardKey?: (row: T) => string | number
  /** When false the shell's white header card is not rendered - for card
      grids nested under an outer header (e.g. inside TContentPage tabs)
      where the shell's TPageHeader would duplicate the title bar. */
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
  /**
   * Show selection checkboxes + batch-delete. Defaults to `false` (unlike
   * TListShell's `true`) because card lists are display-first; opt in when
   * the card list needs multi-select.
   */
  showBatch?: boolean
  /**
   * Declarative row operations, exactly as on `TCrudPage`, so moving a page
   * between the table and tile shapes keeps one `RowAction[]` declaration.
   * A tile picks its own placement, so the array comes back through the
   * `#card` slot scope as `rowActions`.
   */
  rowActions?: RowAction<T>[]
  showPagination?: boolean
  formModalWidth?: number
  /** Width of the read-only view drawer (the `#detail` slot). Default 640.
      Accepts a string (e.g. `'100vw'`) for responsive full-screen on phones. */
  detailWidth?: number | string
  /** Title for the view drawer, derived from the viewed record. */
  detailTitle?: (data: T) => string
  titleHelp?: string
  titleHelpTitle?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TCardPageProps<T, TId>>(), {
  title: undefined,
  mode: 'container',
  cols: () => ({ xs: 1, sm: 2, md: 3, lg: 4 }),
  gap: 16,
  cardKey: undefined,
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
  formModalWidth: 560,
  detailWidth: 640,
  detailTitle: undefined,
  titleHelp: undefined,
  titleHelpTitle: undefined,
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  kpis?: () => unknown
  card?: (props: {
    item: T
    index: number
    selected: boolean
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
