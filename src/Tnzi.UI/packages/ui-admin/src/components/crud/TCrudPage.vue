<template>
  <div class="t-crud-page">
    <TListShell
      :state="props.state"
      :title="title"
      :mode="mode"
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

      <!-- TCrudPage fully owns the create button via this #primary override
           (it carries the legacy `t-crud-page__create` class plus the
           showCreate / canCreate gating), so `show-create` is intentionally
           NOT forwarded to TListShell — its default #primary is always
           displaced here. -->
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
            <NButton tertiary size="small" class="t-list-shell__action t-crud-toolbar__columns">
              <template #icon><TSvgIcon icon="mdi:cog-outline" :size="16" /></template>
              {{ t('admin.crud.columns') }}
            </NButton>
          </template>
        </TCrudColumnSetting>
      </template>

      <template v-if="$slots.header" #header><slot name="header" /></template>
      <template v-if="$slots.search" #search><slot name="search" /></template>
      <template v-if="$slots.toolbarLeft" #toolbarLeft><slot name="toolbarLeft" /></template>
      <template v-if="$slots.toolbarRight" #toolbarRight><slot name="toolbarRight" /></template>
      <template v-if="$slots.batchActions" #batchActions="{ selectedIds }">
        <slot name="batchActions" :selectedIds="selectedIds" />
      </template>
      <template v-if="$slots.error" #error="e"><slot name="error" v-bind="e" /></template>
      <template #form="f"><slot name="form" v-bind="f" /></template>
      <template v-if="$slots.formFooter" #formFooter><slot name="formFooter" /></template>
    </TListShell>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { ref } from 'vue'
import { NButton } from 'naive-ui'
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
  /** Fixed operation-column width — only used with the legacy `#rowActions`
      slot. Prefer the declarative `rowActions` prop (auto-sizes the column). */
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
  rowActionsWidth: 150,
  rowActions: undefined,
  rowActionsMaxInline: 2,
  rowActionsCollapse: true,
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
  batchActions?: (props: { selectedIds: TId[] }) => unknown
  error?: (props: { error: Error; retry: () => Promise<void>; dismiss: () => void }) => unknown
  form?: (props: { formData: Partial<T> | null; mode: FormModalMode | null }) => unknown
  formFooter?: () => unknown
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
