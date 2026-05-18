<script setup lang="ts">
import { computed } from 'vue'
import { NMenu } from 'naive-ui'
import type { MenuOption } from 'naive-ui'
import {
  useAdminRouteStore,
  type AdminMenuItem,
} from '../../stores/useAdminRouteStore'
import { useAdminTabStore } from '../../stores/useAdminTabStore'

interface Props {
  /**
   * `full` — render the entire menu tree (all levels via NMenu dropdowns); used by the
   *          plain `horizontal` layout mode.
   * `first-level` — render only first-level entries; used by hybrid modes where children
   *                 are shown in the sidebar.
   */
  mode?: 'full' | 'first-level'
  /**
   * Phase E: override the menu items rendered. Defaults to
   * `routeStore.menus` (full tree). Hybrid layouts can pass a slice
   * (e.g. `top-hybrid-sidebar-first` passes the *children* of the
   * currently-active 1st level item so the top hosts the 2nd level).
   */
  items?: AdminMenuItem[]
  /** Optional active key override (otherwise uses tabStore.activeTabId). */
  activeKey?: string
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'full',
  items: undefined,
  activeKey: undefined,
})

const emit = defineEmits<{
  menuSelect: [menu: AdminMenuItem]
}>()

const routeStore = useAdminRouteStore()
const tabStore = useAdminTabStore()

function toOption(item: AdminMenuItem): MenuOption {
  const option: MenuOption = {
    key: item.key,
    label: item.label,
  }
  if (props.mode === 'full' && item.children && item.children.length > 0) {
    option.children = item.children.map(toOption)
  }
  return option
}

const sourceMenus = computed<AdminMenuItem[]>(
  () => props.items ?? routeStore.menus,
)

const menuOptions = computed<MenuOption[]>(() =>
  sourceMenus.value.map((m) => toOption(m)),
)

const menuIndex = computed(() => {
  const index = new Map<string, AdminMenuItem>()
  function walk(items: AdminMenuItem[]): void {
    for (const item of items) {
      index.set(item.key, item)
      if (item.children && item.children.length > 0) walk(item.children)
    }
  }
  walk(sourceMenus.value)
  return index
})

function onSelect(key: string): void {
  const item = menuIndex.value.get(key)
  if (item) emit('menuSelect', item)
}
</script>

<template>
  <nav class="t-admin-top-menu" :data-mode="mode" aria-label="Top navigation">
    <NMenu
      :options="menuOptions"
      :value="activeKey ?? tabStore.activeTabId"
      mode="horizontal"
      :responsive="true"
      @update:value="onSelect"
    />
  </nav>
</template>

<style scoped>
.t-admin-top-menu {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
}

.t-admin-top-menu :deep(.n-menu) {
  --n-item-height: 40px;
  --n-item-text-color-hover: var(--tnzi-admin-menu-item-active-color, var(--tnzi-primary));
  --n-item-text-color-active: var(--tnzi-admin-menu-item-active-color, var(--tnzi-primary));
  --n-item-color-hover: var(--tnzi-admin-menu-item-hover-bg, transparent);
  --n-item-color-active: var(--tnzi-admin-menu-item-active-bg, transparent);
}

.t-admin-top-menu :deep(.n-menu .n-menu-item-content) {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  transition:
    color var(--tnzi-admin-motion-duration-fast, 0.15s) ease,
    background-color var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}
</style>
