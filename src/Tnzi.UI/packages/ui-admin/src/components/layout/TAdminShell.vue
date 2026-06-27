<script setup lang="ts">
import { computed, getCurrentInstance, h, inject, onBeforeUnmount, onMounted, ref, toRef } from 'vue'
import { NMenu, type MenuOption } from 'naive-ui'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded } from 'vue-router'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import TAdminSidebar from './TAdminSidebar.vue'
import TAdminMixNavRail from './TAdminMixNavRail.vue'
import TAdminTopMenu from './TAdminTopMenu.vue'
import TSystemLogo from '../utility/TSystemLogo.vue'
import TAdminHeader from './TAdminHeader.vue'
import TAdminTabs from './TAdminTabs.vue'
import TAdminContent from './TAdminContent.vue'
import TAdminFooter from './TAdminFooter.vue'
import TAdminWatermark from './TAdminWatermark.vue'
import TGlobalSearch from './TGlobalSearch.vue'
import TAdminUserAvatar from './TAdminUserAvatar.vue'
import TChatHost from '../chat/TChatHost.vue'
import TBackTop from '../utility/TBackTop.vue'
import { TPinToggler } from '@tnzi/ui'
import { TSvgIcon } from '@tnzi/ui'
import type { TAdminFooterLink } from './TAdminFooter.vue'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import {
  useAdminThemeStore,
  type AdminLayoutMode,
  type PageTransition,
} from '../../stores/useAdminThemeStore'
import {
  useAdminRouteStore,
  type AdminMenuItem,
} from '../../stores/useAdminRouteStore'
import type { AdminTab } from '../../stores/useAdminTabStore'
import { useAdminMenuContext } from '../../headless/useAdminMenuContext'
import { useAdminShellLayout } from '../../headless/useAdminShellLayout'

interface SiderConfig {
  visible?: boolean
  width?: number
  subWidth?: number
  collapsedWidth?: number
  fixed?: boolean
  /** Brand title shown in the sider header (Phase I.7.6+). */
  brand?: string
  /** Muted second line under the brand title in the (expanded) sider header. */
  brandSubtitle?: string
  /** Iconify icon name for the brand logo. */
  brandIcon?: string
}

interface HeaderConfig {
  visible?: boolean
  fixed?: boolean
  showToggler?: boolean
  showBreadcrumb?: boolean
  showSearch?: boolean
  showFullscreen?: boolean
  showThemeBtn?: boolean
  showThemeSchemaBtn?: boolean
  showLangSwitch?: boolean
  showReload?: boolean
  showNotification?: boolean
  showUser?: boolean
}

interface TabsConfig {
  visible?: boolean
  closeByMiddleClick?: boolean
  draggable?: boolean
  showReload?: boolean
}

interface ContentConfig {
  transition?: PageTransition
}

interface FooterConfig {
  visible?: boolean
  copyright?: string
  links?: TAdminFooterLink[]
}

interface Props {
  /**
   * Layout mode. If omitted the store value is used so the Settings Drawer
   * stays in sync; an explicit prop wins for tests / sandboxes.
   */
  mode?: AdminLayoutMode
  title?: string
  sider?: SiderConfig
  header?: HeaderConfig
  tabs?: TabsConfig
  content?: ContentConfig
  footer?: FooterConfig
  /**
   * Phase H1 I1: when true (default), TAdminShell mounts a `TGlobalSearch`
   * modal and binds Cmd/Ctrl+K to open it. Set to `false` if you want
   * to handle the `openSearch` emit yourself.
   */
  builtinSearch?: boolean
  /**
   * Phase H2 B4: when true (default), TAdminShell renders a default
   * `TAdminUserAvatar` in the header's #user slot. Set to `false` if
   * you want to handle the slot yourself.
   */
  builtinUserAvatar?: boolean
  /**
   * Phase H4 L6: when true (default), TAdminShell mounts a floating
   * `TBackTop` button in the bottom-right of the content area. Set
   * to `false` if your content layout already provides one.
   */
  builtinBackTop?: boolean
  /**
   * When true (default), TAdminShell renders TChatHost in the header's
   * user slot before TAdminUserAvatar, providing a persistent chat launcher
   * and notification badge. Set to `false` to handle chat yourself.
   */
  builtinChat?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  mode: undefined,
  title: 'Tnzi Admin',
  sider: () => ({}),
  header: () => ({}),
  tabs: () => ({}),
  content: () => ({}),
  footer: () => ({}),
  builtinSearch: true,
  builtinUserAvatar: true,
  builtinBackTop: true,
  builtinChat: true,
})

