<script setup lang="ts">
import { NBreadcrumb, NBreadcrumbItem } from 'naive-ui'

interface BreadcrumbItem {
  label: string
  path?: string
  disabled?: boolean
}

interface Props {
  items: BreadcrumbItem[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  navigate: [path: string]
}>()

function handleClick(item: BreadcrumbItem, isLast: boolean) {
  if (isLast || item.disabled || !item.path) return
  emit('navigate', item.path)
}
</script>

<template>
  <NBreadcrumb>
    <NBreadcrumbItem
      v-for="(item, index) in props.items"
      :key="index"
      @click="handleClick(item, index === props.items.length - 1)"
    >
      <span
        :class="{
          'cursor-pointer': !item.disabled && index !== props.items.length - 1 && item.path,
          'font-500': index === props.items.length - 1,
        }"
      >
        {{ item.label }}
      </span>
    </NBreadcrumbItem>
  </NBreadcrumb>
</template>
