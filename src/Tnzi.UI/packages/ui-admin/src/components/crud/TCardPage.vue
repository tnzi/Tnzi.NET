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
    <template v-if="$slots.error" #error="e"><slot name="error" v-bind="e" /></template>
    <template #form="f"><slot name="form" v-bind="f" /></template>
    <template v-if="$slots.formFooter" #formFooter><slot name="formFooter" /></template>
  </TListShell>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import TListShell from './TListShell.vue'
import TCardRenderer from './renderers/TCardRenderer.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'

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
  /** When false the shell's white header card is not rendered — for card
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
  showPagination?: boolean
  formModalWidth?: number
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
  showPagination: true,
  formModalWidth: 560,
  titleHelp: undefined,
  titleHelpTitle: undefined,
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  kpis?: () => unknown
  card?: (props: { item: T; index: number; selected: boolean; toggleSelect: () => void }) => unknown
  empty?: () => unknown
  search?: () => unknown
  primary?: () => unknown
  toolbar?: () => unknown
  toolbarLeft?: () => unknown
  toolbarRight?: () => unknown
  error?: (props: { error: Error; retry: () => Promise<void>; dismiss: () => void }) => unknown
  form?: (props: { formData: Partial<T> | null; mode: FormModalMode | null }) => unknown
  formFooter?: () => unknown
}>()
</script>