const emit = defineEmits<{
  openSearch: []
  openThemeDrawer: []
  localeChange: [locale: 'en' | 'zh-cn']
  menuSelect: [menu: AdminMenuItem]
  /** Phase I.7.8: forwarded from TAdminTabs so AdminShellRoot can `router.push`. */
  tabClick: [tab: AdminTab]
}>()

const appStore = useAdminAppStore()
const themeStore = useAdminThemeStore()
const routeStore = useAdminRouteStore()

// Phase B: optionally consume the @tnzi/ui theme context to detect dark mode
// (used to gate invertSider — see siderInverted below). Tests that mount
// without the plugin get `undefined` and we treat that as "light".
const themeContext = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)
const isDark = computed(() => themeContext?.isDark.value === true)
const primaryColor = computed(
  () => themeContext?.settings.value.colors.primary ?? '#646cff',
)

// useRoute() throws if no router is installed (TAdminShell.test mounts
// without one). Fall back to a stable empty descriptor so the mix-layout
// computeds don't crash.
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
function safeUseRouter(): ReturnType<typeof useRouter> | null {
  const instance = getCurrentInstance()
  const hasRouter = !!instance?.appContext.config.globalProperties.$router
  if (!hasRouter) return null
  try {
    return useRouter()
  } catch {
    return null
  }
}
const route = safeUseRoute()
const router = safeUseRouter()
const currentRouteName = computed<string>(() => {
  const name = route?.name
  return typeof name === 'string' ? name : ''
})

// Phase E: layered menu state (active 1st level, 2nd level slices, etc.)
// shared across hybrid layout modes. Mirrors soybean's
// `provideMixMenuContext()`. Without this, the 4 hybrid modes can only
// render the full menu tree everywhere — they look identical.
const menuCtx = useAdminMenuContext({
  menus: computed(() => routeStore.menus),
  routeName: currentRouteName,
  // Bind to themeStore.autoSelectFirstMenu so the Theme Drawer's
  // "auto-select first child" toggle actually controls the headless logic.
  autoSelectSecondLevel: computed(() => themeStore.autoSelectFirstMenu),
})

// 0.2.72+ (B5): the 16 layout-mode derivations + mobile drawer width +
// drawerOpen were sunk to `useAdminShellLayout` so the layout decisions
// can be unit-tested in isolation from the SFC. Destructure into the
// flat var names the template already uses to avoid touching the
// 400-line template below.
const {
  effectiveMode,
  siderInverted,
  topMenuVariant,
  showMainSider,
  siderItems,
  topItems,
  topActiveKey,
  shouldRenderHeaderLogo,
  showSubSider,
  sidebarPresentationMode,
  primarySiderWidth,
  subSiderWidth,
  tabsVisible,
  footerVisible,
  headerVisible,
  resolvedTransition,
  mobileDrawerWidth,
  drawerOpen,
} = useAdminShellLayout({
  mode: toRef(props, 'mode'),
  sider: toRef(props, 'sider'),
  header: toRef(props, 'header'),
  tabs: toRef(props, 'tabs'),
  footer: toRef(props, 'footer'),
  content: toRef(props, 'content'),
  isDark,
  menuCtx,
})

function onMenuSelect(menu: AdminMenuItem): void {
  // On mobile, tapping a nav item should dismiss the drawer (standard
  // mobile pattern) so the user lands on the content rather than being
  // left staring at the still-open menu.
  if (appStore.isMobile) appStore.setSiderCollapse(true)
  emit('menuSelect', menu)
}

// Phase E — top menu select dispatches differently per layout mode:
// - horizontal: leaf navigation, just emit
// - top-hybrid-header-first: 1st level click sets the active 1st level
//   (sider re-renders with new children). If the item has no children,
//   also navigate (it's a leaf 1st level).
function onTopMenuSelect(menu: AdminMenuItem): void {
  const mode = effectiveMode.value
  if (mode === 'top-hybrid-header-first') {
    menuCtx.handleSelectFirstLevelMenu(menu.key)
    if (!menu.children || menu.children.length === 0) {
      emit('menuSelect', menu)
    }
    return
  }
  emit('menuSelect', menu)
}

/** Phase H1 I1: TAdminShell owns the global search modal by default
 *  so consumers get Ctrl/Cmd+K out of the box. They can override by
 *  passing `:builtinSearch="false"` and reacting to `openSearch`. */
