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

interface ITableColumn<T = unknown> {
  key: string
  title: string
  width?: number | string
  minWidth?: number
  sortable?: boolean
  align?: 'left' | 'center' | 'right'
  fixed?: 'left' | 'right'
  render?: (row: T, index: number) => unknown
  hidden?: boolean
}

interface ActionButton {
  key: string
  label: string
  type?: 'primary' | 'default' | 'danger' | 'warning'
  disabled?: (row: Record<string, unknown>) => boolean
  visible?: (row: Record<string, unknown>) => boolean
}

interface PaginationConfig {
  pageIndex: number
  pageSize: number
  total: number
  pageSizes?: number[]
  showTotal?: boolean
}

interface Props {
  data: Record<string, unknown>[]
  columns: ITableColumn[]
  rowKey?: string | ((row: Record<string, unknown>) => string)
  loading?: boolean
  selectable?: boolean
  selectionType?: 'checkbox' | 'radio'
  selectedKeys?: string[]
  pagination?: PaginationConfig | false
  sortable?: boolean
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
  bordered?: boolean
  striped?: boolean
  emptyText?: string
  size?: 'small' | 'medium' | 'large'
  actions?: {
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
  actions: undefined,
})

const emit = defineEmits<{
  'update:selectedKeys': [keys: string[]]
  sort: [field: string, order: 'asc' | 'desc']
  pageChange: [pageIndex: number, pageSize: number]
  rowClick: [row: Record<string, unknown>, index: number]
  action: [actionKey: string, row: Record<string, unknown>, index: number]
}>()

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

  if (props.actions?.buttons?.length) {
    result.push({
      key: '__actions',
      title: 'Actions',
      fixed: 'right',
      width: Math.max(props.actions.buttons.length * 80, 120),
      render(row: object, index: number) {
        const buttons = props.actions!.buttons!
          .filter((btn) => !btn.visible || btn.visible(row))
          .map((btn) =>
            h(
              NButton,
              {
                size: 'small',
                type: mapButtonType(btn.type),
                quaternary: true,
                disabled: btn.disabled ? btn.disabled(row) : false,
                onClick: (e: Event) => {
                  e.stopPropagation()
                  emit('action', btn.key, row, index)
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
      `Total ${itemCount ?? 0} items`
  }

  return paginationProps
})

// 当前分页状态用于 pageSize 变化时使用
let currentPage = computed(() =>
  props.pagination !== false ? props.pagination.pageIndex : 1,
)
let currentPageSize = computed(() =>
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
