<script setup lang="ts">
import { computed, h, ref, watch, getCurrentInstance } from 'vue'
import { useRoute, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'
import { NMenu, NSwitch } from 'naive-ui'
import type { MenuOption } from 'naive-ui'
import {
  useAdminRouteStore,
  type AdminMenuItem,
} from '../../stores/useAdminRouteStore'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { TSvgIcon } from '@tnzi/ui'
import TSystemLogo from '../utility/TSystemLogo.vue'
import { translatePageKey } from '../../pages/_shared/translate'

/**
 * Resolve a menu label. If it looks like an i18n key (`admin.*` or
 * `tnzi.admin.*`), run it through the page-key translator which falls
 * back to a humanised last segment when the locale is missing the key.
 * Plain strings (hand-rolled labels) pass through untouched.
 */
function resolveLabel(label: string): string {
  if (!label) return ''
  if (label.startsWith('admin.') || label.startsWith('tnzi.')) {
    return translatePageKey('', label)
  }
  return label
}

interface Props {
  mode?: 'vertical' | 'vertical-mix'
  width?: number
  collapsedWidth?: number
  /** Brand title shown in the sider header (matches soybean's logo + title block). */
  brand?: string
  /** Muted second line under the brand title. Hidden in the collapsed /
   *  vertical-mix (icon-only) header so it never clips the narrow rail. */
  brandSubtitle?: string
  /** Iconify icon name for the brand logo. When omitted, TSystemLogo
   *  renders the built-in TBrandMark inline SVG (3-cube motif). */
  brandIcon?: string
  /**
   * Hide the entire sider header row. Used by TAdminShell in
   * top-hybrid-* / horizontal modes where the brand already lives in
   * the top header bar — without this the brand renders twice (one
   * stuck in the sider, one in the header). */
  hideHeader?: boolean
  /**
   * Optional override of the highlighted menu key. Used by `vertical-mix`
   * to show the currently-hovered top-level entry as active even before
   * the route navigates (the route only changes on leaf select).
   */
  activeMenuKeyOverride?: string
  /**
   * Phase B: when true, switch the sider to dark background and propagate
   * `inverted` to the inner NMenu so menu item text/hover colours flip
   * to the inverted palette. Mirrors soybean's `themeStore.sider.inverted`
   * (which only takes effect in light mode + vertical layouts).
   */
  inverted?: boolean
  /**
   * Phase E: override the menu items rendered by the sider. Defaults to
   * `routeStore.menus` (full tree). Hybrid layout modes pass the slice of
   * the tree that belongs in the sider for that mode (e.g. for
   * `top-hybrid-header-first` this is the *children* of the currently-
   * active 1st level item; for `vertical-mix` rail this is just the 1st
   * level items themselves).
   */
  items?: AdminMenuItem[]
  /**
   * Render the built-in settings entry in the sidebar footer (gear icon →
   * `router.push({ name: 'settings' })`). Only shows when the `settings`
   * route exists AND its `meta.permission` is held (fail-open before
   * permissions load / for super users, mirroring the menu filter); a
   * consumer-provided `footer` slot fully replaces it.
   */
  showSettingsEntry?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'vertical',
  width: 220,
  collapsedWidth: 64,
  brand: 'Tnzi Admin',
  brandSubtitle: undefined,
  brandIcon: undefined,
  hideHeader: false,
  activeMenuKeyOverride: undefined,
  inverted: false,
  items: undefined,
  showSettingsEntry: true,
})

const emit = defineEmits<{
  menuSelect: [menu: AdminMenuItem]
}>()

const routeStore = useAdminRouteStore()
const appStore = useAdminAppStore()
const authStore = useAdminAuthStore()

// Built-in-menus toggle (footer, super admin only): show or hide the
// framework's preset admin menus (`meta.builtIn` groups), leaving the
// consumer app's own menus untouched. Display-only: flipping it re-filters
// the menu tree, never reachability.
const showBuiltInToggle = computed(() => authStore.isSuperUser)
const builtInLabel = computed(() => resolveLabel('admin.common.builtInMenus'))
const builtInTip = computed(() => resolveLabel('admin.common.builtInMenusTip'))