const globalSearchVisible = ref(false)
function onOpenSearch(): void {
  if (props.builtinSearch !== false) {
    globalSearchVisible.value = true
  }
  emit('openSearch')
}
function onSelectSearchResult(item: { path?: string }): void {
  if (!router || !item.path) return
  router.push(item.path).catch(() => undefined)
}

function onOpenThemeDrawer(): void {
  emit('openThemeDrawer')
}

function onLocaleChange(locale: 'en' | 'zh-cn'): void {
  emit('localeChange', locale)
}

function closeMobileDrawer(): void {
  appStore.setSiderCollapse(true)
}

// ── vertical-mix layout state — strict soybean parity ─────────────
// Reference: D:\Github\soybean-admin-example\src\layouts\modules\
//   global-menu\modules\vertical-mix-menu.vue + context\index.ts
//
// Interaction contract (click-driven, NOT hover-driven):
//   • Click a 1st level rail item with children → updates
//     menuCtx.activeFirstLevelMenuKey (which drives secondLevelMenus
//     directly — no separate `override` ref) and opens the drawer.
//   • Click a 1st level rail item without children → navigates.
//   • mouseleave the mix region → closes the drawer; if not pinned,
//     also restores activeFirstLevelMenuKey to the route's owner.
//   • Hover inside the region does NOT change which children show —
//     that would diverge from soybean's design.
//   • Pinned (`mixSiderFixed`) → drawer stays open, the outer wrapper
//     occupies real layout width (pushes content right). Unpinned →
//     drawer is absolutely positioned, floating over the content.
//
// State refs:
//   • mixDrawerVisible — toggled by click (open) / mouseleave (close).
//     Replaces the older `mixDrawerHover` + `mixFirstLevelOverride`
//     pair, which inverted the soybean semantics and caused the
//     "drawer always shows IAM children" bug.

const mixDrawerVisible = ref(false)

const visibleFirstLevelKey = computed<string>(
  () => menuCtx.activeFirstLevelMenuKey.value,
)

const mixChildren = computed<AdminMenuItem[]>(
  () => menuCtx.secondLevelMenus.value,
)

const hasMixChildren = computed<boolean>(() => mixChildren.value.length > 0)

const showMixDrawer = computed<boolean>(
  () => hasMixChildren.value && (mixDrawerVisible.value || appStore.mixSiderFixed),
)

/** Outer wrapper width — only reserves space when the drawer is pinned
 *  (matches soybean's `appStore.mixSiderFixed && hasChildMenus`). When
 *  not pinned, the absolute inner aside floats over the main content. */
const mixWrapperWidth = computed<number>(() =>
  appStore.mixSiderFixed && hasMixChildren.value ? subSiderWidth.value : 0,
)

/** Inner aside width — animates from 0 → mixChildMenuWidth when the
 *  drawer should be shown. Always set; CSS does the transition. */
const mixInnerWidth = computed<number>(() =>
  showMixDrawer.value ? subSiderWidth.value : 0,
)

function mixChildToOption(item: AdminMenuItem): MenuOption {
  const option: MenuOption = { key: item.key, label: item.label }
  if (item.icon) {
    option.icon = () => h(TSvgIcon, { icon: item.icon as string, size: 18 })
  }
  if (item.children && item.children.length > 0) {
    option.children = item.children.map(mixChildToOption)
  }
  return option
}

const mixChildOptions = computed<MenuOption[]>(() =>
  mixChildren.value.map(mixChildToOption),
)

// Phase G — vertical-mix drawer NMenu auto-expands the ancestor path of
// the current route so a freshly-rendered drawer doesn't hide the active
// leaf inside a collapsed group. soybean does this via `getSelectedMenuKeyPath`.
const mixDrawerExpandedKeys = computed<string[]>(() => {
  const path: string[] = []
  function walk(items: AdminMenuItem[]): boolean {
    for (const item of items) {
      if (item.key === currentRouteName.value) return true
      if (item.children?.length) {
        if (walk(item.children)) {
          path.unshift(item.key)
          return true
        }
      }
    }
    return false
  }
  walk(mixChildren.value)
  return path
})

function findMenuItem(key: string): AdminMenuItem | null {
  for (const m of routeStore.menus) {
    if (m.key === key) return m
    const stack: AdminMenuItem[] = [...(m.children ?? [])]
    while (stack.length) {
      const item = stack.pop()!
      if (item.key === key) return item
      if (item.children) stack.push(...item.children)
    }
  }
  return null
}

/** soybean parity: `handleSelectMenu(key)` in vertical-mix-menu.vue.
 *  Drives `menuCtx.handleSelectFirstLevelMenu` directly so secondLevelMenus
 *  recomputes from the new active key — no intermediate override ref.
 *
 *  • Has children → switch active 1st level + open drawer (no navigate)
 *  • No children → switch active 1st level + navigate (drawer closes
 *    on mouseleave or stays per pin) */
