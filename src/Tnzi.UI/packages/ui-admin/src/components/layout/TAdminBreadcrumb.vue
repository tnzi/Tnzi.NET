<template>
  <NBreadcrumb v-if="visibleItems.length > 0" class="t-admin-breadcrumb" :separator="separator">
    <NBreadcrumbItem
      v-for="item in visibleItems"
      :key="item.to ?? item.label"
      @click="emit('itemClick', item)"
    >
      <slot name="icon" :item="item" />
      {{ resolveLabel(item.label) }}
    </NBreadcrumbItem>
  </NBreadcrumb>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NBreadcrumb, NBreadcrumbItem } from 'naive-ui'

export interface TAdminBreadcrumbItem {
  label: string
  to?: string
  icon?: string
  hidden?: boolean
}

interface Props {
  items: TAdminBreadcrumbItem[]
  separator?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  separator: '/',
})

const emit = defineEmits<{
  itemClick: [item: TAdminBreadcrumbItem]
}>()

const visibleItems = computed(() => props.items.filter((i) => !i.hidden))

function resolveLabel(label: string): string {
  return props.translate ? props.translate(label) : label
}
</script>

<style scoped>
.t-admin-breadcrumb {
  display: flex;
  align-items: center;
  font-size: 14px;
  color: var(--tnzi-text-2);
}
@media (max-width: 640px) {
  .t-admin-breadcrumb {
    font-size: 12px;
  }
}
</style>