// Phase I.7.6: drive NMenu's active state from the *current vue-router route
// name* — previously this was wired to `tabStore.activeTabId`, which only
// updates when a tab is opened, so the first page load showed no active item.
//
// `useRoute()` throws when no router has been installed (e.g. unit tests
// that mount this component in isolation). Detect via `getCurrentInstance()`
// and gracefully fall back to a static "no active route" value.
function safeUseRoute(): RouteLocationNormalizedLoaded | null {
  const instance = getCurrentInstance()
  const hasRouter = !!instance?.appContext.config.globalProperties.$router
  if (!hasRouter) return null
  try {
    return useRoute()
  } catch {
    return null
  }
}
const route = safeUseRoute()

function safeUseRouter(): Router | null {
  const instance = getCurrentInstance()
  const router = instance?.appContext.config.globalProperties.$router as Router | undefined
  return router ?? null
}
const router = safeUseRouter()

// The built-in gear must obey the settings ROUTE's reachability with the same
// fail-open semantics as the menu filter - otherwise a user the matrix locked
// out of Settings still sees an entry that only bounces to /403. The bundled
// route uses `meta.anySettingsPermission` (the config center spans many modules
// with no single code) → `canAnySettings()`; older/custom routes with a plain
// `meta.permission`/`meta.permissions` still work. It must ALSO obey module
// availability (the route carries `moduleGate: 'system'`): on a host without the
// System module the settings route lands in `unavailableRouteNames`, and the
// gear hides for everyone (no super-user bypass — the module guard bounces the
// navigation to /403 regardless).
const { can, canAny, canAnySettings } = usePermissionGuard()
const hasSettingsRoute = computed(() => {
  if (!router?.hasRoute('settings')) return false
  if (routeStore.unavailableRouteNames.has('settings')) return false
  // Partial router mocks (tests) may lack resolve() - same guard as goSettings.
  if (typeof router.resolve !== 'function') return true
  const meta = (router.resolve({ name: 'settings' }).meta ?? {}) as {
    permission?: unknown
    permissions?: unknown
    anySettingsPermission?: unknown
  }
  if (meta.anySettingsPermission === true) return canAnySettings()
  if (typeof meta.permission === 'string' && meta.permission) return can(meta.permission)
  if (Array.isArray(meta.permissions)) {
    const plural = meta.permissions.filter((p): p is string => typeof p === 'string' && p !== '')
    if (plural.length > 0) return canAny(plural)
  }
  return true
})
const isSettingsActive = computed(() => route?.name === 'settings')
const settingsLabel = computed(() => resolveLabel('admin.common.settings'))

function goSettings(): void {
  if (!router) return
  void router.push({ name: 'settings' })
  // 以合成菜单项走 menuSelect 通道，让 TAdminShell 的移动端抽屉收起逻辑生效。
  const path = typeof router.resolve === 'function' ? router.resolve({ name: 'settings' }).path : '/settings'
  emit('menuSelect', { key: 'settings', label: settingsLabel.value, path })
}

const activeMenuKey = computed<string>(() => {
  if (props.activeMenuKeyOverride) return props.activeMenuKeyOverride
  // Hidden detail/sub routes (e.g. `ai.agents.detail`) point back to their list
  // page via `meta.activeMenu` so the parent sidebar entry stays highlighted and
  // its group expanded while a detail page is open.
  const active = route?.meta?.activeMenu
  if (typeof active === 'string' && active) return active
  const name = route?.name
  return typeof name === 'string' ? name : ''
})

// Walk the menu tree to find ancestor keys of the active item so we can
// auto-expand the parent group on route change (e.g. landing on
// `/admin/identity/users` should expand the `identity` group). The
// `expandedKeys` ref is controlled — `NMenu` v:expanded-keys lets the
// user toggle groups, and we watch the route to *add* (never remove)
// the active ancestors so navigating into a leaf keeps its parent open.
const expandedKeys = ref<string[]>([])

function ancestorsOf(key: string): string[] {
  if (!key) return []
  const trail: string[] = []
  function find(items: AdminMenuItem[], current: string[]): boolean {
    for (const item of items) {
      const next = [...current, item.key]
      if (item.key === key) {
        trail.push(...current)
        return true
      }
      if (item.children && find(item.children, next)) return true
    }
    return false
  }
  find(props.items ?? routeStore.menus, [])
  return trail
}