function onMixPrimarySelect(menu: AdminMenuItem): void {
  menuCtx.handleSelectFirstLevelMenu(menu.key)
  if (menu.children && menu.children.length > 0) {
    mixDrawerVisible.value = true
    return
  }
  // Leaf 1st level — close the drawer (matches soybean's drawerVisible=false
  // after a leaf select) and let the consumer's router.push fire.
  if (!appStore.mixSiderFixed) {
    mixDrawerVisible.value = false
  }
  emit('menuSelect', menu)
}

/** TAdminMixNavRail emits a string `key`. Resolve to the menu item, then
 *  funnel through the same select logic as if a parent surface called it. */
function onMixNavRailSelect(key: string): void {
  const item = routeStore.menus.find((m) => m.key === key)
  if (!item) return
  onMixPrimarySelect(item)
}

function onMixChildSelect(key: string): void {
  const item = findMenuItem(key)
  if (!item) return
  if (!item.children || item.children.length === 0) {
    if (!appStore.mixSiderFixed) {
      mixDrawerVisible.value = false
    }
    emit('menuSelect', item)
  }
}

/** Phase H1 I1: Cmd/Ctrl+K opens the global search. soybean uses
 *  `useShortcuts` to register the same combo. We use a plain window
 *  keydown listener so this works in all hosting environments.
 *  ESC also closes the open mobile drawer so phone users get a
 *  conventional dismiss path (taps outside the drawer hit the
 *  backdrop, ESC handles keyboard / external keyboards). */
function onGlobalKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && appStore.isMobile && !appStore.siderCollapse) {
    appStore.setSiderCollapse(true)
    return
  }
  if (props.builtinSearch === false) return
  const isK = event.key === 'k' || event.key === 'K'
  if (isK && (event.metaKey || event.ctrlKey)) {
    event.preventDefault()
    globalSearchVisible.value = true
  }
}
onMounted(() => {
  if (typeof window === 'undefined') return
  window.addEventListener('keydown', onGlobalKeydown)
  // On initial mount at a mobile width, force the nav drawer closed. The
  // persisted `siderCollapse` can carry a desktop "expanded" (false) value,
  // which would otherwise render the drawer open over the content on a fresh
  // phone load. The breakpoint watcher only fires on resize transitions, so
  // it can't cover this first-paint case.
  if (appStore.isMobile) appStore.setSiderCollapse(true)
})
onBeforeUnmount(() => {
  if (typeof window === 'undefined') return
  window.removeEventListener('keydown', onGlobalKeydown)
})

/** soybean parity: `handleResetActiveMenu` in vertical-mix-menu.vue.
 *  Closes the drawer + (when not pinned) restores the active 1st level
 *  to whichever group owns the current route. Mouseenter is intentionally
 *  not wired — hovering the region must not open or change the drawer
 *  (click-only contract). */
function onMixMouseleave(): void {
  if (appStore.mixSiderFixed) return
  mixDrawerVisible.value = false
  const ownerKey = menuCtx.resolveFirstLevelKeyForRoute(currentRouteName.value)
  if (ownerKey && ownerKey !== menuCtx.activeFirstLevelMenuKey.value) {
    menuCtx.handleSelectFirstLevelMenu(ownerKey)
  }
}

// `drawerOpen` is now provided by `useAdminShellLayout` (see destructure
// above). Kept for backward-compat with the template's usage.
</script>

