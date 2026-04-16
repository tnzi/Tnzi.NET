<script setup lang="ts">
import { computed } from 'vue'
import TAdminSidebar from './TAdminSidebar.vue'
import TAdminHeader from './TAdminHeader.vue'
import TAdminTabs from './TAdminTabs.vue'
import TAdminContent from './TAdminContent.vue'
import TAdminFooter from './TAdminFooter.vue'
import type { TAdminFooterLink } from './TAdminFooter.vue'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'

type ShellMode = 'vertical' | 'vertical-mix' | 'horizontal'

interface SiderConfig {
  visible?: boolean
  width?: number
  subWidth?: number
  collapsedWidth?: number
  fixed?: boolean
}

interface HeaderConfig {
  visible?: boolean
  fixed?: boolean
  showToggler?: boolean
  showBreadcrumb?: boolean
  showSearch?: boolean
  showFullscreen?: boolean
  showThemeBtn?: boolean
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
  transition?: 'fade' | 'slide-left' | 'slide-right' | 'zoom' | 'none'
}

interface FooterConfig {
  visible?: boolean
  copyright?: string
  links?: TAdminFooterLink[]
}

interface Props {
  mode?: ShellMode
  title?: string
  sider?: SiderConfig
  header?: HeaderConfig
  tabs?: TabsConfig
  content?: ContentConfig
  footer?: FooterConfig
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'vertical',
  title: 'Tnzi Admin',
  sider: () => ({}),
  header: () => ({}),
  tabs: () => ({}),
  content: () => ({}),
  footer: () => ({}),
})

const emit = defineEmits<{
  openSearch: []
  openThemeDrawer: []
  localeChange: [locale: 'en' | 'zh-cn']
  menuSelect: [menu: AdminMenuItem]
}>()

const appStore = useAdminAppStore()

const effectiveMode = computed<ShellMode>(() =>
  appStore.isMobile ? 'vertical' : props.mode,
)

const showMainSider = computed<boolean>(() => {
  if (props.sider.visible === false) return false
  if (appStore.isMobile) return false
  if (effectiveMode.value === 'horizontal') return false
  return true
})

const showSubSider = computed<boolean>(
  () => effectiveMode.value === 'vertical-mix' && !appStore.isMobile,
)

const tabsVisible = computed<boolean>(() => props.tabs.visible !== false)
const footerVisible = computed<boolean>(() => props.footer.visible !== false)
const headerVisible = computed<boolean>(() => props.header.visible !== false)

function onMenuSelect(menu: AdminMenuItem): void {
  emit('menuSelect', menu)
}

function onOpenSearch(): void {
  emit('openSearch')
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

const drawerOpen = computed<boolean>(
  () => appStore.isMobile && !appStore.siderCollapse,
)
</script>

<template>
  <div
    class="t-admin-shell"
    :class="{ 't-admin-shell--full-content': appStore.fullContent }"
    :data-mode="effectiveMode"
  >
    <!-- Main sider (vertical / vertical-mix desktop) -->
    <aside
      v-if="showMainSider"
      class="t-admin-shell__sider"
      :style="{
        width: `${
          appStore.siderCollapse
            ? (sider.collapsedWidth ?? 64)
            : (sider.width ?? 220)
        }px`,
      }"
    >
      <TAdminSidebar
        :mode="effectiveMode === 'vertical-mix' ? 'vertical-mix' : 'vertical'"
        :width="sider.width ?? 220"
        :collapsed-width="sider.collapsedWidth ?? 64"
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

    <!-- Sub-sider (vertical-mix only) -->
    <aside
      v-if="showSubSider"
      class="t-admin-shell__sub-sider"
      :style="{ width: `${sider.subWidth ?? 180}px` }"
    >
      <TAdminSidebar
        mode="vertical"
        :width="sider.subWidth ?? 180"
        :collapsed-width="sider.collapsedWidth ?? 64"
        @menu-select="onMenuSelect"
      />
    </aside>

    <!-- Main column -->
    <div class="t-admin-shell__main">
      <TAdminHeader
        v-if="headerVisible"
        :title="title"
        :fixed="header.fixed ?? true"
        :show-toggler="header.showToggler ?? true"
        :show-search="header.showSearch ?? true"
        :show-fullscreen="header.showFullscreen ?? true"
        :show-theme-btn="header.showThemeBtn ?? true"
        :show-lang-switch="header.showLangSwitch ?? true"
        :show-reload="header.showReload ?? true"
        @open-search="onOpenSearch"
        @open-theme-drawer="onOpenThemeDrawer"
        @locale-change="onLocaleChange"
      >
        <template v-if="$slots['header-logo']" #logo>
          <slot name="header-logo" />
        </template>
        <template v-if="header.showBreadcrumb !== false" #breadcrumb>
          <slot name="header-breadcrumb" />
        </template>
        <template v-if="$slots['header-notification']" #notification>
          <slot name="header-notification" />
        </template>
        <template v-if="$slots['header-user']" #user>
          <slot name="header-user" />
        </template>
      </TAdminHeader>

      <TAdminTabs
        v-if="tabsVisible"
        :close-by-middle-click="tabs.closeByMiddleClick ?? true"
        :draggable="tabs.draggable ?? true"
        :show-reload="tabs.showReload ?? true"
      />

      <TAdminContent :transition-name="content.transition ?? 'fade'">
        <slot />
      </TAdminContent>

      <TAdminFooter
        v-if="footerVisible"
        :copyright="footer.copyright"
        :links="footer.links"
      >
        <template v-if="$slots['footer']" #default>
          <slot name="footer" />
        </template>
      </TAdminFooter>
    </div>

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
        :style="{ width: `${props.sider?.width ?? 260}px` }"
        :aria-hidden="!drawerOpen"
      >
        <div v-if="$slots['mobile-drawer-header']" class="t-admin-shell__drawer-header">
          <slot name="mobile-drawer-header" />
        </div>
        <TAdminSidebar
          mode="vertical"
          :width="sider.width ?? 260"
          :collapsed-width="sider.collapsedWidth ?? 64"
          @menu-select="onMenuSelect"
        />
      </aside>
    </Teleport>
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

.t-admin-shell__sider,
.t-admin-shell__sub-sider {
  flex-shrink: 0;
  height: 100%;
  transition: width 0.2s ease;
}

.t-admin-shell__sub-sider {
  border-right: 1px solid var(--tnzi-border-color, #e5e7eb);
}

.t-admin-shell__main {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

.t-admin-shell--full-content :deep(.t-admin-header),
.t-admin-shell--full-content :deep(.t-admin-tabs),
.t-admin-shell--full-content :deep(.t-admin-footer),
.t-admin-shell--full-content .t-admin-shell__sider,
.t-admin-shell--full-content .t-admin-shell__sub-sider {
  display: none;
}

.t-admin-shell__drawer-backdrop {
  position: fixed;
  inset: 0;
  background-color: var(--tnzi-overlay-bg, rgba(0, 0, 0, 0.45));
  z-index: 1000;
}

.t-admin-shell__drawer {
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  /* width set inline based on sider.width prop */
  max-width: 80vw;
  background-color: var(--tnzi-surface, #ffffff);
  z-index: 1001;
  transform: translateX(-100%);
  transition: transform 0.25s ease;
  display: flex;
  flex-direction: column;
}

.t-admin-shell__drawer--open {
  transform: translateX(0);
}

.t-admin-shell__drawer-header {
  flex-shrink: 0;
  padding: 12px 16px;
  border-bottom: 1px solid var(--tnzi-border-color, #e5e7eb);
}
</style>
