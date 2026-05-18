<template>
  <div class="t-row-actions">
    <slot name="prepend" :row="row" />
    <NButton
      v-if="showEdit"
      type="primary"
      ghost
      size="small"
      @click="handleEdit"
    >
      {{ t('admin.crud.edit') }}
    </NButton>
    <slot name="middle" :row="row" />
    <NPopconfirm
      v-if="showDelete"
      @positive-click="handleDelete"
    >
      <template #trigger>
        <NButton type="error" ghost size="small">
          {{ t('admin.crud.delete') }}
        </NButton>
      </template>
      {{ confirmText ?? t('admin.crud.confirmDelete') }}
    </NPopconfirm>
    <slot name="append" :row="row" />
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { NButton, NPopconfirm } from 'naive-ui'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'

export interface TRowActionsProps<
  T,
  TId extends string | number = string | number,
> {
  row: T
  state: UseCrudPageReturn<T, TId>
  showEdit?: boolean
  showDelete?: boolean
  confirmText?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TRowActionsProps<T, TId>>(), {
  showEdit: true,
  showDelete: true,
  confirmText: undefined,
  translate: undefined,
})

function t(key: string): string {
  if (props.translate) return props.translate(key)
  const fallback: Record<string, string> = {
    'admin.crud.edit': 'Edit',
    'admin.crud.delete': 'Delete',
    'admin.crud.confirmDelete': 'Are you sure to delete?',
  }
  return fallback[key] ?? key
}

function handleEdit(): void {
  props.state.openEdit(props.row)
}

async function handleDelete(): Promise<void> {
  const id = props.state.rowKey(props.row)
  await props.state.handleDelete([id])
}
</script>

<style scoped>
.t-row-actions {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}
</style>
