<template>
  <div v-if="state.hasSelection.value" class="t-batch-actions">
    <span class="t-batch-actions__count">
      {{ t('admin.crud.selected') }} {{ state.selectedCount.value }}
    </span>
    <div class="t-batch-actions__content">
      <slot :selectedIds="state.selectedIds.value" />
    </div>
    <button
      type="button"
      class="t-batch-actions__clear"
      @click="onClear"
    >
      {{ t('admin.crud.unselectAll') }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { useBatchActions } from '../../headless/useBatchActions'

type BatchState = ReturnType<typeof useBatchActions>

interface Props {
  state: BatchState
  translate?: (key: string) => string
}

const props = defineProps<Props>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function onClear(): void {
  props.state.clear()
}
</script>

<style scoped>
.t-batch-actions {
  display: flex;
  align-items: center;
  gap: var(--tnzi-spacing-md, 12px);
  padding: var(--tnzi-spacing-sm, 8px) var(--tnzi-spacing-md, 12px);
  background: var(--tnzi-color-info-bg, #f0f7ff);
  border: 1px solid var(--tnzi-color-info-border, #d6e4ff);
  border-radius: var(--tnzi-admin-radius-sm, 4px);
}

.t-batch-actions__count {
  font-weight: 600;
  color: var(--tnzi-color-text, inherit);
}

.t-batch-actions__content {
  display: flex;
  align-items: center;
  gap: var(--tnzi-spacing-sm, 8px);
  flex: 1;
}

.t-batch-actions__clear {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--tnzi-color-primary, #2080f0);
  padding: 0;
  font: inherit;
}
</style>
