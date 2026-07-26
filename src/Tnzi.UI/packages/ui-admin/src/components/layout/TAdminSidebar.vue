<script setup lang="ts">
import { computed, h, ref, watch, getCurrentInstance, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { useRoute, type RouteLocationNormalizedLoaded } from 'vue-router'
import { NMenu } from 'naive-ui'
import type { MenuOption } from 'naive-ui'
import {
  useAdminRouteStore,
  type AdminMenuItem,
} from '../../stores/useAdminRouteStore'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { TSvgIcon } from '@tnzi/ui'
import TSystemLogo from '../utility/TSystemLogo.vue'
import TSidebarSettingsFooter from './TSidebarSettingsFooter.vue'
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
   * the top header bar - without this the brand renders twice (one
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
   * Force the sider onto a light surface with dark foreground even under the
   * global dark mode - used when the user paints the sider a LIGHT custom
   * color while dark mode is active (otherwise NMenu would render light text
   * on the light custom surface). Mutually exclusive with `inverted`.
   */
  lightSurface?: boolean
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
  lightSurface: false,
  items: undefined,
  showSettingsEntry: true,
})

const emit = defineEmits<{
  menuSelect: [menu: AdminMenuItem]
}>()

const routeStore = useAdminRouteStore()
const appStore = useAdminAppStore()

// Phase I.7.6: drive NMenu's active state from the *current vue-router route
// name* - previously this was wired to `tabStore.activeTabId`, which only
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
// `expandedKeys` ref is controlled - `NMenu` v:expanded-keys lets the
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
    text) whenever the available width can't fit the brand string -
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

// ── Scroll-aware edge shadows ──
// The logo header / footer only cast their elevation shadow when the menu is
// actually scrolled (i.e. there is content hidden beneath them). A short,
// non-scrolling menu shows a clean seamless sider with no shadow.
const bodyRef = ref<HTMLElement | null>(null)
const canScrollUp = ref(false)
const canScrollDown = ref(false)
function updateScrollShadows(): void {
  const el = bodyRef.value
  if (!el) return
  canScrollUp.value = el.scrollTop > 1
  canScrollDown.value = el.scrollTop + el.clientHeight < el.scrollHeight - 1
}
let resizeObserver: ResizeObserver | null = null
onMounted(() => {
  void nextTick(updateScrollShadows)
  const el = bodyRef.value
  if (typeof ResizeObserver !== 'undefined' && el) {
    // Observe the body AND its inner menu so content-height changes
    // (expanding a submenu, collapse toggle, menu swap) recompute the state.
    resizeObserver = new ResizeObserver(() => updateScrollShadows())
    resizeObserver.observe(el)
    if (el.firstElementChild) resizeObserver.observe(el.firstElementChild)
  }
})
onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  resizeObserver = null
})
watch(() => appStore.siderCollapse, () => void nextTick(updateScrollShadows))
</script>

<template>
  <aside
    class="t-admin-sidebar"
    :class="{
      't-admin-sidebar--inverted': inverted,
      't-admin-sidebar--surface-light': lightSurface && !inverted,
    }"
    :data-mode="mode"
    :style="{ width: `${currentWidth}px` }"
    aria-label="Primary navigation"
  >
    <div
      v-if="!hideHeader"
      class="t-admin-sidebar__header"
      :class="{ 't-admin-sidebar__header--elevated': canScrollUp }"
    >
      <slot name="header">
        <!-- Phase G follow-up: vertical-mix rail (~90px) cannot fit the
             "icon + brand text" combo (text alone runs ~96px). Force the
             icon-only layout here so the rail header doesn't horizontally
             overflow - matches soybean's vertical-mix first-level-menu
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

    <div ref="bodyRef" class="t-admin-sidebar__body" @scroll.passive="updateScrollShadows">
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

    <div
      v-if="$slots.footer"
      class="t-admin-sidebar__footer"
      :class="{ 't-admin-sidebar__footer--elevated': canScrollDown }"
    >
      <slot name="footer" />
    </div>
    <!-- Default footer (Settings + built-in toggle). Shared with the
         vertical-mix rail via TSidebarSettingsFooter so both render the same
         bottom actions. Renders nothing when the user has neither affordance. -->
    <TSidebarSettingsFooter
      v-else
      :show-settings-entry="showSettingsEntry"
      :elevated="canScrollDown"
      @menu-select="(m) => emit('menuSelect', m)"
    />
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
  /* No hard bottom border (it read as "chopping" the menu). The soft feathered
     shadow only appears WHILE the menu is scrolled beneath the logo bar (the
     `--elevated` modifier, toggled by the scroll watcher) - a short menu shows
     no shadow. `z-index`+`position` let the shadow paint over the menu body. */
  position: relative;
  z-index: 2;
  transition: box-shadow 0.2s ease;
}
.t-admin-sidebar__header--elevated {
  box-shadow: 0 6px 8px -6px var(--tnzi-admin-sider-edge-shadow, rgba(0, 0, 0, 0.06));
}
/* soybean parity - brand title in the sider uses the primary
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

/* Active leaf row - primary text + slightly stronger weight. Soybean does
   *not* render a left-border accent (despite the I.7.6 brief specifying one);
   the bg highlight alone carries the active state. */