<template>
  <div
    class="t-admin-shell"
    :class="{
      't-admin-shell--full-content': appStore.fullContent,
      't-admin-shell--invert-sider': themeStore.invertSider,
    }"
    :data-mode="effectiveMode"
    :data-scroll-mode="themeStore.scrollMode"
    :style="{
      '--tnzi-admin-header-height': themeStore.headerHeight + 'px',
      '--tnzi-admin-tab-height': themeStore.tabHeight + 'px',
      // Sticky tab bar offset. In 'content' mode the bar's scroll context is
      // `.t-admin-shell__main-stack`, which already sits below the header, so
      // the offset is 0; in 'wrapper' mode the whole shell scrolls and the bar
      // must clear the sticky header. See TAdminTabs `.t-admin-tabs--fixed`.
      '--tnzi-admin-tab-sticky-top':
        themeStore.scrollMode === 'wrapper' ? themeStore.headerHeight + 'px' : '0px',
    }"
  >
    <!-- vertical-mix mouse region — soybean parity.
         Single mouseleave fires for the *entire* rail+sub-sider strip,
         not per-aside, so the drawer doesn't close while the cursor
         crosses the gap between them. NO mouseenter — the contract is
         click-driven, hovering must not change drawer state. -->
    <div
      v-if="effectiveMode === 'vertical-mix' && !appStore.isMobile && showMainSider"
      class="t-admin-shell__mix-region"
      @mouseleave="onMixMouseleave"
    >
      <aside class="t-admin-shell__sider" :style="{ width: `${primarySiderWidth}px` }">
        <TAdminMixNavRail
          :menus="menuCtx.firstLevelMenus.value"
          :active-menu-key="visibleFirstLevelKey"
          :inverted="siderInverted"
          :is-mini="appStore.siderCollapse"
          :theme-color="primaryColor"
          :on-toggle-collapse="() => appStore.toggleSiderCollapse()"
          @select="onMixNavRailSelect"
        >
          <template #header>
            <slot name="sider-header">
              <TSystemLogo :icon="sider.brandIcon" :icon-size="32" layout="icon-only" :title="''" />
            </slot>
          </template>
        </TAdminMixNavRail>
      </aside>

      <!-- Sub-sider: outer wrapper reserves layout space ONLY when pinned
           (matches soybean's `mixSiderFixed && hasChildMenus` width
           expression on the relative div). Unpinned, the inner aside
           floats absolutely over the main content. Width transitions
           give the slide-in / slide-out motion. -->
      <div
        v-if="showSubSider"
        class="t-admin-shell__sub-sider-wrapper"
        :style="{ width: `${mixWrapperWidth}px` }"
      >
        <aside
          class="t-admin-shell__sub-sider"
          :class="{ 't-admin-shell__sub-sider--open': showMixDrawer }"
          :style="{ width: `${mixInnerWidth}px` }"
        >
          <header class="t-admin-shell__sub-sider-header">
            <h2 class="t-admin-shell__sub-sider-title">{{ title }}</h2>
            <TPinToggler
              :pinned="appStore.mixSiderFixed"
              @toggle="appStore.toggleMixSiderFixed"
            />
          </header>
          <div class="t-admin-shell__sub-sider-body">
            <NMenu
              :options="mixChildOptions"
              :value="currentRouteName || undefined"
              :expanded-keys="mixDrawerExpandedKeys"
              mode="vertical"
              :indent="18"
              @update:value="onMixChildSelect"
            />
          </div>
        </aside>
      </div>
    </div>

    <!-- Full-height primary sider — vertical mode only. vertical-mix is the
         mouse-region above; the hybrid (top-hybrid-header-first) children
         sider is rendered INSIDE the main column below the header (soybean
         horizontal-mix parity) so the header spans the full width instead of
         being shoved right by a full-height sider. -->
    <aside
      v-else-if="showMainSider && effectiveMode !== 'top-hybrid-header-first'"
      class="t-admin-shell__sider"
      :style="{ width: `${primarySiderWidth}px` }"
    >
      <TAdminSidebar
        :mode="sidebarPresentationMode"
        :width="primarySiderWidth"
        :collapsed-width="sider.collapsedWidth ?? themeStore.siderCollapsedWidth"
        :brand="sider.brand ?? title"
        :brand-subtitle="sider.brandSubtitle"
        :brand-icon="sider.brandIcon"
        :inverted="siderInverted"
        :items="siderItems"
        :hide-header="shouldRenderHeaderLogo"
        @menu-select="onMenuSelect"
      >
        <template v-if="$slots['sider-header']" #header>
          <slot name="sider-header" />
        </template>
        <template v-if="$slots['sider-footer']" #footer>
          <slot name="sider-footer" />
        </template>
      </TAdminSidebar>
    </aside>

    <!-- Main column -->
    <div class="t-admin-shell__main">
      <TAdminHeader
        v-if="headerVisible"
        :title="title"
        :fixed="header.fixed ?? themeStore.fixedHeader"
        :show-toggler="header.showToggler ?? true"
        :show-search="header.showSearch ?? themeStore.globalSearchVisible"
        :show-fullscreen="header.showFullscreen ?? themeStore.fullscreenVisible"
        :show-theme-btn="header.showThemeBtn ?? true"
        :show-theme-schema-btn="header.showThemeSchemaBtn ?? themeStore.themeSchemaVisible"
        :show-lang-switch="header.showLangSwitch ?? themeStore.multilingualVisible"
        :show-reload="header.showReload ?? themeStore.reloadVisible"
        @open-search="onOpenSearch"
        @open-theme-drawer="onOpenThemeDrawer"
        @locale-change="onLocaleChange"
      >
        <!-- Phase H3 B3: in horizontal / top-hybrid modes the main
             sider is gone (or rendered narrow), so the brand logo
             has nowhere to live unless we surface it in the header.
             Default to a built-in TSystemLogo here when the consumer
             hasn't supplied #header-logo; vertical / vertical-mix
             leave it to the sider header so we skip. -->
        <template #logo>
          <slot name="header-logo">
            <TSystemLogo
              v-if="shouldRenderHeaderLogo"
              :icon="sider.brandIcon"
              :title="sider.brand ?? title"
              :icon-size="28"
              layout="full"
              class="t-admin-shell__header-logo"
            />
          </slot>
        </template>
        <!-- Top menu lives inside the header for horizontal & hybrid modes -->
        <template v-if="topMenuVariant" #menu>
          <TAdminTopMenu
            :mode="topMenuVariant"
            :items="topItems"
            :active-key="topActiveKey"
            @menu-select="onTopMenuSelect"
          />
        </template>
        <!-- Breadcrumb is suppressed in horizontal / hybrid modes: the top
             menu already lives in the header and provides the navigation
             context, so a breadcrumb in the left region only steals
             horizontal space from (and shifts) the menu. Keep it for the
             vertical / vertical-mix modes where the header has no top menu. -->
        <template
          v-if="
            header.showBreadcrumb !== false &&
            themeStore.breadcrumbVisible &&
            !topMenuVariant
          "
          #breadcrumb
        >
          <slot name="header-breadcrumb" />
        </template>
        <template v-if="$slots['header-notification']" #notification>
          <slot name="header-notification" />
        </template>
        <!-- TChatHost lives in a dedicated #chat slot so it renders even
             when the consumer overrides #header-user (e.g. AdminShellRoot
             passes its own TAdminUserAvatar via #header-user, which would
             otherwise replace the #user default and hide TChatHost). -->
        <template #chat>
          <TChatHost v-if="builtinChat !== false" />
        </template>
        <template #user>
          <!-- Phase H2 B4: default to a built-in TAdminUserAvatar so
               consumers get a working user dropdown out of the box.
               Override via the `#header-user` slot when they want
               custom content. -->
          <slot name="header-user">
            <TAdminUserAvatar v-if="builtinUserAvatar !== false" />
          </slot>
        </template>
      </TAdminHeader>

      <!-- Main body. In hybrid (top-hybrid-header-first) mode this is a ROW
           that hosts the children sider BELOW the now-full-width header
           (soybean horizontal-mix parity). In every other mode it's a plain
           column that simply stacks tabs + content + footer. -->
      <div
        class="t-admin-shell__main-body"
        :class="{
          't-admin-shell__main-body--hybrid':
            effectiveMode === 'top-hybrid-header-first' && showMainSider,
        }"
      >
        <!-- Hybrid children sider — sits under the full-width header, to the
             left of the content. Header logo is rendered in the header so we
             always hide the sider's own header. -->
        <aside
          v-if="effectiveMode === 'top-hybrid-header-first' && showMainSider"
          class="t-admin-shell__hybrid-sider"
          :style="{ width: `${primarySiderWidth}px` }"
        >
          <TAdminSidebar
            mode="vertical"
            :width="primarySiderWidth"
            :collapsed-width="sider.collapsedWidth ?? themeStore.siderCollapsedWidth"
            :inverted="siderInverted"
            :items="siderItems"
            :hide-header="true"
            @menu-select="onMenuSelect"
          >
            <template v-if="$slots['sider-footer']" #footer>
              <slot name="sider-footer" />
            </template>
          </TAdminSidebar>
        </aside>

        <div class="t-admin-shell__main-stack">
          <TAdminTabs
            v-if="tabsVisible"
            :fixed="themeStore.fixedTab"
            :close-by-middle-click="tabs.closeByMiddleClick ?? true"
            :draggable="tabs.draggable ?? true"
            :show-reload="tabs.showReload ?? true"
            @tab-click="(tab) => emit('tabClick', tab)"
          />

          <TAdminContent :transition-name="resolvedTransition">
            <slot />
          </TAdminContent>

          <TAdminFooter
            v-if="footerVisible"
            :fixed="themeStore.fixedFooter"
            :copyright="footer.copyright"
            :links="footer.links"
          >
            <template v-if="$slots['footer']" #default>
              <slot name="footer" />
            </template>
          </TAdminFooter>
        </div>
      </div>
    </div>

    <!-- Watermark overlay (renders nothing when disabled) -->
    <TAdminWatermark />

    <!-- Mobile drawer (teleported) -->
    <Teleport to="body">
      <div
        v-if="drawerOpen"
        class="t-admin-shell__drawer-backdrop"
        @click="closeMobileDrawer"
      />
      <aside
        v-if="appStore.isMobile && props.sider?.visible !== false"
        class="t-admin-shell__drawer"
        :class="{ 't-admin-shell__drawer--open': drawerOpen }"
        :style="{ width: `${mobileDrawerWidth}px` }"
        :aria-hidden="!drawerOpen"
        role="dialog"
        aria-modal="true"
        aria-label="Navigation"
      >
        <div v-if="$slots['mobile-drawer-header']" class="t-admin-shell__drawer-header">
          <slot name="mobile-drawer-header" />
        </div>
        <TAdminSidebar
          mode="vertical"
          :width="sider.width ?? 260"
          :collapsed-width="sider.collapsedWidth ?? themeStore.siderCollapsedWidth"
          :brand="sider.brand ?? title"
          :brand-subtitle="sider.brandSubtitle"
          :brand-icon="sider.brandIcon"
          :inverted="siderInverted"
          @menu-select="onMenuSelect"
        />
      </aside>
    </Teleport>

    <!-- Phase H1 I1: built-in global search modal. Cmd/Ctrl+K opens
         it; consumers can opt out via :builtinSearch="false". -->
    <TGlobalSearch
      v-if="builtinSearch !== false"
      v-model:show="globalSearchVisible"
      @select="onSelectSearchResult"
    />
    <!-- Phase H4 L6: floating back-top button (bottom-right). Hidden
         until scrolled past 200px. Consumers can opt out via
         :builtinBackTop="false". -->
    <TBackTop v-if="builtinBackTop !== false" />
  </div>
