<template>
  <div class="t-crud-page">
    <TListShell
      :state="props.state"
      :title="title"
      :mode="mode"
      :show-header="showHeader"
      :show-create="showCreate"
      :show-search="true"
      :show-default-search="showDefaultSearch"
      :search-fields="searchFields"
      :search-placeholder="searchPlaceholder"
      :default-advanced-mode="defaultAdvancedMode"
      :hide-simple-mode="hideSimpleMode"
      :show-export="showExport"
      :show-import="showImport"
      :show-refresh="true"
      :show-batch="showBatchDelete"
      :show-pagination="true"
      :form-modal-width="formModalWidth"
      :detail-width="detailWidth"
      :detail-title="detailTitle"
      :title-help="titleHelp"
      :title-help-title="titleHelpTitle"
      :translate="translate"
    >
      <template #renderer>
        <TTableRenderer
          :state="props.state"
          :mode="mode"
          :show-selection="showBatchDelete"
          :row-actions-width="rowActionsWidth"
          :row-actions="rowActions"
          :row-actions-max-inline="rowActionsMaxInline"
          :row-actions-collapse="rowActionsCollapse"
          :row-key="rowKey"
          :translate="translate"
        >
          <template v-if="$slots.rowActions" #rowActions="{ row }">
            <slot name="rowActions" :row="row" />
          </template>
        </TTableRenderer>
      </template>

      <!-- TCrudPage owns the create button via this #primary override (it
           carries the legacy `t-crud-page__create` class). `show-create` MUST
           also be forwarded to TListShell: when the v-if below renders nothing,
           Vue's renderSlot falls back to TListShell's default #primary button,
           which would otherwise use its own showCreate default (true). -->
      <template #primary>
        <slot name="primary">
          <NButton
            v-if="showCreate && props.state.canCreate !== false"
            type="primary"
            tertiary
            size="small"
            class="t-list-shell__action t-list-shell__create t-crud-page__create"
            @click="props.state.openCreate"
          >
            <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
            {{ t('admin.crud.create') }}
          </NButton>
        </slot>
      </template>

      <template #toolbar>
        <TCrudColumnSetting
          :settings="props.state.columnSettings"
          :all-columns="allColumns"
          :show="showColumnSetting"
          :translate="props.translate"
          @update:show="(v: boolean) => (showColumnSetting = v)"
        >
          <template #trigger>
            <NTooltip placement="top" :delay="600">
              <template #trigger>
                <NButton
                  tertiary
                  size="small"
                  class="t-list-shell__action t-crud-toolbar__columns t-list-shell__trailing-icon"
                  :aria-label="t('admin.crud.columns')"
                >
                  <template #icon><TSvgIcon icon="mdi:cog-outline" :size="16" /></template>
                </NButton>
              </template>
              {{ t('admin.crud.columns') }}
            </NTooltip>
          </template>
        </TCrudColumnSetting>
      </template>

      <template v-if="$slots.header" #header><slot name="header" /></template>
      <template v-if="$slots.kpis" #kpis><slot name="kpis" /></template>
      <template v-if="$slots.search" #search><slot name="search" /></template>
      <template v-if="$slots.toolbarLeft" #toolbarLeft><slot name="toolbarLeft" /></template>
      <template v-if="$slots.toolbarRight" #toolbarRight><slot name="toolbarRight" /></template>
      <template v-if="$slots.batchActions" #batchActions="{ selectedIds }">
        <slot name="batchActions" :selectedIds="selectedIds" />
      </template>
      <template v-if="$slots.error" #error="e"><slot name="error" v-bind="e" /></template>
      <template #form="f"><slot name="form" v-bind="f" /></template>
      <template v-if="$slots.formFooter" #formFooter><slot name="formFooter" /></template>
      <template v-if="$slots.detail" #detail="d"><slot name="detail" v-bind="d" /></template>
      <template v-if="$slots.detailFooter" #detailFooter="d"><slot name="detailFooter" v-bind="d" /></template>
    </TListShell>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { ref } from 'vue'