.t-admin-sidebar__body :deep(.n-menu .n-menu-item-content--selected) {
  font-weight: 500;
}

.t-admin-sidebar__footer {
  flex-shrink: 0;
  /* No hard top border. The soft feathered upward shadow only appears WHILE
     there is more menu below (scrolled), via the `--elevated` modifier -
     the smooth, scroll-aware counterpart to the logo bar's shadow. */
  position: relative;
  z-index: 2;
  transition: box-shadow 0.2s ease;
  padding: 12px 16px;
}
.t-admin-sidebar__footer--elevated {
  box-shadow: 0 -6px 8px -6px var(--tnzi-admin-sider-edge-shadow, rgba(0, 0, 0, 0.06));
}

/* Vertical-mix mode - top-level only, icons centered when present.
   Phase G follow-up: NMenu's `:indent` is set to 0 for vertical-mix (see
   the NMenu binding above). We also need !important on `padding-left`
   here because NMenu has internal `padding-left: var(--n-item-padding)`
   inline styles that win without it - without the override the rail item
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
  /* Phase G - soybean uses 10% primary alpha for active and a neutral
     grey for hover (so hover and active are visually distinct). The
     default vertical sider shares the primary-tint hover/active palette;
     vertical-mix differentiates by lowering active alpha + greying
     hover. Mirrors soybean's first-level-menu styling. */
  --n-item-color-hover: rgb(0 0 0 / 0.06);
  --n-item-color-active: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.10);
  --n-item-color-active-hover: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.14);
  --n-item-color-active-collapsed: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.10);
}

/* Phase B - inverted (dark) sider variant. Soybean's `themeStore.sider.inverted`
   only takes effect under light mode + vertical layout; the parent shell is
   responsible for that gating (we just react to the prop here). NMenu
   `:inverted` already swaps its own item palette via Naive's inverted theme;
   we only need to flip the surface chrome (sider bg + brand title + border). */
.t-admin-sidebar--inverted {
  /* Prefer the user's custom sider color when set; fall back to the built-in
     dark inverted surface (the invertSider default). */
  background: var(--tnzi-admin-sider-bg, var(--tnzi-admin-sider-inverted-bg, rgb(0, 20, 40)));
  border-right-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
  /* Dark-on-dark needs a somewhat stronger shadow than the light-surface
     default for the (scroll-gated) logo/footer elevation to read. */
  --tnzi-admin-sider-edge-shadow: rgba(0, 0, 0, 0.3);
  /* Base text flips to the light inverted tint so descendants that read it -
     including the footer (TSidebarSettingsFooter, a child component that
     inherits this custom property across the component boundary) - stay
     legible on the dark surface. */
  --tnzi-base-text: var(--tnzi-admin-sider-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
}
.t-admin-sidebar--inverted .t-admin-sidebar__header :deep(.t-system-logo__title) {
  /* Brand text: a custom sider text color wins, else the soft-white inverted
     tint (the saturated primary reads weak on a dark backdrop). */
  color: var(--tnzi-admin-sider-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
}
.t-admin-sidebar--inverted .t-admin-sidebar__header :deep(.t-system-logo__subtitle) {
  /* Muted sub-line needs a lighter tint on the dark inverted backdrop. */
  color: var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.55));
}
/* A custom sider text color drives the NMenu resting item text; when unset it
   falls back to a light inverted value (this rule wins over naive's :inverted
   default, so it MUST carry a fallback - a bare `var()` would resolve invalid
   and drop the menu text to the inherited dark color = unreadable auto menu). */
.t-admin-sidebar--inverted .t-admin-sidebar__body :deep(.n-menu) {
  --n-item-text-color: var(--tnzi-admin-sider-fg, rgba(255, 255, 255, 0.75));
}

/* Forced light surface - a LIGHT custom sider color under the global dark
   mode. Without this, NMenu (not inverted) would render its dark-mode light
   text on the light custom surface. We override the resting item text +
   base tokens so the sider reads correctly. */
.t-admin-sidebar--surface-light {
  --tnzi-base-text: var(--tnzi-admin-sider-fg, var(--tnzi-admin-surface-light-text, rgba(0, 0, 0, 0.88)));
  --tnzi-base-text-muted: var(--tnzi-admin-surface-light-text-muted, rgba(0, 0, 0, 0.5));
  --tnzi-border: var(--tnzi-admin-surface-light-border, rgba(0, 0, 0, 0.1));
  color: var(--tnzi-base-text);
}
.t-admin-sidebar--surface-light .t-admin-sidebar__body :deep(.n-menu) {
  --n-item-text-color: var(--tnzi-admin-sider-fg, var(--tnzi-admin-surface-light-text, rgba(0, 0, 0, 0.88)));
  --n-item-text-color-hover: var(--tnzi-primary);
  --n-item-text-color-active: var(--tnzi-primary);
  --n-item-color-hover: var(--tnzi-admin-surface-light-hover-bg, rgb(var(--tnzi-primary-rgb) / 0.06));
  --n-item-color-active: var(--tnzi-admin-surface-light-active-bg, rgb(var(--tnzi-primary-rgb) / 0.1));
}

</style>
