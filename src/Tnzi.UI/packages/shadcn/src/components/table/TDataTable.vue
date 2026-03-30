<script setup lang="ts" generic="T extends Record<string, any>">
import { computed, h, isVNode, ref, watch } from 'vue';
import type { VNode } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type {
  IDataTableEmits,
  IDataTableProps,
  ITableColumn,
} from '@tnzi/core/components';
import { calculateTotalPages, clampPageIndex, toggleSort } from '@tnzi/core/utils';
import { Button, Checkbox } from '../primitive/ui';

const props = withDefaults(defineProps<IDataTableProps<T>>(), {
  data: () => [] as T[],
  columns: () => [] as ITableColumn<T>[],
  rowKey: 'id',
  loading: false,
  selectable: false,
  selectionType: 'checkbox',
  selectedKeys: () => [],
  sortable: false,
  bordered: true,
  striped: false,
  emptyText: '',
  size: 'medium',
  actions: undefined,
  exportable: undefined,
  printable: undefined,
});

const emit = defineEmits<IDataTableEmits<T>>();
const { t } = useI18n();

const localSelectedKeys = ref<string[]>([...(props.selectedKeys ?? [])]);
const localSortBy = ref(props.sortBy);
const localSortOrder = ref<'asc' | 'desc' | undefined>(props.sortOrder);
const getPaginationConfig = () => props.pagination || undefined;
const currentPage = ref(getPaginationConfig()?.pageIndex ?? 1);
const currentPageSize = ref(getPaginationConfig()?.pageSize ?? 10);

watch(
  () => props.selectedKeys,
  (next) => {
    localSelectedKeys.value = [...(next ?? [])];
  },
  { deep: true }
);

const visibleColumns = computed(() => (props.columns ?? []).filter((column) => !column.hidden));
const emptyText = computed(() => props.emptyText || t('common.noData'));

const getRowKey = (row: T, index: number): string => {
  if (typeof props.rowKey === 'function') {
    const key = props.rowKey(row);
    if (key == null) {
      console.warn(`[TDataTable] rowKey function returned ${key} for row at index ${index}, falling back to index.`);
      return String(index);
    }
    return key;
  }
  if (typeof props.rowKey === 'string' && row[props.rowKey as keyof T] != null) {
    return String(row[props.rowKey as keyof T]);
  }
  if (typeof props.rowKey === 'string') {
    console.warn(`[TDataTable] rowKey "${props.rowKey}" is undefined/null for row at index ${index}, falling back to index.`);
  }
  return String(index);
};

const isRowSelected = (row: T, index: number) => localSelectedKeys.value.includes(getRowKey(row, index));

const allRowKeys = computed(() => (props.data ?? []).map((row, index) => getRowKey(row, index)));
const allSelected = computed(() => allRowKeys.value.length > 0 && allRowKeys.value.every((key) => localSelectedKeys.value.includes(key)));

const toggleSelectAll = () => {
  localSelectedKeys.value = allSelected.value ? [] : [...allRowKeys.value];
  emit('update:selectedKeys', localSelectedKeys.value);
};

const toggleSelectRow = (row: T, index: number) => {
  const key = getRowKey(row, index);
  if (localSelectedKeys.value.includes(key)) {
    localSelectedKeys.value = localSelectedKeys.value.filter((item) => item !== key);
  } else if (props.selectionType === 'radio') {
    localSelectedKeys.value = [key];
  } else {
    localSelectedKeys.value = [...localSelectedKeys.value, key];
  }
  emit('update:selectedKeys', localSelectedKeys.value);
};

const renderCell = (row: T, column: ITableColumn<T>, index: number): unknown => {
  if (column.render) return column.render(row, index);
  return row[column.key as keyof T] as unknown;
};

/**
 * Functional component wrapper for cell rendering.
 * Handles both VNode and primitive return values from renderCell.
 */
const CellRenderer = (cellProps: { row: T; column: ITableColumn<T>; index: number }) => {
  const result = renderCell(cellProps.row, cellProps.column, cellProps.index);
  if (isVNode(result)) return result as VNode;
  return h('span', {}, String(result ?? ''));
};

const getAlignClass = (align?: 'left' | 'center' | 'right') => {
  if (align === 'center') return 'text-center';
  if (align === 'right') return 'text-right';
  return 'text-left';
};

const handleSort = (column: ITableColumn<T>) => {
  if (!column.sortable) return;
  const next = toggleSort(
    {
      sortBy: localSortBy.value,
      sortDirection: localSortOrder.value,
    },
    column.key,
    'asc'
  );
  localSortBy.value = next.sortBy;
  localSortOrder.value = next.sortDirection;
  emit('sort', next.sortBy, next.sortDirection);
};

