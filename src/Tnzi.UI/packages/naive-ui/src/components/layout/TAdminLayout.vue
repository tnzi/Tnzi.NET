<script setup lang="ts">
import { computed } from 'vue'
import {
  NLayout,
  NLayoutSider,
  NLayoutHeader,
  NLayoutContent,
  NLayoutFooter,
  NMenu,
} from 'naive-ui'
import type { MenuOption } from 'naive-ui'
import type { IMenuItem } from '@tnzi/core'

interface Props {
  showSidebar?: boolean
  showHeader?: boolean
  showFooter?: boolean
  sidebarItems?: IMenuItem[]
  sidebarCollapsed?: boolean
  sidebarWidth?: number
  collapsedWidth?: number
  logo?: string
  logoText?: string
  title?: string
}

const props = withDefaults(defineProps<Props>(), {
  showSidebar: true,
  showHeader: true,
  showFooter: false,
  sidebarCollapsed: false,
  sidebarWidth: 220,
  collapsedWidth: 64,
})

const emit = defineEmits<{
  sidebarCollapse: [collapsed: boolean]
  menuSelect: [key: string]
}>()

function convertToMenuOptions(items: IMenuItem[]): MenuOption[] {
  return items
    .filter((item) => !item.hidden)
    .map((item) => {
      const option: MenuOption = {
        key: item.key,
        label: item.label,
        disabled: item.disabled,
      }
      if (item.children && item.children.length > 0) {
        option.children = convertToMenuOptions(item.children)
      }
      return option
    })
}

const menuOptions = computed(() => {
  if (!props.sidebarItems) return []
  return convertToMenuOptions(props.sidebarItems)
})

function handleMenuSelect(key: string) {
  emit('menuSelect', key)
}
</script>

<template>
  <NLayout has-sider class="t-admin-layout">
    <NLayoutSider
      v-if="showSidebar"
      :collapsed="sidebarCollapsed"
      :width="sidebarWidth"
      :collapsed-width="collapsedWidth"
      bordered
      show-trigger
      collapse-mode="width"
      @collapse="emit('sidebarCollapse', true)"
      @expand="emit('sidebarCollapse', false)"
    >
      <slot name="sidebar">
        <div class="t-layout__logo">
          <img v-if="logo" :src="logo" alt="Logo" class="t-layout__logo-img" />
          <span v-if="!sidebarCollapsed" class="t-layout__logo-text">
            {{ logoText || 'Tnzi' }}
          </span>
        </div>
        <NMenu
          :options="menuOptions"
          @update:value="handleMenuSelect"
        />
      </slot>
    </NLayoutSider>
    <NLayout>
      <NLayoutHeader
        v-if="showHeader"
        bordered
        class="t-layout__header"
      >
        <slot name="header">
          <span class="t-layout__header-title">{{ title }}</span>
        </slot>
      </NLayoutHeader>
      <NLayoutContent content-style="padding: 16px;">
        <slot />
      </NLayoutContent>
      <NLayoutFooter
        v-if="showFooter"
        bordered
        class="t-layout__footer"
      >
        <slot name="footer" />
      </NLayoutFooter>
    </NLayout>
  </NLayout>
</template>

<style scoped>
.t-admin-layout {
  height: 100vh;
}

.t-layout__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 56px;
  padding: 0 12px;
  overflow: hidden;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}

.t-layout__logo-img {
  width: 32px;
  height: 32px;
  object-fit: contain;
  flex-shrink: 0;
}

.t-layout__logo-text {
  margin-left: 8px;
  font-size: 16px;
  font-weight: 700;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.t-layout__header {
  height: 56px;
  padding: 0 16px;
  display: flex;
  align-items: center;
}

.t-layout__header-title {
  font-weight: 600;
  font-size: 16px;
}

.t-layout__footer {
  padding: 12px 16px;
  text-align: center;
}
</style>
