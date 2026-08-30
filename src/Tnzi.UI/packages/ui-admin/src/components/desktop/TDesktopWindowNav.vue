<script setup lang="ts">
/**
 * `TDesktopWindowNav` - the module's page list, down the left edge of a window.
 *
 * A module is ONE icon on the desktop; its pages live here. That is what keeps
 * the desktop from becoming a wall of a hundred tiles, and it gives a window a
 * real place to navigate from - the shell's sidebar is gone in this layout.
 *
 * Deliberately NOT `TAdminSidebar`, even though it renders the same tree. That
 * component reads two APPLICATION-level singletons: `appStore.siderCollapse`
 * (so collapsing one window would collapse every other window and the shell's
 * own sider) and the global route (so every window would highlight whatever the
 * address bar happens to say). Both are exactly wrong per-window. It also ships
 * a brand header and a settings entry that a window has no use for. What is
 * left after removing all that is this file.
 */
import { computed, h, ref, watch } from 'vue'
import { NMenu, type MenuOption } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { normalizeNavBadge } from '../../utils/nav-badge'
import { resolveWindowIcon } from './desktop-labels'

const props = defineProps<{
  /** The module's own entries. Branch nodes render as expandable groups. */
  items: AdminMenuItem[]
  /** Module name, shown above the list. */
  moduleLabel: string
  moduleIcon?: string
  /** Route name of the page this window is showing. */
  activeKey: string
  /** Icon-rail mode. Owned by the window so it can force it when narrow. */
  collapsed: boolean
  translate?: (key: string, fallback?: string) => string
}>()

const emit = defineEmits<{
  select: [item: AdminMenuItem]
  'update:collapsed': [value: boolean]
}>()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

/** Flat index so a select can be answered with the item, not just its key. */
const byKey = computed(() => {
  const map = new Map<string, AdminMenuItem>()
  const walk = (nodes: AdminMenuItem[]): void => {
    for (const node of nodes) {
      map.set(node.key, node)
      if (node.children?.length) walk(node.children)
    }
  }
  walk(props.items)
  return map
})

function toOption(item: AdminMenuItem): MenuOption {
  const option: MenuOption = {
    key: item.key,
    label: item.label,
    icon: () => h(TSvgIcon, { icon: resolveWindowIcon(item.icon, item.key), size: 17 }),
  }
  const badge = normalizeNavBadge(item.badge)
  if (badge !== null) {
    // `extra` is dropped in the collapsed rail (naive fades the whole row
    // header), which is fine here: a rail this narrow has no room for a count,
    // and the window is one click from expanded.
    option.extra = () => h('span', { class: 't-desktop-nav__badge' }, badge)
  }
  if (item.children?.length) option.children = item.children.map(toOption)
  return option
}

const options = computed<MenuOption[]>(() => props.items.map(toOption))

/**
 * Ancestors of the active key, so drilling into a nested group leaves its
 * parents open. Controlled (not naive's own state) because the active key can
 * change from outside - the window's back button, a row click inside the page.
 */
const expandedKeys = ref<string[]>([])

function ancestorsOf(key: string): string[] {
  const trail: string[] = []
  const find = (nodes: AdminMenuItem[], path: string[]): boolean => {
    for (const node of nodes) {
      if (node.key === key) {
        trail.push(...path)
        return true
      }
      if (node.children?.length && find(node.children, [...path, node.key])) return true
    }
    return false
  }
  find(props.items, [])
  return trail
}

watch(
  () => props.activeKey,
  (key) => {
    // Merge, never replace: a group the user opened by hand stays open.
    expandedKeys.value = Array.from(new Set([...expandedKeys.value, ...ancestorsOf(key)]))
  },
  { immediate: true },
)

function onSelect(key: string): void {
  const item = byKey.value.get(key)
  // Branch nodes have no page behind them; naive only fires `select` for
  // leaves, but a consumer tree could put a path on a branch.
  if (item?.path) emit('select', item)
}
</script>