import { NButton, NTooltip } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TListShell from './TListShell.vue'
import TTableRenderer from './renderers/TTableRenderer.vue'
import TCrudColumnSetting from './TCrudColumnSetting.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'
import type { RowAction } from '../../headless/rowActions'

export interface TCrudPageProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  allColumns: ColumnDef[]
  title?: string
  /** Optional row-key override (defaults to `state.rowKey`). Preserved from
      the legacy TCrudPage public API. */
  rowKey?: (row: T) => TId
  /** `page` (default) fills the page height; `container` is content-sized. */
  mode?: 'page' | 'container'
  /** When false the shell's white header card is not rendered — for pages that
      nest the CRUD block under an outer header (e.g. inside TContentPage tabs)
      where the shell's TPageHeader would duplicate the title bar. */
  showHeader?: boolean
  showCreate?: boolean
  showBatchDelete?: boolean
  showExport?: boolean
  showImport?: boolean
  showDefaultSearch?: boolean
  /** Vestigial / API-compat: the row serial is surfaced as the selection
      checkbox's hover tooltip, so this no longer renders a dedicated column.
      Kept so existing consumers passing it don't break. */
  showSerial?: boolean
  searchFields?: FormSchemaItem[]
  searchPlaceholder?: string
  defaultAdvancedMode?: boolean
  hideSimpleMode?: boolean
  formModalWidth?: number
  /** Width of the read-only view drawer (the `#detail` slot). Default 640.
      Accepts a string (e.g. `'100vw'`) for responsive full-screen on phones. */
  detailWidth?: number | string
  /** Title for the view drawer, derived from the viewed record. */
  detailTitle?: (data: T) => string
  /** Explicit operation-column width. When set it always wins — including
      over the declarative `rowActions` auto-estimate. Leave unset (default)
      to let declarative actions auto-size the column; the legacy
      `#rowActions` slot falls back to a fixed 150px when unset. */
  rowActionsWidth?: number
  /** Declarative operation actions. The framework renders `TRowActions` in the
      table cell + mobile card footer and auto-sizes the operation column.
      Use `editAction`/`viewAction`/`deleteAction` for the built-ins. */
  rowActions?: RowAction<T>[]
  /** Max inline action slots before the tail collapses into More▾ (default 2). */
  rowActionsMaxInline?: number
  /** Disable action collapsing — render every action inline (default true → collapse on). */
  rowActionsCollapse?: boolean
  titleHelp?: string
  titleHelpTitle?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TCrudPageProps<T, TId>>(), {
  title: undefined,
  rowKey: undefined,
  mode: 'page',
  showHeader: true,
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
  formModalWidth: 560,
  detailWidth: 640,
  detailTitle: undefined,
  // No default width — undefined lets the renderer auto-size declarative
  // rowActions (the old fixed 150 was the source of clipped action cells).
  rowActionsWidth: undefined,
  rowActions: undefined,
  rowActionsMaxInline: 2,
  rowActionsCollapse: true,
  titleHelp: undefined,
  titleHelpTitle: undefined,
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  kpis?: () => unknown
  search?: () => unknown
  primary?: () => unknown
  toolbarLeft?: () => unknown
  toolbarRight?: () => unknown
  batchActions?: (props: { selectedIds: TId[] }) => unknown
  error?: (props: { error: Error; retry: () => Promise<void>; dismiss: () => void }) => unknown
  form?: (props: { formData: Partial<T> | null; mode: FormModalMode | null }) => unknown
  formFooter?: () => unknown
  detail?: (props: { data: Partial<T> | null; mode: FormModalMode | null }) => unknown
  detailFooter?: (props: { data: Partial<T> | null }) => unknown
  rowActions?: (props: { row: T }) => unknown
}>()

const showColumnSetting = ref(false)

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}
</script>

<style scoped>
.t-crud-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
</style>
