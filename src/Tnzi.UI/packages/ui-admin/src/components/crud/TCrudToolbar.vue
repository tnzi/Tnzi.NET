<template>
  <div class="t-crud-toolbar">
    <div class="t-crud-toolbar__left">
      <slot name="primary" />
      <slot name="left" />
    </div>
    <div class="t-crud-toolbar__right">
      <slot name="right" />
      <div v-if="showRefresh" class="t-crud-toolbar__refresh">
        <NButton
          :aria-label="t('admin.crud.refresh')"
          @click="emit('refresh')"
        >
          {{ t('admin.crud.refresh') }}
        </NButton>
      </div>
      <div v-if="showExport" class="t-crud-toolbar__export">
        <NButton
          :aria-label="t('admin.crud.export')"
          @click="emit('export')"
        >
          {{ t('admin.crud.export') }}
        </NButton>
      </div>
      <div v-if="showImport" class="t-crud-toolbar__import">
        <NButton
          :aria-label="t('admin.crud.import')"
          @click="emit('import')"
        >
          {{ t('admin.crud.import') }}
        </NButton>
      </div>
      <div v-if="showColumns" class="t-crud-toolbar__columns">
        <NButton
          :aria-label="t('admin.crud.columns')"
          @click="emit('openColumns')"
        >
          {{ t('admin.crud.columns') }}
        </NButton>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { NButton } from 'naive-ui'

interface Props {
  showRefresh?: boolean
  showExport?: boolean
  showImport?: boolean
  showColumns?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  showRefresh: true,
  showExport: true,
  showImport: true,
  showColumns: true,
  translate: undefined,
})

const emit = defineEmits<{
  refresh: []
  export: []
  import: []
  openColumns: []
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}
</script>

<style scoped>
.t-crud-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--tnzi-spacing-sm, 8px);
  padding: var(--tnzi-spacing-sm, 8px) 0;
}

.t-crud-toolbar__left,
.t-crud-toolbar__right {
  display: flex;
  align-items: center;
  gap: var(--tnzi-spacing-sm, 8px);
}
</style>
