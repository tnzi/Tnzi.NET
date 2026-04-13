<script setup lang="ts">
/**
 * @deprecated TAdminLayout is a backend-admin-specific component that pollutes the C-end @tnzi/ui package.
 * It will be removed in Phase 2 of the 2026-04-12 production-readiness refactor. Migrate to
 * `@tnzi/ui-admin`'s `TAdminShell` (three-mode layout: vertical / vertical-mix / horizontal) instead.
 * See docs/superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md §D4.
 */
import { computed } from 'vue'
import {
  NLayout,
  NLayoutSider,
  NLayoutHeader,
  NLayoutContent,
  NLayoutFooter,
  NMenu,
} from 'naive-ui'
import type { IMenuItem } from '@tnzi/core'
import { convertToMenuOptions } from '../../utils/naive-helpers'

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

const menuOptions = computed(() => {
  if (!props.sidebarItems) return []
  return convertToMenuOptions(props.sidebarItems)
})

function handleMenuSelect(key: string) {
  emit('menuSelect', key)
}
</script>

<template>
  <NLayout has-sider class="h-screen">
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
        <div class="flex-center h-[56px] px-3 overflow-hidden border-b border-naive">
          <img v-if="logo" :src="logo" alt="Logo" class="w-8 h-8 object-contain shrink-0" />
          <span v-if="!sidebarCollapsed" class="ml-2 text-4 font-700 truncate">
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
        class="h-[56px] px-4 flex items-center"
      >
        <slot name="header">
          <span class="font-600 text-4">{{ title }}</span>
        </slot>
      </NLayoutHeader>
      <NLayoutContent content-style="padding: 16px;">
        <slot />
      </NLayoutContent>
      <NLayoutFooter
        v-if="showFooter"
        bordered
        class="py-3 px-4 text-center"
      >
        <slot name="footer" />
      </NLayoutFooter>
    </NLayout>
  </NLayout>
</template>
