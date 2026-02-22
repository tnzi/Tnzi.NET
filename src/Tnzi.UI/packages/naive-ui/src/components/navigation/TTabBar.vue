<script setup lang="ts">
import { NBadge } from 'naive-ui'
import type { ITabItem, ITabBarProps } from '@tnzi/core'

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
  if (tab.badge) return tab.badge
  if (props.badge && props.badge[tab.key]) return props.badge[tab.key]!
  return 0
}

function handleClick(tab: ITabItem) {
  if (tab.disabled) return
  emit('change', tab.key, tab)
}
</script>

<template>
  <div class="t-tabbar" :class="{ 'is-fixed': fixed }">
    <div
      v-for="tab in tabs"
      :key="tab.key"
      class="t-tabbar__item"
      :class="{ active: tab.key === activeKey, disabled: tab.disabled }"
      @click="handleClick(tab)"
    >
      <NBadge :value="getBadge(tab)" :show="!!getBadge(tab)">
        <span class="t-tabbar__icon">{{ tab.icon || '○' }}</span>
      </NBadge>
      <span class="t-tabbar__label">{{ tab.label }}</span>
    </div>
  </div>
</template>

<style scoped>
.t-tabbar {
  display: flex;
  justify-content: space-around;
  align-items: center;
  height: 50px;
  border-top: 1px solid var(--n-border-color, #e0e0e6);
  background-color: var(--n-color, #fff);
  box-sizing: border-box;
}

.t-tabbar.is-fixed {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 100;
}

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

.t-tabbar__icon {
  font-size: 20px;
  line-height: 1;
  margin-bottom: 2px;
}

.t-tabbar__label {
  line-height: 1;
}
</style>