<template>
  <nav class="t-desktop-nav" :class="{ 't-desktop-nav--collapsed': collapsed }">
    <div class="t-desktop-nav__head">
      <TSvgIcon
        v-if="moduleIcon"
        :icon="moduleIcon"
        :size="16"
        class="t-desktop-nav__head-icon"
        aria-hidden="true"
      />
      <span v-if="!collapsed" class="t-desktop-nav__head-label">{{ moduleLabel }}</span>
      <button
        type="button"
        class="t-desktop-nav__toggle"
        :aria-label="
          collapsed
            ? t('admin.desktop.nav.expand', 'Expand navigation')
            : t('admin.desktop.nav.collapse', 'Collapse navigation')
        "
        :title="
          collapsed
            ? t('admin.desktop.nav.expand', 'Expand navigation')
            : t('admin.desktop.nav.collapse', 'Collapse navigation')
        "
        @click="emit('update:collapsed', !collapsed)"
      >
        <TSvgIcon
          :icon="collapsed ? 'mdi:chevron-right' : 'mdi:chevron-left'"
          :size="16"
          aria-hidden="true"
        />
      </button>
    </div>

    <NMenu
      class="t-desktop-nav__menu"
      :options="options"
      :value="activeKey"
      :collapsed="collapsed"
      :collapsed-width="48"
      :collapsed-icon-size="18"
      :indent="16"
      :root-indent="14"
      @update:value="onSelect"
      @update:expanded-keys="(keys: string[]) => (expandedKeys = keys)"
      :expanded-keys="expandedKeys"
    />
  </nav>
</template>

<style scoped>
.t-desktop-nav {
  display: flex;
  flex: 0 0 auto;
  flex-direction: column;
  width: 190px;
  min-height: 0;
  overflow: hidden;
  background: color-mix(
    in srgb,
    var(--tnzi-bg-deep) var(--tnzi-desktop-solidity),
    transparent
  );
  border-right: 1px solid var(--tnzi-border);
  backdrop-filter: var(--tnzi-desktop-backdrop);
  transition: width 0.14s ease;
}

.t-desktop-nav--collapsed {
  width: 48px;
}

.t-desktop-nav__head {
  display: flex;
  flex: 0 0 auto;
  gap: 7px;
  align-items: center;
  height: 34px;
  padding: 0 4px 0 14px;
  border-bottom: 1px solid var(--tnzi-border);
}

.t-desktop-nav--collapsed .t-desktop-nav__head {
  /* The label is gone; centre what remains rather than leaving it hard left. */
  gap: 0;
  padding: 0;
  justify-content: center;
}

.t-desktop-nav__head-icon {
  flex: 0 0 auto;
  opacity: 0.7;
}

.t-desktop-nav--collapsed .t-desktop-nav__head-icon {
  display: none;
}

.t-desktop-nav__head-label {
  flex: 1 1 auto;
  overflow: hidden;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-overflow: ellipsis;
  text-transform: uppercase;
  white-space: nowrap;
  opacity: 0.55;
}

.t-desktop-nav__toggle {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  padding: 0;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 4px;
  opacity: 0.6;
  transition: background-color 0.12s ease, opacity 0.12s ease;
}

.t-desktop-nav__toggle:hover,
.t-desktop-nav__toggle:focus-visible {
  background: var(--tnzi-desktop-window-hover);
  opacity: 1;
  outline: none;
}

.t-desktop-nav__menu {
  flex: 1 1 auto;
  min-height: 0;
  padding: 6px 0;
  overflow-x: hidden;
  overflow-y: auto;
}

.t-desktop-nav :deep(.t-desktop-nav__badge) {
  padding: 0 6px;
  font-size: 11px;
  font-variant-numeric: tabular-nums;
  line-height: 16px;
  color: #fff;
  background: var(--tnzi-error, #d03050);
  border-radius: 8px;
}
</style>