watch(
  activeMenuKey,
  (key) => {
    const ancestors = ancestorsOf(key)
    const merged = new Set([...expandedKeys.value, ...ancestors])
    expandedKeys.value = Array.from(merged)
  },
  { immediate: true },
)

function onUpdateExpandedKeys(keys: string[]): void {
  expandedKeys.value = keys
}

function toOption(item: AdminMenuItem): MenuOption {
  const option: MenuOption = {
    key: item.key,
    label: resolveLabel(item.label),
  }
  if (item.icon) {
    // NMenu accepts a `() => VNode` for the icon slot.
    option.icon = () => h(TSvgIcon, { icon: item.icon as string, size: 18 })
  }
  if (item.children && item.children.length > 0 && props.mode !== 'vertical-mix') {
    option.children = item.children.map(toOption)
  }
  return option
}

// Source of menu items: explicit prop wins, else default to the full tree.
const sourceMenus = computed<AdminMenuItem[]>(
  () => props.items ?? routeStore.menus,
)

const menuOptions = computed<MenuOption[]>(() => {
  if (props.mode === 'vertical-mix') {
    // Phase G fix: include the icon in the rail's NMenu options. The
    // previous `{key, label}` shape silently dropped icons, so the
    // 90px rail rendered as a label-only column with zero visual
    // affordance. Mirrors soybean's first-level-menu rendering.
    return sourceMenus.value.map((m) => {
      const opt: MenuOption = { key: m.key, label: resolveLabel(m.label) }
      if (m.icon) {
        opt.icon = () => h(TSvgIcon, { icon: m.icon as string, size: 20 })
      }
      return opt
    })
  }
  return sourceMenus.value.map(toOption)
})

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

const currentWidth = computed(() =>
  appStore.siderCollapse ? props.collapsedWidth : props.width,
)

/** Phase G follow-up: header should show the icon-only logo (no brand
    text) whenever the available width can't fit the brand string —
    that's true when the sider is collapsed AND when we're in the
    narrow vertical-mix rail. */
