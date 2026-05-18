<template>
  <n-data-table
    :columns="naiveColumns"
    :data="data"
    :row-key="resolvedRowKey"
    :loading="loading"
    :pagination="naivePagination"
    :bordered="bordered"
    :striped="striped"
    :size="size"
    :checked-row-keys="selectedKeys"
    :row-props="getRowProps"
    @update:checked-row-keys="onSelectionChange"
    @update:sorter="onSorterChange"
    @update:page="onPageChange"
    @update:page-size="onPageSizeChange"
  />
</template>

<script setup lang="ts">
import { computed, h } from 'vue'
import { NDataTable, NButton, NSpace } from 'naive-ui'
import type { DataTableColumn, DataTableSortState, PaginationProps } from 'naive-ui'
import { useI18n } from '@tnzi/core/adapters/i18n'
import type { ITableColumn, IPaginationConfig } from '@tnzi/core/types/shared-ui'

defineOptions({ name: 'TTable' })

interface ActionButton {
  key: string
  label: string
  type?: 'primary' | 'default' | 'danger' | 'warning'
  disabled?: (row: Record<string, unknown>) => boolean
  visible?: (row: Record<string, unknown>) => boolean
}

interface Props {
  data: Record<string, unknown>[]
  columns: ITableColumn[]
  rowKey?: string | ((row: Record<string, unknown>) => string)
  loading?: boolean
  selectable?: boolean
  selectionType?: 'checkbox' | 'radio'
  selectedKeys?: string[]
  pagination?: IPaginationConfig | false
  sortable?: boolean
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
  bordered?: boolean
  striped?: boolean
  emptyText?: string
  size?: 'small' | 'medium' | 'large'
  rowActions?: {
    buttons?: ActionButton[]
  }
}

const props = withDefaults(defineProps<Props>(), {
  rowKey: 'id',
  loading: false,
  selectable: false,
  selectionType: 'checkbox',
  selectedKeys: () => [],
  pagination: false,
  sortable: false,
  sortBy: undefined,
  sortOrder: undefined,
  bordered: true,
  striped: true,
  emptyText: 'No data',
  size: 'medium',
  rowActions: undefined,
})

const emit = defineEmits<{
  'update:selectedKeys': [keys: string[]]
  sort: [field: string, order: 'asc' | 'desc']
  pageChange: [pageIndex: number, pageSize: number]
  rowClick: [row: Record<string, unknown>, index: number]
  action: [actionKey: string, row: Record<string, unknown>, index: number]
}>()

const { t } = useI18n()

const resolvedRowKey = computed(() => {
  if (typeof props.rowKey === 'function') {
    return props.rowKey
  }
  return (row: Record<string, unknown>) => String(row[props.rowKey as string] ?? '')
})

function mapButtonType(type?: string): 'primary' | 'default' | 'error' | 'warning' | 'info' {
  if (type === 'danger') return 'error'
  if (type === 'primary' || type === 'warning') return type
  return 'default'
}

const naiveColumns = computed<DataTableColumn[]>(() => {
  const result: DataTableColumn[] = []

  if (props.selectable) {
    result.push({
      type: 'selection',
      multiple: props.selectionType === 'checkbox',
    } as DataTableColumn)
  }

  for (const col of props.columns) {
    if (col.hidden) continue

    const naiveCol: DataTableColumn = {
      key: col.key,
      title: col.title,
      width: col.width,
      minWidth: col.minWidth,
      align: col.align,
      fixed: col.fixed,
      sorter: col.sortable || (props.sortable && col.sortable !== false) ? 'default' : undefined,
      render: col.render
        ? (row: object, index: number) => col.render!(row as Record<string, unknown>, index)
        : undefined,
      sortOrder:
        props.sortBy === col.key && props.sortOrder
          ? props.sortOrder === 'asc'
            ? 'ascend'
            : 'descend'
          : false,
    } as DataTableColumn

    result.push(naiveCol)
  }

  if (props.rowActions?.buttons?.length) {
    result.push({
      key: '__actions',
      title: t('table.actions'),
      fixed: 'right',
      width: Math.max(props.rowActions.buttons.length * 80, 120),
      render(row: object, index: number) {
        const buttons = props.rowActions!.buttons!
          .filter((btn) => !btn.visible || btn.visible(row as Record<string, unknown>))
          .map((btn) =>
            h(
              NButton,
              {
                size: 'small',
                type: mapButtonType(btn.type),
                quaternary: true,
                disabled: btn.disabled ? btn.disabled(row as Record<string, unknown>) : false,
                onClick: (e: Event) => {
                  e.stopPropagation()
                  emit('action', btn.key, row as Record<string, unknown>, index)
                },
              },
              { default: () => btn.label },
            ),
          )
        return h(NSpace, { size: 4 }, { default: () => buttons })
      },
    } as DataTableColumn)
  }

  return result
})

const naivePagination = computed<PaginationProps | false>(() => {
  if (props.pagination === false) return false

  const config = props.pagination
  const paginationProps: PaginationProps = {
    page: config.pageIndex,
    pageSize: config.pageSize,
    pageCount: Math.ceil(config.total / config.pageSize),
    pageSizes: config.pageSizes,
    showSizePicker: !!config.pageSizes?.length,
    itemCount: config.total,
  }

  if (config.showTotal !== false) {
    paginationProps.prefix = ({ itemCount }: { itemCount: number | undefined }) =>
      t('table.totalItems', { count: String(itemCount ?? 0) })
  }

  return paginationProps
})

// 当前分页 pageSize 用于 pageSize 变化时使用
const currentPageSize = computed(() =>
  props.pagination !== false ? props.pagination.pageSize : 10,
)

function onSelectionChange(keys: Array<string | number>) {
  emit('update:selectedKeys', keys.map(String))
}

function onSorterChange(options: DataTableSortState | DataTableSortState[] | null) {
  if (!options || Array.isArray(options)) return
  const order = options.order === 'ascend' ? 'asc' : 'desc'
  emit('sort', String(options.columnKey), order)
}

function onPageChange(page: number) {
  emit('pageChange', page, currentPageSize.value)
}

function onPageSizeChange(pageSize: number) {
  emit('pageChange', 1, pageSize)
}

function getRowProps(row: Record<string, unknown>, index: number) {
  return {
    style: 'cursor: pointer;',
    onClick: () => {
      emit('rowClick', row, index)
    },
  }
}
</script>
