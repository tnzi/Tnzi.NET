<script setup lang="ts" generic="T extends Record<string, unknown>">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDataTableEmits, IDataTableProps } from '@tnzi/core/components';

const props = withDefaults(defineProps<IDataTableProps<T>>(), {
  data: () => [] as T[],
  columns: () => [],
  loading: false,
  selectable: false,
  selectedKeys: () => [],
  bordered: true,
  striped: false,
  size: 'medium',
  emptyText: '',
});

const emit = defineEmits<IDataTableEmits<T>>();
const { t } = useI18n();

const tableRows = computed(() => props.data ?? []);

const getValue = (row: T, key: string) => {
  return row[key as keyof T] as unknown;
};

const rowKeyValue = (row: T, index: number): string => {
  if (typeof props.rowKey === 'function') return props.rowKey(row);
  if (typeof props.rowKey === 'string' && row[props.rowKey as keyof T] != null) {
    return String(row[props.rowKey as keyof T]);
  }
  return String(index);
};

const onActionClick = (actionKey: string, row: T, index: number) => {
  emit('action', actionKey, row, index);
};
</script>

<template>
  <div class="rounded-xl bg-white p-3">
    <van-loading v-if="props.loading" size="20px" class="mx-auto my-8" />

    <van-empty v-else-if="tableRows.length === 0" :description="props.emptyText || t('common.noData')" />

    <div v-else class="space-y-3">
      <van-cell-group inset>
        <van-cell v-for="(row, rowIndex) in tableRows" :key="rowKeyValue(row, rowIndex)">
          <template #title>
            <div class="space-y-1">
              <div
                v-for="column in props.columns"
                :key="column.key"
                class="flex items-center justify-between gap-2 text-sm"
              >
                <span class="text-slate-500">{{ column.title }}</span>
                <span class="text-slate-900">{{ String(getValue(row, column.key) ?? '-') }}</span>
              </div>
            </div>
          </template>

          <template #value>
            <div v-if="props.actions?.buttons?.length" class="mt-2 flex justify-end gap-1">
              <template v-for="btn in props.actions.buttons" :key="btn.key">
                <van-button
                  v-if="!btn.visible || btn.visible(row)"
                  size="mini"
                  :type="btn.type === 'danger' ? 'danger' : btn.type === 'primary' ? 'primary' : 'default'"
                  :disabled="btn.disabled ? btn.disabled(row) : false"
                  @click.stop="onActionClick(btn.key, row, rowIndex)"
                >
                  {{ btn.label }}
                </van-button>
              </template>
            </div>
          </template>
        </van-cell>
      </van-cell-group>
    </div>
  </div>
</template>
