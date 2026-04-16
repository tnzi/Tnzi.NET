<template>
  <NPopover
    :show="show"
    trigger="manual"
    placement="bottom-end"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <template #trigger>
      <slot name="trigger" />
    </template>
    <div class="t-crud-column-setting">
      <div class="t-crud-column-setting__header">
        <span class="t-crud-column-setting__title">{{ t('admin.crud.columns') }}</span>
        <button
          type="button"
          class="t-crud-column-setting__reset"
          :aria-label="t('admin.crud.reset')"
          @click="onReset"
        >
          {{ t('admin.crud.reset') }}
        </button>
      </div>
      <VueDraggable
        :model-value="localOrder"
        handle=".drag-handle"
        class="t-crud-column-setting__list"
        @update:model-value="onReorder"
      >
        <div
          v-for="col in orderedColumns"
          :key="col.key"
          class="t-crud-column-setting__row"
        >
          <span class="drag-handle" aria-label="Drag">::</span>
          <NCheckbox
            :checked="!props.settings.hiddenKeys.value.has(col.key)"
            @update:checked="() => props.settings.toggle(col.key)"
          >
            {{ col.title }}
          </NCheckbox>
        </div>
      </VueDraggable>
    </div>
  </NPopover>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NPopover, NCheckbox } from 'naive-ui'
import { VueDraggable } from 'vue-draggable-plus'
import { useColumnSettings, type ColumnDef } from '../../headless/useColumnSettings'

type ColumnSettings = ReturnType<typeof useColumnSettings>

interface Props {
  settings: ColumnSettings
  allColumns: ColumnDef[]
  show: boolean
  translate?: (key: string) => string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:show': [boolean]
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

const localOrder = ref<string[]>([...props.settings.orderedKeys.value])

watch(
  () => props.settings.orderedKeys.value,
  (next) => {
    localOrder.value = [...next]
  },
  { deep: true },
)

const columnMap = computed(() => new Map(props.allColumns.map((c) => [c.key, c])))

const orderedColumns = computed<ColumnDef[]>(() =>
  localOrder.value
    .map((k) => columnMap.value.get(k))
    .filter((c): c is ColumnDef => !!c),
)

function onReorder(next: string[]): void {
  localOrder.value = [...next]
  props.settings.reorder(next)
}

function onReset(): void {
  props.settings.reset()
}
</script>

<style scoped>
.t-crud-column-setting {
  min-width: 240px;
  padding: var(--tnzi-spacing-sm, 8px);
}

.t-crud-column-setting__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-bottom: var(--tnzi-spacing-sm, 8px);
  border-bottom: 1px solid var(--tnzi-color-border, #eee);
}

.t-crud-column-setting__title {
  font-weight: 600;
  color: var(--tnzi-color-text, inherit);
}

.t-crud-column-setting__reset {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--tnzi-color-primary, #2080f0);
  padding: 0;
  font: inherit;
}

.t-crud-column-setting__list {
  display: flex;
  flex-direction: column;
  gap: var(--tnzi-spacing-xs, 4px);
  padding-top: var(--tnzi-spacing-sm, 8px);
}

.t-crud-column-setting__row {
  display: flex;
  align-items: center;
  gap: var(--tnzi-spacing-sm, 8px);
  padding: var(--tnzi-spacing-xs, 4px) 0;
}

.drag-handle {
  cursor: grab;
  user-select: none;
  color: var(--tnzi-color-text-muted, #999);
}
</style>