const hasPagination = computed(() => !!getPaginationConfig());
const total = computed(() => getPaginationConfig()?.total ?? props.data.length);
const totalPages = computed(() => calculateTotalPages(total.value, currentPageSize.value));

const goPage = (page: number) => {
  const next = clampPageIndex(page, total.value, currentPageSize.value);
  currentPage.value = next;
  emit('pageChange', next, currentPageSize.value);
};

const handleAction = (actionKey: string, row: T, index: number) => emit('action', actionKey, row, index);
</script>

<template>
  <div class="w-full rounded-xl border bg-card text-card-foreground shadow-sm" :class="props.class" :style="props.style">
    <div
      v-if="props.exportable || props.printable"
      class="flex items-center justify-between border-b px-4 py-3"
    >
      <p class="text-sm text-muted-foreground">
        <template v-if="props.selectable">{{ localSelectedKeys.length }} / {{ props.data.length }}</template>
      </p>
      <div class="flex gap-2">
        <Button v-if="props.exportable" variant="outline" size="sm" @click="emit('export', props.exportable?.format || 'json')">
          {{ t('table.export') }}
        </Button>
        <Button v-if="props.printable" variant="outline" size="sm" @click="emit('print')">
          {{ t('table.print') }}
        </Button>
      </div>
    </div>

    <div v-if="props.loading" class="p-8 text-center text-sm text-muted-foreground">
      {{ t('common.loading') }}
    </div>

    <div v-else-if="props.data.length === 0" class="p-8 text-center text-sm text-muted-foreground">
      {{ emptyText }}
    </div>

    <div v-else class="overflow-x-auto">
      <table class="min-w-full text-sm">
        <thead class="bg-muted/40">
          <tr>
            <th v-if="props.selectable" class="w-10 px-3 py-3 text-left">
              <Checkbox :model-value="allSelected" @update:model-value="toggleSelectAll" />
            </th>
            <th
              v-for="column in visibleColumns"
              :key="column.key"
              class="px-3 py-3 text-left font-medium"
              :class="column.sortable ? 'cursor-pointer select-none' : ''"
              :style="{ width: column.width as string | number, minWidth: column.minWidth ? `${column.minWidth}px` : undefined }"
              @click="handleSort(column)"
            >
              <div class="inline-flex items-center gap-1">
                <span>{{ column.title }}</span>
                <span v-if="column.sortable && localSortBy === column.key" class="text-xs text-muted-foreground">
                  {{ localSortOrder === 'asc' ? '↑' : '↓' }}
                </span>
              </div>
            </th>
            <th v-if="props.actions" class="px-3 py-3 text-right font-medium">{{ t('common.more') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(row, index) in props.data"
            :key="getRowKey(row, index)"
            class="border-t transition hover:bg-muted/30"
            :class="[
              props.striped && index % 2 === 1 ? 'bg-muted/20' : '',
              isRowSelected(row, index) ? 'bg-accent/30' : '',
            ]"
            @click="emit('rowClick', row, index)"
            @dblclick="emit('rowDblClick', row, index)"
          >
            <td v-if="props.selectable" class="px-3 py-3" @click.stop>
              <Checkbox
                :model-value="isRowSelected(row, index)"
                @update:model-value="toggleSelectRow(row, index)"
              />
            </td>
            <td
              v-for="column in visibleColumns"
              :key="`${getRowKey(row, index)}-${column.key}`"
              class="px-3 py-3"
              :class="getAlignClass(column.align)"
            >
              <slot :name="`cell-${column.key}`" :row="row" :column="column" :index="index">
                <CellRenderer :row="row" :column="column" :index="index" />
              </slot>
            </td>
            <td v-if="props.actions" class="px-3 py-3 text-right" @click.stop>
              <div class="inline-flex gap-2">
                <Button
                  v-for="button in props.actions?.buttons || []"
                  :key="button.key"
                  size="sm"
                  :variant="button.type === 'danger' ? 'destructive' : button.type === 'primary' ? 'default' : 'outline'"
                  :disabled="button.disabled?.(row)"
                  @click="handleAction(button.key, row, index)"
                >
                  {{ button.label }}
                </Button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="hasPagination" class="flex items-center justify-between border-t px-4 py-3 text-sm">
      <span class="text-muted-foreground">
        {{ props.pagination && props.pagination.showTotal ? t('table.total', { count: total }) : '' }}
      </span>
      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" :disabled="currentPage <= 1" @click="goPage(currentPage - 1)">
          {{ t('common.prev') }}
        </Button>
        <span>{{ currentPage }} / {{ totalPages }}</span>
        <Button variant="outline" size="sm" :disabled="currentPage >= totalPages" @click="goPage(currentPage + 1)">
          {{ t('common.next') }}
        </Button>
      </div>
    </div>
  </div>
</template>

