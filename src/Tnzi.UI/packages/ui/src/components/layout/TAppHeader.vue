<script setup lang="ts">
import { NLayoutHeader } from 'naive-ui'

interface Props {
  title?: string
  showBreadcrumb?: boolean
  fixed?: boolean
}

withDefaults(defineProps<Props>(), {
  showBreadcrumb: true,
  fixed: false,
})
</script>

<template>
  <NLayoutHeader
    bordered
    class="h-[56px] px-4 flex-between border-naive"
    :class="{ 'fixed top-0 left-0 right-0 z-100': fixed }"
  >
    <div class="flex items-center gap-3">
      <slot name="logo" />
      <slot v-if="showBreadcrumb" name="breadcrumb" />
    </div>
    <div v-if="title" class="font-600 text-4">{{ title }}</div>
    <div class="flex items-center gap-2">
      <slot name="actions" />
    </div>
  </NLayoutHeader>
</template>