</template>

<style scoped>
.t-admin-shell {
  display: flex;
  width: 100%;
  height: 100vh;
  min-height: 0;
  background-color: var(--tnzi-layout-bg, #f5f7fa);
}

.t-admin-shell__sider {
  flex-shrink: 0;
  /* Flexbox children default to `min-width: auto` (= min-content), which
     would let inner labels (e.g. the mix-rail menu labels with
     visibility:hidden but still occupying intrinsic width) push the
     sider past the inline-style width and ignore the collapsed-width
     setter. Force min-width:0 so inline width truly clamps the column. */
  min-width: 0;
  height: 100%;
  transition: width var(--tnzi-admin-motion-duration-base, 0.25s) var(--tnzi-admin-motion-ease-in-out, ease);
}

/* vertical-mix mouse region: a flex strip that owns both the rail and
   the sub-sider so a single mouseleave triggers the drawer close — see
   the matching template comment above. */
.t-admin-shell__mix-region {
  display: flex;
  flex-shrink: 0;
  height: 100%;
}

/* sub-sider wrapper — reserves layout width only when pinned.
   Mirrors soybean's `<div class="relative h-full transition-width-300"
   :style="{ width: fixed && hasChildren ? width : '0px' }">` pattern. */
.t-admin-shell__sub-sider-wrapper {
  position: relative;
  flex-shrink: 0;
  height: 100%;
  transition: width var(--tnzi-admin-motion-duration-base, 0.25s) var(--tnzi-admin-motion-ease-in-out, ease);
}

/* sub-sider — absolutely positioned inside the wrapper so its open/close
   animation floats over the main content instead of pushing it. Width
   transitions independently from the wrapper. */
.t-admin-shell__sub-sider {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
  background: var(--tnzi-admin-sider-bg, var(--tnzi-container-bg, #ffffff));
  border-right: 1px solid var(--tnzi-border, #e5e7eb);
  overflow: hidden;
  white-space: nowrap;
  box-shadow: var(--tnzi-shadow-sider, 2px 0 8px 0 rgb(29 35 41 / 5%));
  /* Must outrank TAdminHeader (z-index 80) so the drawer's own header
     row — which sits at y=0 to align with the layout header line —
     isn't clipped by the (sticky) page header. soybean avoids this by
     teleporting the drawer inside the sider container, which sits in
     a separate stacking context above the main column; we replicate
     the effect with an explicit z-index. */
  z-index: var(--tnzi-admin-z-sub-sider, 90);
  transition:
    width var(--tnzi-admin-motion-duration-base, 0.25s) var(--tnzi-admin-motion-ease-in-out, ease);
}
.t-admin-shell__sub-sider-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 12px;
  height: var(--tnzi-admin-header-height, 56px);
  flex-shrink: 0;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.t-admin-shell__sub-sider-title {
  /* Phase G — soybean uses font-weight 700 + primary colour for the
     mix-drawer title (`text-16px text-primary font-bold`). */
  margin: 0;
  font-size: 16px;
  font-weight: 700;
  color: var(--tnzi-primary, #6366f1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-admin-shell__sub-sider-body {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  /* Scrollbar styling delegated to styles/polish.css macOS-style overlay rules. */
}

.t-admin-shell__main {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

/* Main body wrapper. The header always sits above this in
   `.t-admin-shell__main`; this wrapper holds the rest (tabs/content/footer).
   Column by default; hybrid flips it to a row so the children sider sits
   beside the content, under the full-width header (soybean horizontal-mix). */
.t-admin-shell__main-body {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}
.t-admin-shell__main-body--hybrid {
  flex-direction: row;
}

/* Hybrid children sider — a vertical menu column under the full-width
   header. Same chrome (bg + right border) as the primary sider; width is
   set inline so it animates on collapse. */
.t-admin-shell__hybrid-sider {
  flex-shrink: 0;
  min-width: 0;
  height: 100%;
  background: var(--tnzi-admin-sider-bg, var(--tnzi-container-bg, #ffffff));
  border-right: 1px solid var(--tnzi-border, #e5e7eb);
  transition: width var(--tnzi-admin-motion-duration-base, 0.25s) var(--tnzi-admin-motion-ease-in-out, ease);
}

/* Tabs + content + footer stack — to the right of the hybrid sider, or
   full-width in every other mode. */
.t-admin-shell__main-stack {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

/* Phase H1 A2: scrollMode='wrapper' lifts the scroll to the outer
   shell so the whole page (header + tabs + content + footer) scrolls
   together. When fixedHeader/fixedTab/fixedFooter is on, they
   `position: sticky` to the viewport. The default scrollMode='content'
   keeps the scroll inside `.t-admin-shell__main > content` only.
   Mirrors soybean's `theme.layout.scrollMode` switch. */
.t-admin-shell[data-scroll-mode='wrapper'] {
  height: auto;
  min-height: 100vh;
  overflow-y: auto;
}
.t-admin-shell[data-scroll-mode='wrapper'] .t-admin-shell__main,
.t-admin-shell[data-scroll-mode='wrapper'] .t-admin-shell__main-body,
.t-admin-shell[data-scroll-mode='wrapper'] .t-admin-shell__main-stack {
  overflow: visible;
}
.t-admin-shell[data-scroll-mode='wrapper'] :deep(.t-admin-content) {
  overflow: visible;
}

.t-admin-shell--full-content :deep(.t-admin-header),
.t-admin-shell--full-content :deep(.t-admin-tabs),
.t-admin-shell--full-content :deep(.t-admin-footer),
.t-admin-shell--full-content .t-admin-shell__sider,
.t-admin-shell--full-content .t-admin-shell__hybrid-sider,
.t-admin-shell--full-content .t-admin-shell__sub-sider-wrapper,
.t-admin-shell--full-content .t-admin-shell__sub-sider,
.t-admin-shell--full-content .t-admin-shell__mix-region {
  display: none;
}

/* Phase B (next-up): invertSider is no longer driven by this wrapper hack —
   TAdminSidebar will accept an `:inverted` prop and propagate it to the
   inner <NMenu :inverted> so menu items also flip. The wrapper class
   lingers only as a state marker for any consumer-side custom overrides. */

.t-admin-shell__drawer-backdrop {
  position: fixed;
  inset: 0;
  background-color: var(--tnzi-admin-overlay-bg, rgba(0, 0, 0, 0.45));
  z-index: var(--tnzi-admin-z-drawer-backdrop, 1000);
}

.t-admin-shell__drawer {
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  max-width: 80vw;
  background-color: var(--tnzi-container-bg, #ffffff);
  z-index: var(--tnzi-admin-z-drawer, 1001);
  transform: translateX(-100%);
  transition: transform var(--tnzi-admin-motion-duration-base, 0.25s) var(--tnzi-admin-motion-ease-out, ease);
  display: flex;
  flex-direction: column;
  box-shadow: var(--tnzi-shadow-drawer, 0 8px 24px rgba(0, 0, 0, 0.12));
}

.t-admin-shell__drawer--open {
  transform: translateX(0);
}

.t-admin-shell__drawer-header {
  flex-shrink: 0;
  padding: 12px 16px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
</style>
