<template>
  <n-spin :show="loading">
    <n-list :bordered="bordered" v-if="data.length > 0">
      <n-list-item
        v-for="(item, index) in data"
        :key="resolveItemKey(item, index)"
        class="cursor-pointer transition-colors hover:bg-naive-hover"
        @click="onItemClick(item, index)"
      >
        <slot :item="item" :index="index">
          {{ item }}
        </slot>
      </n-list-item>
    </n-list>
    <div v-else class="py-10 flex-center">
      <slot name="empty">
        <n-empty :description="emptyText" />
      </slot>
    </div>
  </n-spin>
</template>

<script setup lang="ts">
import { NList, NListItem, NEmpty, NSpin } from 'naive-ui'

interface Props {
  data: Record<string, unknown>[]
  loading?: boolean
  emptyText?: string
  bordered?: boolean
  itemKey?: string | ((item: Record<string, unknown>, index: number) => string)
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  emptyText: 'No data',
  bordered: true,
  itemKey: undefined,
})

const emit = defineEmits<{
  itemClick: [item: Record<string, unknown>, index: number]
  loadMore: []
}>()

function resolveItemKey(item: Record<string, unknown>, index: number): string {
  if (!props.itemKey) {
    return item?.id != null ? String(item.id) : String(index)
  }
  if (typeof props.itemKey === 'function') {
    return props.itemKey(item, index)
  }
  return item?.[props.itemKey] != null ? String(item[props.itemKey]) : String(index)
}

function onItemClick(item: Record<string, unknown>, index: number) {
  emit('itemClick', item, index)
}
</script>

