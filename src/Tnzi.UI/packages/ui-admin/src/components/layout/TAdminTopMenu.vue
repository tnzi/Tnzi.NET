<script setup lang="ts">
import { computed, h } from 'vue'
import { NMenu } from 'naive-ui'
import type { DropdownProps, MenuOption } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import {
  useAdminRouteStore,
  type AdminMenuItem,
} from '../../stores/useAdminRouteStore'
import { useAdminTabStore } from '../../stores/useAdminTabStore'

interface Props {
  /**
   * `full` - render the entire menu tree (all levels via NMenu dropdowns); used by the
   *          plain `horizontal` layout mode.
   * `first-level` - render only first-level entries; used by hybrid modes where children
   *                 are shown in the sidebar.
   */
  mode?: 'full' | 'first-level'
  /**
   * Phase E: override the menu items rendered. Defaults to
   * `routeStore.menus` (full tree). Layout modes can pass a slice; when
   * left undefined the `mode` prop (`full` / `first-level`) decides how
   * many levels of the default tree render.
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
  // Same icon treatment as the sidebar menu - the recursion carries the
  // icons into the dropdown submenus too.
  if (item.icon) {
    option.icon = () => h(TSvgIcon, { icon: item.icon as string, size: 16 })
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
    <!-- The dropdown class tags the TELEPORTED submenu popovers so polish.css
         can re-skin just the top-menu dropdowns (follow the header surface)
         without touching page-level dropdowns. -->
    <NMenu
      :options="menuOptions"
      :value="activeKey ?? tabStore.activeTabId"
      mode="horizontal"
      :responsive="true"
      :dropdown-props="{ class: 't-admin-top-menu__dropdown', showArrow: true } as unknown as DropdownProps"
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

/* Center the nav entries within the header's flexible middle region -
   horizontal / hybrid put the whole navigation up top, so it reads as the
   header's centerpiece rather than hugging the logo. With `responsive`,
   naive nests the items inside a full-width `.v-overflow` flex row (the
   "..." counter container) - THAT is the element that must center; the
   `.n-menu--horizontal` root only contains the v-overflow. Alignment does
   not affect the overflow math (it measures child widths, not positions). */
.t-admin-top-menu :deep(.n-menu--horizontal),
.t-admin-top-menu :deep(.n-menu--horizontal > .v-overflow) {
  justify-content: center;
}

.t-admin-top-menu :deep(.n-menu) {
  --n-item-height: 40px;
  --n-item-text-color-hover: var(--tnzi-admin-menu-item-active-color, var(--tnzi-primary));
  --n-item-text-color-active: var(--tnzi-admin-menu-item-active-color, var(--tnzi-primary));
  --n-item-color-hover: var(--tnzi-admin-menu-item-hover-bg, transparent);
  --n-item-color-active: var(--tnzi-admin-menu-item-active-bg, transparent);
}

/* Tighten the icon → label gap. naive reserves an 8px icon margin and the
   16px glyph floats centered in a 24px box, so the VISUAL gap was ~12px;
   2px brings it to ~6px (the grid's auto column shrinks with the margin). */
.t-admin-top-menu :deep(.n-menu .n-menu-item-content .n-menu-item-content__icon) {
  margin-right: 2px !important;
}

.t-admin-top-menu :deep(.n-menu .n-menu-item-content) {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  /* naive's horizontal menu reserves a 2px bottom border for its active
     underline, which shoves the icon+label 1px above the item's true center
     (content centers in the 38px grid row, the pill spans the full 40px).
     The selected state is the tinted pill here, not an underline - drop the
     reserved border so the content centers in the pill. */
  border-bottom: none !important;
  transition:
    color var(--tnzi-admin-motion-duration-fast, 0.15s) ease,
    background-color var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}

/* Selected treatment - mirror the vertical sidebar: the active entry (or the
   parent whose dropdown child is the current page) gets the tinted pill, not
   just a colored label. naive's horizontal menu doesn't paint item
   backgrounds through theme vars, so the pill is applied by class. */
.t-admin-top-menu :deep(.n-menu .n-menu-item-content--selected),
.t-admin-top-menu :deep(.n-menu .n-menu-item-content--child-active) {
  background-color: var(--tnzi-admin-menu-item-active-bg, rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.1));
}
</style>