const isHeaderCompact = computed(
  () => appStore.siderCollapse || props.mode === 'vertical-mix',
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
    :class="{ 't-admin-sidebar--inverted': inverted }"
    :data-mode="mode"
    :style="{ width: `${currentWidth}px` }"
    aria-label="Primary navigation"
  >
    <div v-if="!hideHeader" class="t-admin-sidebar__header">
      <slot name="header">
        <!-- Phase G follow-up: vertical-mix rail (~90px) cannot fit the
             "icon + brand text" combo (text alone runs ~96px). Force the
             icon-only layout here so the rail header doesn't horizontally
             overflow — matches soybean's vertical-mix first-level-menu
             which only shows the logo. -->
        <TSystemLogo
          :title="isHeaderCompact ? '' : brand"
          :subtitle="isHeaderCompact ? '' : brandSubtitle"
          :icon="brandIcon"
          :icon-size="32"
          :layout="isHeaderCompact ? 'icon-only' : 'full'"
        />
      </slot>
    </div>

    <div class="t-admin-sidebar__body">
      <NMenu
        :options="menuOptions"
        :collapsed="appStore.siderCollapse"
        :value="activeMenuKey"
        :expanded-keys="expandedKeys"
        :mode="nMenuMode"
        :indent="mode === 'vertical-mix' ? 0 : 18"
        :collapsed-width="collapsedWidth"
        :collapsed-icon-size="22"
        :inverted="inverted"
        @update:value="onSelect"
        @update:expanded-keys="onUpdateExpandedKeys"
      />
    </div>

    <div v-if="$slots.footer" class="t-admin-sidebar__footer">
      <slot name="footer" />
    </div>
    <div
      v-else-if="showBuiltInToggle || (showSettingsEntry && hasSettingsRoute)"
      class="t-admin-sidebar__footer t-admin-sidebar__footer--default"
    >
      <button
        v-if="showBuiltInToggle"
        type="button"
        class="t-admin-sidebar__settings t-admin-sidebar__ops"
        :class="{ 'is-active': isHeaderCompact && appStore.showBuiltInMenus, 't-admin-sidebar__settings--collapsed': isHeaderCompact }"
        :title="builtInTip"
        @click="appStore.toggleBuiltInMenus()"
      >
        <TSvgIcon icon="mdi:cube-outline" :size="18" />
        <span v-if="!isHeaderCompact" class="t-admin-sidebar__settings-label">{{ builtInLabel }}</span>
        <!-- 纯指示器：整行点击是唯一切换入口，避免行/开关双触发 -->
        <NSwitch
          v-if="!isHeaderCompact"
          :value="appStore.showBuiltInMenus"
          size="small"
          class="t-admin-sidebar__ops-switch"
          style="pointer-events: none"
        />
      </button>
      <button
        v-if="showSettingsEntry && hasSettingsRoute"
        type="button"
        class="t-admin-sidebar__settings"
        :class="{ 'is-active': isSettingsActive, 't-admin-sidebar__settings--collapsed': isHeaderCompact }"
        :title="settingsLabel"
        @click="goSettings"
      >
        <TSvgIcon icon="mdi:cog-outline" :size="18" />
        <!-- vertical-mix 窄轨（~90px）与折叠态都只显图标 — 与 isHeaderCompact 同口径 -->
        <span v-if="!isHeaderCompact" class="t-admin-sidebar__settings-label">{{ settingsLabel }}</span>
      </button>
    </div>
  </aside>
</template>

<style scoped>
.t-admin-sidebar {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--tnzi-admin-sider-bg, var(--tnzi-container-bg, #ffffff));
  border-right: 1px solid var(--tnzi-border, #e5e7eb);
  box-shadow: var(--tnzi-shadow-sider, 2px 0 8px 0 rgb(29 35 41 / 5%));
  transition: width var(--tnzi-admin-motion-duration-base, 0.25s) var(--tnzi-admin-motion-ease-in-out, ease);
  overflow: hidden;
}

.t-admin-sidebar__header {
  flex-shrink: 0;
  height: var(--tnzi-admin-header-height, 56px);
  display: flex;
  align-items: center;
  padding: 0 16px;
  font-weight: 600;
  font-size: 16px;
  color: var(--tnzi-base-text);
  /* No bottom border — matches soybean's seamless logo-to-menu transition. */
}
/* soybean parity — brand title in the sider uses the primary
   theme color (the soybean reference renders "SoybeanAdmin" in
   rgb(100,108,255) 紫). Our previous base-text override was wrong. */
.t-admin-sidebar__header :deep(.t-system-logo__title) {
  color: var(--tnzi-primary, #646cff);
  font-size: 16px;
  font-weight: 600;
}

.t-admin-sidebar__body {
  flex: 1 1 auto;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 8px 0;
  /* Scrollbar styling delegated to styles/polish.css macOS-style overlay rules. */
}

.t-admin-sidebar__body :deep(.n-menu) {
  --n-item-height: 44px;
  --n-item-icon-size: 18px;
  --n-item-text-color-hover: var(--tnzi-primary);
  --n-item-text-color-active: var(--tnzi-primary);
  --n-item-text-color-active-hover: var(--tnzi-primary);
  --n-item-color-hover: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
  --n-item-color-active: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.1);
  --n-item-color-active-hover: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.14);
  --n-item-color-active-collapsed: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.1);
  --n-font-size: 14px;
}

/* soybean parity: full-bleed menu items (no margin/radius) so the active
   highlight spans the full sider width like soybean. The vertical-mix
   override below restores the capsule treatment for the 90px rail. */
.t-admin-sidebar__body :deep(.n-menu .n-menu-item-content) {
  border-radius: 0;
  margin: 0;
  font-weight: 400;
  transition:
    color 0.15s ease,
    background-color 0.15s ease;
}

/* Active leaf row — primary text + slightly stronger weight. Soybean does
   *not* render a left-border accent (despite the I.7.6 brief specifying one);
   the bg highlight alone carries the active state. */
.t-admin-sidebar__body :deep(.n-menu .n-menu-item-content--selected) {
  font-weight: 500;
}

.t-admin-sidebar__footer {
  flex-shrink: 0;
  border-top: 1px solid var(--tnzi-border, #e5e7eb);
  padding: 12px 16px;
}

/* Vertical-mix mode — top-level only, icons centered when present.
   Phase G follow-up: NMenu's `:indent` is set to 0 for vertical-mix (see
   the NMenu binding above). We also need !important on `padding-left`
   here because NMenu has internal `padding-left: var(--n-item-padding)`
   inline styles that win without it — without the override the rail item
   gets ~18px left padding that overflows the 90px rail. */
.t-admin-sidebar[data-mode='vertical-mix'] .t-admin-sidebar__body :deep(.n-menu .n-menu-item-content) {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 8px 4px !important;
  margin: 4px 6px;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  text-align: center;
  font-size: 12px;
}
/* Center the icon (NMenu wraps it in a small fixed-width slot). */
.t-admin-sidebar[data-mode='vertical-mix'] .t-admin-sidebar__body :deep(.n-menu-item-content__icon) {
  margin: 0 0 4px 0;
}
/* Make the label use the full available width with center alignment so a
   slightly long label doesn't push the item past the rail edge. */
.t-admin-sidebar[data-mode='vertical-mix'] .t-admin-sidebar__body :deep(.n-menu-item-content-header) {
  width: 100%;
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-admin-sidebar[data-mode='vertical-mix'] .t-admin-sidebar__body :deep(.n-menu) {
  --n-item-height: 60px;
  /* Phase G — soybean uses 10% primary alpha for active and a neutral
     grey for hover (so hover and active are visually distinct). The
     default vertical sider shares the primary-tint hover/active palette;
     vertical-mix differentiates by lowering active alpha + greying
     hover. Mirrors soybean's first-level-menu styling. */
  --n-item-color-hover: rgb(0 0 0 / 0.06);
  --n-item-color-active: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.10);
  --n-item-color-active-hover: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.14);
  --n-item-color-active-collapsed: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.10);
}

