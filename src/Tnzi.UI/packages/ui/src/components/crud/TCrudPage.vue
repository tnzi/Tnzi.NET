<script setup lang="ts" generic="T extends Record<string, any>">
/**
 * @deprecated TCrudPage is a backend-admin-specific component that pollutes the C-end @tnzi/ui package.
 * It will be removed in Phase 2 of the 2026-04-12 production-readiness refactor. Migrate to
 * `@tnzi/ui-admin`'s `TCrudPage` (built on `useCrudPage` headless + TCrudToolbar / TFormModal /
 * TBatchActions / TCrudColumnSetting) instead.
 * See docs/superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md §D4.
 */
import { ref, computed } from 'vue'
import { NButton, NSpace, NCard, NPopconfirm } from 'naive-ui'
import { useI18n } from '@tnzi/core/adapters/i18n'
import type { ITableColumn, IPaginationConfig } from '@tnzi/core/types/shared-ui'
import TTable from '../data/TTable.vue'
import TSearchForm from '../form/TSearchForm.vue'

const props = withDefaults(defineProps<{
  title: string
  columns: ITableColumn<T>[] | any[]
  data: T[]
  rowKey?: string | ((row: Record<string, unknown>) => string)
  pagination?: IPaginationConfig | false
  searchable?: boolean
  searchPlaceholder?: string
  creatable?: boolean
  editable?: boolean
  deletable?: boolean
  loading?: boolean
}>(), {
  rowKey: 'id',
  pagination: false,
  searchable: true,
  searchPlaceholder: '',
  creatable: true,
  editable: true,
  deletable: true,
  loading: false,
})

const emit = defineEmits<{
  create: []
  edit: [row: T]
  delete: [row: T]
  batchDelete: [keys: string[]]
  search: [keyword: string]
  reset: []
  pageChange: [pageIndex: number, pageSize: number]
  sort: [field: string, order: 'asc' | 'desc']
}>()

const { t } = useI18n()
const selectedKeys = ref<string[]>([])
const searchKeyword = ref('')

// 构建操作列按钮
const actionButtons = computed(() => {
  const buttons: Array<{
    key: string
    label: string
    type?: 'primary' | 'default' | 'danger' | 'warning'
  }> = []

  if (props.editable) {
    buttons.push({ key: 'edit', label: t('common.edit'), type: 'primary' })
  }
  if (props.deletable) {
    buttons.push({ key: 'delete', label: t('common.delete'), type: 'danger' })
  }

  return buttons
})

// 合并 actions 配置传给 TTable
const tableActions = computed(() => {
  if (actionButtons.value.length === 0) return undefined
  return { buttons: actionButtons.value }
})

function handleSearch(keyword: string) {
  emit('search', keyword)
}

function handleReset() {
  searchKeyword.value = ''
  emit('reset')
}

function handleAction(actionKey: string, row: Record<string, unknown>) {
  if (actionKey === 'edit') {
    emit('edit', row as T)
  } else if (actionKey === 'delete') {
    emit('delete', row as T)
  }
}

function handleSelectionChange(keys: string[]) {
  selectedKeys.value = keys
}

function handlePageChange(pageIndex: number, pageSize: number) {
  emit('pageChange', pageIndex, pageSize)
}

function handleSort(field: string, order: 'asc' | 'desc') {
  emit('sort', field, order)
}
</script>

<template>
  <NCard :title="props.title">
    <template #header-extra>
      <NSpace>
        <NButton
          v-if="props.creatable"
          type="primary"
          @click="emit('create')"
        >
          {{ t('crud.create') }}
        </NButton>
        <NPopconfirm
          v-if="props.deletable && selectedKeys.length > 0"
          @positive-click="emit('batchDelete', selectedKeys)"
        >
          <template #trigger>
            <NButton type="error">
              {{ t('crud.batchDelete') }} ({{ selectedKeys.length }})
            </NButton>
          </template>
          {{ t('crud.confirmBatchDelete') }}
        </NPopconfirm>
      </NSpace>
    </template>

    <TSearchForm
      v-if="props.searchable"
      v-model="searchKeyword"
      :placeholder="props.searchPlaceholder || t('common.search')"
      :loading="props.loading"
      class="mb-4"
      @search="handleSearch"
      @reset="handleReset"
    />

    <TTable
      :data="(props.data as Record<string, unknown>[])"
      :columns="(props.columns as any[])"
      :row-key="props.rowKey"
      :loading="props.loading"
      :pagination="props.pagination"
      :row-actions="tableActions"
      selectable
      :selected-keys="selectedKeys"
      @update:selected-keys="handleSelectionChange"
      @page-change="handlePageChange"
      @sort="handleSort"
      @action="handleAction"
    />

    <!-- 用于放置弹窗表单等额外内容 -->
    <slot name="form-modal" />
  </NCard>
</template>
