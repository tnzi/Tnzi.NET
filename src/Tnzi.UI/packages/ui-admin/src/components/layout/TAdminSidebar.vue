<script setup lang="ts">
import { computed } from 'vue'
import { NMenu } from 'naive-ui'
import type { MenuOption } from 'naive-ui'
import {
  useAdminRouteStore,
  type AdminMenuItem,
} from '../../stores/useAdminRouteStore'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminTabStore } from '../../stores/useAdminTabStore'

interface Props {
  mode?: 'vertical' | 'vertical-mix'
  width?: number
  collapsedWidth?: number
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'vertical',
  width: 220,
  collapsedWidth: 64,
})

const emit = defineEmits<{
  menuSelect: [menu: AdminMenuItem]
}>()

const routeStore = useAdminRouteStore()
const appStore = useAdminAppStore()
const tabStore = useAdminTabStore()

function toOption(item: AdminMenuItem): MenuOption {
  const option: MenuOption = {
    key: item.key,
    label: item.label,
  }
  if (item.children && item.children.length > 0 && props.mode !== 'vertical-mix') {
    option.children = item.children.map(toOption)
  }
  return option
}

const menuOptions = computed<MenuOption[]>(() => {
  if (props.mode === 'vertical-mix') {
    return routeStore.menus.map((m) => ({ key: m.key, label: m.label }))
  }
  return routeStore.menus.map(toOption)
})

const menuIndex = computed(() => {
  const index = new Map<string, AdminMenuItem>()
  function walk(items: AdminMenuItem[]): void {
    for (const item of items) {
      index.set(item.key, item)
      if (item.children && item.children.length > 0) walk(item.children)
    }
  }
  walk(routeStore.menus)
  return index
})

const currentWidth = computed(() =>
  appStore.siderCollapse ? props.collapsedWidth : props.width,
)

// NMenu only accepts 'vertical' | 'horizontal'. Our 'vertical-mix' is a
// presentational mode where only first-level entries are rendered; under
// the hood we still pass 'vertical' to NMenu.
const nMenuMode = computed<'vertical' | 'horizontal'>(() => 'vertical')

function onSelect(key: string): void {
  const item = menuIndex.value.get(key)
  if (item) emit('menuSelect', item)
}
</script>

<template>
  <aside
    class="t-admin-sidebar"
    :data-mode="mode"
    :style="{ width: `${currentWidth}px` }"
    aria-label="Primary navigation"
  >
    <div v-if="$slots.header" class="t-admin-sidebar__header">
      <slot name="header" />
    </div>

    <div class="t-admin-sidebar__body">
      <NMenu
        :options="menuOptions"
        :collapsed="appStore.siderCollapse"
        :value="tabStore.activeTabId"
        :mode="nMenuMode"
        :indent="18"
        :collapsed-width="collapsedWidth"
        :collapsed-icon-size="20"
        @update:value="onSelect"
      />
    </div>

    <div v-if="$slots.footer" class="t-admin-sidebar__footer">
      <slot name="footer" />
    </div>
  </aside>
</template>

<style scoped>
.t-admin-sidebar {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--tnzi-sider-bg, var(--tnzi-surface, #ffffff));
  border-right: 1px solid var(--tnzi-border-color, #e5e7eb);
  transition: width 0.2s ease;
  overflow: hidden;
}

.t-admin-sidebar__header {
  flex-shrink: 0;
  border-bottom: 1px solid var(--tnzi-border-color, #e5e7eb);
}

.t-admin-sidebar__body {
  flex: 1 1 auto;
  overflow-y: auto;
  overflow-x: hidden;
}

.t-admin-sidebar__footer {
  flex-shrink: 0;
  border-top: 1px solid var(--tnzi-border-color, #e5e7eb);
}
</style>