/* Phase B — inverted (dark) sider variant. Soybean's `themeStore.sider.inverted`
   only takes effect under light mode + vertical layout; the parent shell is
   responsible for that gating (we just react to the prop here). NMenu
   `:inverted` already swaps its own item palette via Naive's inverted theme;
   we only need to flip the surface chrome (sider bg + brand title + border). */
.t-admin-sidebar--inverted {
  background: var(--tnzi-admin-sider-inverted-bg, rgb(0, 20, 40));
  border-right-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
}
.t-admin-sidebar--inverted .t-admin-sidebar__header :deep(.t-system-logo__title) {
  /* Brand text remains primary-tinted but on a dark backdrop the saturated
     primary reads weak; soybean uses a soft-white tint. */
  color: var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92));
}
.t-admin-sidebar--inverted .t-admin-sidebar__header :deep(.t-system-logo__subtitle) {
  /* Muted sub-line needs a lighter tint on the dark inverted backdrop. */
  color: var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.55));
}
.t-admin-sidebar--inverted .t-admin-sidebar__footer {
  border-top-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
}
.t-admin-sidebar--inverted .t-admin-sidebar__settings {
  color: var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92));
}

.t-admin-sidebar__footer--default {
  padding: 8px;
}
.t-admin-sidebar__settings {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  width: 100%;
  padding: 8px 10px;
  border: none;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: transparent;
  color: var(--tnzi-base-text);
  font-size: 14px;
  cursor: pointer;
  transition: color 0.15s ease, background-color 0.15s ease;
}
.t-admin-sidebar__settings:hover {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
  color: var(--tnzi-primary);
}
.t-admin-sidebar__settings.is-active {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.1);
  color: var(--tnzi-primary);
  font-weight: 500;
}
.t-admin-sidebar__settings--collapsed {
  justify-content: center;
  padding: 8px 0;
}
/* Ops-view row: label takes the flexible width, switch hugs the right edge. */
.t-admin-sidebar__ops .t-admin-sidebar__settings-label {
  flex: 1 1 auto;
  text-align: left;
}
.t-admin-sidebar__ops-switch {
  flex-shrink: 0;
}
</style>
