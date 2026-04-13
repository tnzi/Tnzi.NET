<script setup lang="ts">
import { NBadge } from 'naive-ui'
import type { ITabItem } from '@tnzi/core'

interface Props {
  tabs: ITabItem[]
  activeKey?: string
  badge?: Record<string, number>
  fixed?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  fixed: false,
})

const emit = defineEmits<{
  change: [key: string, tab: ITabItem]
}>()

function getBadge(tab: ITabItem): number {
  return props.badge?.[tab.key] ?? tab.badge ?? 0
}

function handleClick(tab: ITabItem) {
  if (tab.disabled) return
  emit('change', tab.key, tab)
}
</script>

<template>
  <div
    class="flex justify-around items-center h-[50px] border-t border-naive bg-naive-card box-border"
    :class="{ 'fixed bottom-0 left-0 right-0 z-100': fixed }"
  >
    <div
      v-for="tab in tabs"
      :key="tab.key"
      class="t-tabbar__item"
      :class="{ active: tab.key === activeKey, disabled: tab.disabled }"
      @click="handleClick(tab)"
    >
      <NBadge :value="getBadge(tab)" :show="!!getBadge(tab)">
        <span class="text-5 leading-none mb-0.5">{{ tab.icon || '○' }}</span>
      </NBadge>
      <span class="leading-none">{{ tab.label }}</span>
    </div>
  </div>
</template>

<style scoped>
.t-tabbar__item {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  cursor: pointer;
  color: #999;
  font-size: 12px;
  transition: color 0.2s;
  user-select: none;
}

.t-tabbar__item.active {
  color: var(--n-primary-color, #18a058);
}

.t-tabbar__item.disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
