<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  NDrawer,
  NDrawerContent,
  NTabs,
  NTab,
  NTabPane,
  NColorPicker,
  NSwitch,
  NInput,
  NInputNumber,
  NButton,
  NSelect,
  NPopconfirm,
  NDivider,
  NTooltip,
  useMessage,
} from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useTheme, type ThemeContext, type ThemeColors } from '@tnzi/ui'
import {
  useAdminThemeStore,
  type AdminLayoutMode,
  type PageTransition,
  type TabStyle,
} from '../../stores/useAdminThemeStore'
import TLayoutModeCard from './TLayoutModeCard.vue'
import {
  copySnapshotToClipboard,
  downloadSnapshot,
  parseSnapshot,
  snapshotToJson,
  type AdminThemeSnapshot,
} from '../../theme/admin-config'
import { useBreakpoint } from '../../headless/useBreakpoint'

interface ThemePreset {
  name: string
  primary: string
  layoutMode?: AdminLayoutMode
  mode?: 'light' | 'dark'
}

interface Props {
  show: boolean
  themeContext?: ThemeContext
  presets?: ThemePreset[]
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  themeContext: undefined,
  presets: undefined,
  translate: undefined,
})

const emit = defineEmits<{
  'update:show': [value: boolean]
}>()

const PRESET_COLORS: string[] = [
  '#2080F0', // Naive blue (default)
  '#1677FF', // Ant blue
  '#7C3AED', // Violet
  '#06B6D4', // Cyan
  '#10B981', // Emerald
  '#22C55E', // Green
  '#EAB308', // Yellow
  '#F59E0B', // Amber
  '#F97316', // Orange
  '#EF4444', // Red
  '#EC4899', // Pink
  '#6B7280', // Gray
]

const DEFAULT_PRESETS: ThemePreset[] = PRESET_COLORS.map((c) => ({
  name: c,
  primary: c,
}))

const COLOR_ROLES: Array<{ role: keyof ThemeColors; key: string }> = [
  { role: 'primary', key: 'admin.theme.appearance.primaryColor' },
  { role: 'info', key: 'admin.theme.appearance.infoColor' },
  { role: 'success', key: 'admin.theme.appearance.successColor' },
  { role: 'warning', key: 'admin.theme.appearance.warningColor' },
  { role: 'error', key: 'admin.theme.appearance.errorColor' },
]

const LAYOUT_MODES: AdminLayoutMode[] = [
  'vertical',
  'horizontal',
  'vertical-mix',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
]

const LAYOUT_LABEL_KEY: Record<AdminLayoutMode, string> = {
  'vertical': 'admin.theme.layout.vertical',
  'horizontal': 'admin.theme.layout.horizontal',
  'vertical-mix': 'admin.theme.layout.verticalMix',
  'vertical-hybrid-header-first': 'admin.theme.layout.verticalHybridHeaderFirst',
  'top-hybrid-sidebar-first': 'admin.theme.layout.topHybridSidebarFirst',
  'top-hybrid-header-first': 'admin.theme.layout.topHybridHeaderFirst',
}

const TRANSITION_OPTIONS: PageTransition[] = [
  'fade',
  'fade-slide',
  'fade-bottom',
  'fade-scale',
  'zoom-fade',
  'zoom-out',
  'slide-left',
  'slide-right',
  'zoom',
  'none',
]
const TRANSITION_LABEL_KEY: Record<PageTransition, string> = {
  fade: 'admin.theme.general.transitionFade',
  'fade-slide': 'admin.theme.general.transitionFadeSlide',
  'fade-bottom': 'admin.theme.general.transitionFadeBottom',
  'fade-scale': 'admin.theme.general.transitionFadeScale',
  'zoom-fade': 'admin.theme.general.transitionZoomFade',
  'zoom-out': 'admin.theme.general.transitionZoomOut',
  'slide-left': 'admin.theme.general.transitionSlideLeft',
  'slide-right': 'admin.theme.general.transitionSlideRight',
  zoom: 'admin.theme.general.transitionZoom',
  none: 'admin.theme.general.transitionNone',
}

const TAB_STYLE_OPTIONS: TabStyle[] = ['chrome', 'button', 'slider']
const TAB_STYLE_LABEL_KEY: Record<TabStyle, string> = {
  chrome: 'admin.theme.layout.tabStyleChrome',
  button: 'admin.theme.layout.tabStyleButton',
  slider: 'admin.theme.layout.tabStyleSlider',
}

const ctx = useTheme(props.themeContext)
const themeStore = useAdminThemeStore()
const message = useMessage()
const bp = useBreakpoint()

// Drawer width adapts to viewport: 420 desktop, 100vw on phones (<sm).
// Tablets (md band, 640-1023) keep 360 so list of long labels still fits.
const drawerWidth = computed<number | string>(() => {
  if (bp.isXs.value) return '100vw'
  if (bp.isSm.value) return 360
  return 420
})

// NOTE: the layout/preset grids are 3-col by default, dropping to 2-col on
// phones. This is done via a scoped `@media` query rather than a v-bind CSS
// var — NDrawer teleports its content to <body>, and Vue's `v-bind()` writes
// the var onto the component root (which stays in place), so teleported nodes
// can't resolve it and `repeat(var(--x), 1fr)` collapses to a single column.
// Scoped data-v selectors DO follow teleported nodes, so a media query works.

// Phase C: layout modes that host the menu in the header. The shell forces
// headerVisible=true in these modes; mirror that here so the drawer's
// "Show header" toggle disables itself with the correct state instead of
// silently no-op-ing.
const HEADER_HOSTED_MODES: AdminLayoutMode[] = [
  'horizontal',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
]
const headerVisibilityForcedOn = computed(() =>
  HEADER_HOSTED_MODES.includes(themeStore.layoutMode),
)

// I.7.10 — 5-tab → 4-tab consolidation: watermark settings merged into the
// `general` tab (matches soybean's drawer layout).
const activeTab = ref<'appearance' | 'layout' | 'general' | 'preset'>('appearance')
const importBuffer = ref('')

const resolvedPresets = computed<ThemePreset[]>(() => props.presets ?? DEFAULT_PRESETS)
/** Hex string list fed to NColorPicker `:swatches`. Soybean's theme-color.vue
 *  uses the same pattern — putting preset colours INSIDE the picker is more
 *  discoverable than a separate row of buttons. */
const presetSwatches = computed<string[]>(() => resolvedPresets.value.map((p) => p.primary))

function translate(key: string): string {
  return props.translate ? props.translate(key) : key
}

// ─── Appearance handlers ──────────────────────────────────────────────────

function onSetColor(role: keyof ThemeColors, value: string | null): void {
  if (!value) return
  ctx.setColor(role, value)
  // Phase F — when infoFollowPrimary is on, primary changes propagate to
  // info too (mirrors soybean's `theme-color.vue:67-69` "Info follows
  // primary" checkbox behaviour).
  if (role === 'primary' && themeStore.infoFollowPrimary) {
    ctx.setColor('info' as keyof ThemeColors, value)
  }
}

function applyPresetColor(color: string): void {
  ctx.setColor('primary', color)
}

function setMode(mode: 'light' | 'dark' | 'auto'): void {
  ctx.setMode(mode)
}

// ─── Layout handlers ──────────────────────────────────────────────────────

function selectLayoutMode(mode: AdminLayoutMode): void {
  themeStore.setLayoutMode(mode)
}

// ─── Preset handlers ──────────────────────────────────────────────────────

function buildSnapshot(): AdminThemeSnapshot {
  return {
    version: 1,
    exportedAt: new Date().toISOString(),
    admin: {
      layoutMode: themeStore.layoutMode,
      headerVisible: themeStore.headerVisible,
      tabVisible: themeStore.tabVisible,
      footerVisible: themeStore.footerVisible,
      breadcrumbVisible: themeStore.breadcrumbVisible,
      siderWidth: themeStore.siderWidth,
      siderCollapsedWidth: themeStore.siderCollapsedWidth,
      mixSiderWidth: themeStore.mixSiderWidth,
      headerHeight: themeStore.headerHeight,
      tabHeight: themeStore.tabHeight,
      tabStyle: themeStore.tabStyle,
      pageTransition: themeStore.pageTransition,
      pageAnimate: themeStore.pageAnimate,
      invertSider: themeStore.invertSider,
      fixedHeader: themeStore.fixedHeader,
      fixedTab: themeStore.fixedTab,
      fixedFooter: themeStore.fixedFooter,
      watermark: { ...themeStore.watermark },
      recommendColor: themeStore.recommendColor,
      infoFollowPrimary: themeStore.infoFollowPrimary,
      tabCache: themeStore.tabCache,
      breadcrumbShowIcon: themeStore.breadcrumbShowIcon,
      multilingualVisible: themeStore.multilingualVisible,
      globalSearchVisible: themeStore.globalSearchVisible,
      grayscale: themeStore.grayscale,
      colourWeakness: themeStore.colourWeakness,
      closeTabByMiddleClick: themeStore.closeTabByMiddleClick,
      tabScrollAnimation: themeStore.tabScrollAnimation,
      scrollMode: themeStore.scrollMode,
      mixCollapsedWidth: themeStore.mixCollapsedWidth,
      mixChildMenuWidth: themeStore.mixChildMenuWidth,
      autoSelectFirstMenu: themeStore.autoSelectFirstMenu,
      themeRadius: themeStore.themeRadius,
      footerHeight: themeStore.footerHeight,
    },
    ui: {
      mode: ctx.settings.value.mode,
      colors: { ...ctx.settings.value.colors },
    },
  }
}

async function onCopy(): Promise<void> {
  const ok = await copySnapshotToClipboard(buildSnapshot())
  if (ok) {
    message.success(translate('admin.theme.preset.clipboardOk'))
  } else {
    message.warning(translate('admin.theme.preset.clipboardFail'))
  }
}

function onDownload(): void {
  downloadSnapshot(buildSnapshot())
}

function onImport(): void {
  if (!importBuffer.value.trim()) return
  let snapshot: AdminThemeSnapshot
  try {
    snapshot = parseSnapshot(importBuffer.value)
  } catch (err) {
    const text = err instanceof Error ? err.message : ''
    message.error(
      text.includes('snapshot')
        ? translate('admin.theme.preset.invalidSnapshot')
        : translate('admin.theme.preset.invalidJson'),
    )
    return
  }
  applySnapshot(snapshot)
  importBuffer.value = ''
  message.success(translate('admin.theme.preset.apply'))
}

/**
 * Open the native file picker and populate `importBuffer` with the
 * selected .json file's contents. The user then clicks "Apply" to
 * commit (same code path as paste-import, so all the validation /
 * error toasts stay shared).
 */
function onChooseFile(): void {
  if (typeof document === 'undefined') return
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.json,application/json'
  input.onchange = (e: Event) => {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = () => {
      const text = typeof reader.result === 'string' ? reader.result : ''
      importBuffer.value = text
    }
    reader.onerror = () => {
      message.error(translate('admin.theme.preset.invalidJson'))
    }
    reader.readAsText(file)
  }
  input.click()
}

function applySnapshot(snapshot: AdminThemeSnapshot): void {
  // Apply admin state
  themeStore.setLayoutMode(snapshot.admin.layoutMode)
  themeStore.setHeaderVisible(snapshot.admin.headerVisible)
  themeStore.setTabVisible(snapshot.admin.tabVisible)
  themeStore.setFooterVisible(snapshot.admin.footerVisible)
  themeStore.setBreadcrumbVisible(snapshot.admin.breadcrumbVisible)
  themeStore.setSiderWidth(snapshot.admin.siderWidth)
  themeStore.setSiderCollapsedWidth(snapshot.admin.siderCollapsedWidth)
  themeStore.setMixSiderWidth(snapshot.admin.mixSiderWidth)
  themeStore.setHeaderHeight(snapshot.admin.headerHeight)
  themeStore.setTabHeight(snapshot.admin.tabHeight)
  themeStore.setTabStyle(snapshot.admin.tabStyle)
  themeStore.setPageTransition(snapshot.admin.pageTransition)
  themeStore.setPageAnimate(snapshot.admin.pageAnimate)
  if (themeStore.invertSider !== snapshot.admin.invertSider) themeStore.toggleInvertSider()
  themeStore.setFixedHeader(snapshot.admin.fixedHeader)
  themeStore.setFixedTab(snapshot.admin.fixedTab)
  themeStore.setFixedFooter(snapshot.admin.fixedFooter)
  themeStore.setWatermark(snapshot.admin.watermark)
  // Phase D — defaults preserved if older snapshot is missing the field.
  if (typeof snapshot.admin.recommendColor === 'boolean') {
    themeStore.setRecommendColor(snapshot.admin.recommendColor)
  }
  if (typeof snapshot.admin.infoFollowPrimary === 'boolean') {
    themeStore.setInfoFollowPrimary(snapshot.admin.infoFollowPrimary)
  }
  if (typeof snapshot.admin.tabCache === 'boolean') {
    themeStore.setTabCache(snapshot.admin.tabCache)
  }
  if (typeof snapshot.admin.breadcrumbShowIcon === 'boolean') {
    themeStore.setBreadcrumbShowIcon(snapshot.admin.breadcrumbShowIcon)
  }
  if (typeof snapshot.admin.multilingualVisible === 'boolean') {
    themeStore.setMultilingualVisible(snapshot.admin.multilingualVisible)
  }
  if (typeof snapshot.admin.globalSearchVisible === 'boolean') {
    themeStore.setGlobalSearchVisible(snapshot.admin.globalSearchVisible)
  }
  if (typeof snapshot.admin.grayscale === 'boolean') {
    themeStore.setGrayscale(snapshot.admin.grayscale)
  }
  if (typeof snapshot.admin.colourWeakness === 'boolean') {
    themeStore.setColourWeakness(snapshot.admin.colourWeakness)
  }
  if (typeof snapshot.admin.closeTabByMiddleClick === 'boolean') {
    themeStore.setCloseTabByMiddleClick(snapshot.admin.closeTabByMiddleClick)
  }
  if (typeof snapshot.admin.tabScrollAnimation === 'boolean') {
    themeStore.setTabScrollAnimation(snapshot.admin.tabScrollAnimation)
  }
  if (snapshot.admin.scrollMode === 'content' || snapshot.admin.scrollMode === 'wrapper') {
    themeStore.setScrollMode(snapshot.admin.scrollMode)
  }
  if (typeof snapshot.admin.mixCollapsedWidth === 'number') {
    themeStore.setMixCollapsedWidth(snapshot.admin.mixCollapsedWidth)
  }
  if (typeof snapshot.admin.mixChildMenuWidth === 'number') {
    themeStore.setMixChildMenuWidth(snapshot.admin.mixChildMenuWidth)
  }
  if (typeof snapshot.admin.autoSelectFirstMenu === 'boolean') {
    themeStore.setAutoSelectFirstMenu(snapshot.admin.autoSelectFirstMenu)
  }
  if (typeof snapshot.admin.themeRadius === 'number') {
    themeStore.setThemeRadius(snapshot.admin.themeRadius)
  }
  if (typeof snapshot.admin.footerHeight === 'number') {
    themeStore.setFooterHeight(snapshot.admin.footerHeight)
  }
  // Apply ui state
  ctx.setMode(snapshot.ui.mode)
  for (const [role, color] of Object.entries(snapshot.ui.colors)) {
    if (color) ctx.setColor(role as keyof ThemeColors, color)
  }
}

function resetAll(): void {
  ctx.reset()
  themeStore.reset()
  message.success(translate('admin.theme.reset'))
}

function close(): void {
  emit('update:show', false)
}

const snapshotJson = computed(() => snapshotToJson(buildSnapshot()))

defineExpose({ resetAll, applySnapshot, close, buildSnapshot })
</script>

<template>
  <NDrawer
    :show="show"
    :width="drawerWidth"
    placement="right"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <!-- Phase G follow-up: `:native-scrollbar="false"` swaps the
         browser-default scrollbar for Naive UI's NScrollbar (thin grey,
         hidden until hover). Matches soybean's drawer presentation. -->
    <NDrawerContent
      :title="translate('admin.theme.title')"
      closable
      :native-scrollbar="false"
    >
      <NTabs v-model:value="activeTab" type="segment" justify-content="space-evenly">
        <!-- ── Tab 1: Appearance — 3 NDivider groups (Scheme/Color/Radius) ── -->
        <NTabPane
          name="appearance"
          :tab="translate('admin.theme.tabs.appearance')"
        >
          <!-- Group 1: Theme scheme — icon-only segmented tabs, horizontally
               centered. Mirrors soybean's theme-schema.vue (NTabs type=segment
               + SvgIcon per tab, w-214px center via `.i-flex-center`). -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.appearance.mode') }}</NDivider>
          <section class="t-theme-drawer__mode-tabs">
            <NTabs
              :value="ctx.settings.value.mode"
              type="segment"
              size="small"
              class="w-240px"
              @update:value="(m: string | number) => setMode(m as 'light' | 'dark' | 'auto')"
            >
              <NTab name="light">
                <Icon icon="material-symbols:sunny" width="20" height="20" />
              </NTab>
              <NTab name="dark">
                <Icon icon="material-symbols:nightlight-rounded" width="20" height="20" />
              </NTab>
              <NTab name="auto">
                <Icon icon="material-symbols:hdr-auto" width="20" height="20" />
              </NTab>
            </NTabs>
          </section>
          <section
            v-if="!ctx.isDark.value && themeStore.layoutMode.startsWith('vertical')"
            class="t-theme-drawer__row"
          >
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.invertSider') }}</span>
            <NSwitch :value="themeStore.invertSider" @update:value="themeStore.toggleInvertSider" />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.appearance.grayscale') }}</span>
            <NSwitch :value="themeStore.grayscale" @update:value="themeStore.setGrayscale" />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.appearance.colourWeakness') }}</span>
            <NSwitch :value="themeStore.colourWeakness" @update:value="themeStore.setColourWeakness" />
          </section>

          <!-- Group 2: Theme color (recommend toggle + 5 pickers + follow primary).
               Soybean parity: preset swatches are embedded INSIDE NColorPicker
               via `:swatches`, not rendered as a separate row of buttons. That
               removes the duplicate "swatches block above + picker below"
               feel of the previous design. -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.appearance.themeColor') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.appearance.recommendColorOn') }}</span>
            <NSwitch :value="themeStore.recommendColor" @update:value="themeStore.setRecommendColor" />
          </section>
          <div class="t-theme-drawer__color-grid">
            <label
              v-for="role in COLOR_ROLES"
              :key="role.role"
              class="t-theme-drawer__color-row"
            >
              <span class="t-theme-drawer__color-label">{{ translate(role.key) }}</span>
              <NColorPicker
                :value="ctx.settings.value.colors[role.role]"
                :modes="['hex']"
                :show-alpha="false"
                :swatches="presetSwatches"
                size="small"
                class="t-theme-drawer__color-picker"
                @update:value="(v: string | null) => onSetColor(role.role, v)"
              />
            </label>
          </div>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.appearance.infoFollowPrimary') }}</span>
            <NSwitch :value="themeStore.infoFollowPrimary" @update:value="themeStore.setInfoFollowPrimary" />
          </section>

          <!-- Group 3: Radius (number input — soybean parity) -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.appearance.themeRadius') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.appearance.borderRadius') }}</span>
            <NInputNumber
              :value="themeStore.themeRadius"
              size="small"
              :min="0"
              :max="16"
              :step="1"
              class="w-120px"
              @update:value="(v: number | null) => v != null && themeStore.setThemeRadius(v)"
            />
          </section>
        </NTabPane>

        <!-- ── Tab 2: Layout — soybean parity (Mode/Sider/Header/Tab/Footer/Content) ── -->
        <NTabPane
          name="layout"
          :tab="translate('admin.theme.tabs.layout')"
        >
          <!-- Group 1: Layout mode (6 visual cards) -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.layout.mode') }}</NDivider>
          <section class="t-theme-drawer__section">
            <div class="t-theme-drawer__layout-grid">
              <TLayoutModeCard
                v-for="m in LAYOUT_MODES"
                :key="m"
                :mode="m"
                :active="themeStore.layoutMode === m"
                :label="translate(LAYOUT_LABEL_KEY[m])"
                @select="selectLayoutMode"
              />
            </div>
          </section>

          <!-- Group 2: Sider — hide entirely in horizontal mode (no sider exists) -->
          <template v-if="themeStore.layoutMode !== 'horizontal'">
            <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.group.sider') }}</NDivider>
            <section v-if="themeStore.layoutMode === 'vertical'" class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.siderWidth') }}</span>
              <NInputNumber
                :value="themeStore.siderWidth"
                size="small"
                :min="160"
                :max="320"
                :step="4"
                class="w-120px"
                @update:value="(v: number | null) => v != null && themeStore.setSiderWidth(v)"
              />
            </section>
            <section v-if="themeStore.layoutMode === 'vertical'" class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.siderCollapsedWidth') }}</span>
              <NInputNumber
                :value="themeStore.siderCollapsedWidth"
                size="small"
                :min="48"
                :max="100"
                :step="2"
                class="w-120px"
                @update:value="(v: number | null) => v != null && themeStore.setSiderCollapsedWidth(v)"
              />
            </section>
            <section
              v-if="themeStore.layoutMode === 'vertical-mix' || themeStore.layoutMode.includes('hybrid')"
              class="t-theme-drawer__row"
            >
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.mixSiderWidth') }}</span>
              <NInputNumber
                :value="themeStore.mixSiderWidth"
                size="small"
                :min="60"
                :max="140"
                :step="2"
                class="w-120px"
                @update:value="(v: number | null) => v != null && themeStore.setMixSiderWidth(v)"
              />
            </section>
            <section
              v-if="themeStore.layoutMode === 'vertical-mix' || themeStore.layoutMode.includes('hybrid')"
              class="t-theme-drawer__row"
            >
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.mixCollapsedWidth') }}</span>
              <NInputNumber
                :value="themeStore.mixCollapsedWidth"
                size="small"
                :min="48"
                :max="100"
                :step="2"
                class="w-120px"
                @update:value="(v: number | null) => v != null && themeStore.setMixCollapsedWidth(v)"
              />
            </section>
            <section
              v-if="themeStore.layoutMode === 'vertical-mix' || themeStore.layoutMode === 'vertical-hybrid-header-first'"
              class="t-theme-drawer__row"
            >
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.mixChildMenuWidth') }}</span>
              <NInputNumber
                :value="themeStore.mixChildMenuWidth"
                size="small"
                :min="160"
                :max="320"
                :step="4"
                class="w-120px"
                @update:value="(v: number | null) => v != null && themeStore.setMixChildMenuWidth(v)"
              />
            </section>
            <section
              v-if="themeStore.layoutMode.includes('hybrid') || themeStore.layoutMode === 'vertical-mix'"
              class="t-theme-drawer__row"
            >
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.autoSelectFirstMenu') }}</span>
              <NSwitch
                :value="themeStore.autoSelectFirstMenu"
                @update:value="themeStore.setAutoSelectFirstMenu"
              />
            </section>
          </template>

          <!-- Group 3: Header — "show header" toggle removed; in non-vertical
               modes the header hosts the menu (hiding strands the user) and
               in vertical modes there's no reason to hide it. Footer/tab
               keep their visibility switches because they truly are
               optional. -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.group.header') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.headerHeight') }}</span>
            <NInputNumber
              :value="themeStore.headerHeight"
              size="small"
              :min="44"
              :max="80"
              :step="2"
              class="w-120px"
              @update:value="(v: number | null) => v != null && themeStore.setHeaderHeight(v)"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.showBreadcrumb') }}</span>
            <NSwitch :value="themeStore.breadcrumbVisible" @update:value="themeStore.setBreadcrumbVisible" />
          </section>
          <section v-if="themeStore.breadcrumbVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.breadcrumbShowIcon') }}</span>
            <NSwitch :value="themeStore.breadcrumbShowIcon" @update:value="themeStore.setBreadcrumbShowIcon" />
          </section>

          <!-- Group 4: Tab — sub-rows gated on `tabVisible` so disabled
               knobs don't crowd the panel when the bar itself is off. -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.group.tab') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.showTab') }}</span>
            <NSwitch :value="themeStore.tabVisible" @update:value="themeStore.setTabVisible" />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.tabHeight') }}</span>
            <NInputNumber
              :value="themeStore.tabHeight"
              size="small"
              :min="32"
              :max="56"
              :step="2"
              class="w-120px"
              @update:value="(v: number | null) => v != null && themeStore.setTabHeight(v)"
            />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.tabStyle') }}</span>
            <NSelect
              :value="themeStore.tabStyle"
              size="small"
              class="w-160px"
              :options="TAB_STYLE_OPTIONS.map((s) => ({ value: s, label: translate(TAB_STYLE_LABEL_KEY[s]) }))"
              @update:value="(v: TabStyle) => themeStore.setTabStyle(v)"
            />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.tabCache') }}</span>
            <NSwitch :value="themeStore.tabCache" @update:value="themeStore.setTabCache" />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.closeTabByMiddleClick') }}</span>
            <NSwitch
              :value="themeStore.closeTabByMiddleClick"
              @update:value="themeStore.setCloseTabByMiddleClick"
            />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.tabScrollAnimation') }}</span>
            <NSwitch
              :value="themeStore.tabScrollAnimation"
              @update:value="themeStore.setTabScrollAnimation"
            />
          </section>

          <!-- Group 5: Footer -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.group.footer') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.showFooter') }}</span>
            <NSwitch :value="themeStore.footerVisible" @update:value="themeStore.setFooterVisible" />
          </section>
          <section v-if="themeStore.footerVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.footerHeight') }}</span>
            <NInputNumber
              :value="themeStore.footerHeight"
              size="small"
              :min="32"
              :max="80"
              :step="2"
              class="w-120px"
              @update:value="(v: number | null) => v != null && themeStore.setFooterHeight(v)"
            />
          </section>

          <!-- Group 6: Content (scroll mode + page animation + fixed pins) -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.group.content') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.scrollMode') }}</span>
            <NSelect
              :value="themeStore.scrollMode"
              size="small"
              class="w-160px"
              :options="[
                { value: 'content', label: translate('admin.theme.layout.scrollModeContent') },
                { value: 'wrapper', label: translate('admin.theme.layout.scrollModeWrapper') },
              ]"
              @update:value="themeStore.setScrollMode"
            />
          </section>
          <section v-if="themeStore.scrollMode === 'wrapper'" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.fixedHeader') }}</span>
            <NSwitch :value="themeStore.fixedHeader" @update:value="themeStore.setFixedHeader" />
          </section>
          <section v-if="themeStore.scrollMode === 'wrapper' && themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.fixedTab') }}</span>
            <NSwitch :value="themeStore.fixedTab" @update:value="themeStore.setFixedTab" />
          </section>
          <section v-if="themeStore.scrollMode === 'wrapper' && themeStore.footerVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.layout.fixedFooter') }}</span>
            <NSwitch :value="themeStore.fixedFooter" @update:value="themeStore.setFixedFooter" />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.pageAnimate') }}</span>
            <NSwitch :value="themeStore.pageAnimate" @update:value="themeStore.setPageAnimate" />
          </section>
          <section v-if="themeStore.pageAnimate" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.pageAnimateMode') }}</span>
            <NSelect
              :value="themeStore.pageTransition"
              size="small"
              class="w-160px"
              :options="TRANSITION_OPTIONS.map((t) => ({ value: t, label: translate(TRANSITION_LABEL_KEY[t]) }))"
              @update:value="(v: PageTransition) => themeStore.setPageTransition(v)"
            />
          </section>
        </NTabPane>

        <!-- ── Tab 3: General — 2 NDivider groups (Global/Watermark) ── -->
        <NTabPane
          name="general"
          :tab="translate('admin.theme.tabs.general')"
        >
          <!-- Group 1: Global — header chrome visibility toggles. -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.group.global') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.multilingualVisible') }}</span>
            <NSwitch
              :value="themeStore.multilingualVisible"
              @update:value="themeStore.setMultilingualVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.globalSearchVisible') }}</span>
            <NSwitch
              :value="themeStore.globalSearchVisible"
              @update:value="themeStore.setGlobalSearchVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.fullscreenVisible') }}</span>
            <NSwitch
              :value="themeStore.fullscreenVisible"
              @update:value="themeStore.setFullscreenVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.themeSchemaVisible') }}</span>
            <NSwitch
              :value="themeStore.themeSchemaVisible"
              @update:value="themeStore.setThemeSchemaVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.general.reloadVisible') }}</span>
            <NSwitch
              :value="themeStore.reloadVisible"
              @update:value="themeStore.setReloadVisible"
            />
          </section>

          <!-- Group 2: Watermark — sub-rows gated on `enabled` rather
               than disabled, so a turned-off watermark hides its
               configuration knobs entirely. -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.tabs.watermark') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ translate('admin.theme.watermark.enabled') }}</span>
            <NSwitch
              :value="themeStore.watermark.enabled"
              @update:value="(v: boolean) => themeStore.setWatermark({ enabled: v })"
            />
          </section>
          <template v-if="themeStore.watermark.enabled">
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.watermark.text') }}</span>
              <NInput
                :value="themeStore.watermark.text"
                size="small"
                class="w-180px"
                @update:value="(v: string) => themeStore.setWatermark({ text: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.watermark.includeUserName') }}</span>
              <NSwitch
                :value="themeStore.watermark.includeUserName"
                @update:value="(v: boolean) => themeStore.setWatermark({ includeUserName: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.watermark.includeDate') }}</span>
              <NSwitch
                :value="themeStore.watermark.includeDate"
                @update:value="(v: boolean) => themeStore.setWatermark({ includeDate: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.watermark.opacity') }}</span>
              <NInputNumber
                :value="themeStore.watermark.opacity"
                size="small"
                :min="0.05"
                :max="0.5"
                :step="0.05"
                :precision="2"
                class="w-120px"
                @update:value="(v: number | null) => v != null && themeStore.setWatermark({ opacity: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ translate('admin.theme.watermark.fontSize') }}</span>
              <NInputNumber
                :value="themeStore.watermark.fontSize"
                :min="10"
                :max="32"
                :step="1"
                size="small"
                class="w-100px"
                @update:value="(v: number | null) => v != null && themeStore.setWatermark({ fontSize: v })"
              />
            </section>
          </template>
        </NTabPane>

        <!-- ── Tab 4: Preset — palette cards + export/import ── -->
        <NTabPane
          name="preset"
          :tab="translate('admin.theme.tabs.preset')"
        >
          <!-- Palette cards: 3-col grid, each card carries a sizeable colour
               swatch, hex code, and a check overlay on the active palette.
               Hover lifts the card; active gets a primary-tinted border.
               Replaces the previous tiny coloured-button row, which had no
               typographic anchor and felt arbitrary. -->
          <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.preset.palette') }}</NDivider>
          <div class="t-theme-drawer__preset-grid">
            <button
              v-for="preset in resolvedPresets"
              :key="preset.name"
              type="button"
              class="t-theme-drawer__preset-card"
              :class="{ 't-theme-drawer__preset-card--active': ctx.settings.value.colors.primary === preset.primary }"
              :aria-label="`Apply ${preset.primary}`"
              @click="applyPresetColor(preset.primary)"
            >
              <span
                class="t-theme-drawer__preset-color"
                :style="{ backgroundColor: preset.primary }"
              >
                <Icon
                  v-if="ctx.settings.value.colors.primary === preset.primary"
                  icon="mdi:check"
                  width="22"
                  height="22"
                />
              </span>
              <span class="t-theme-drawer__preset-hex">{{ preset.primary.toUpperCase() }}</span>
            </button>
          </div>

          <section class="t-theme-drawer__section">
            <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.preset.export') }}</NDivider>
            <p class="t-theme-drawer__hint">
              {{ translate('admin.theme.preset.exportDescription') }}
            </p>
            <div class="t-theme-drawer__row-actions">
              <NButton size="small" @click="onCopy">
                {{ translate('admin.theme.preset.copy') }}
              </NButton>
              <NButton size="small" @click="onDownload">
                {{ translate('admin.theme.preset.download') }}
              </NButton>
            </div>
            <NInput
              :value="snapshotJson"
              type="textarea"
              :rows="6"
              size="small"
              readonly
              class="mt-8px font-mono text-11px"
            />
          </section>

          <section class="t-theme-drawer__section">
            <NDivider class="t-theme-drawer__divider">{{ translate('admin.theme.preset.import') }}</NDivider>
            <NInput
              v-model:value="importBuffer"
              type="textarea"
              :rows="5"
              size="small"
              :placeholder="translate('admin.theme.preset.importPlaceholder')"
              class="font-mono text-11px"
            />
            <div class="t-theme-drawer__row-actions mt-8px">
              <NButton size="small" @click="onChooseFile">
                {{ translate('admin.theme.preset.chooseFile') }}
              </NButton>
              <NButton type="primary" size="small" :disabled="!importBuffer.trim()" @click="onImport">
                {{ translate('admin.theme.preset.apply') }}
              </NButton>
            </div>
          </section>

          <!-- Phase D: reset/copy lifted into the drawer footer (visible
               from every tab). The Preset tab now only carries the
               export/import surfaces — destructive + clipboard ops live
               in the persistent footer below. -->
        </NTabPane>
      </NTabs>

      <!-- Persistent footer — soybean parity (`config-operation.vue:51-54`):
           Reset + Copy are reachable regardless of which tab is active. -->
      <template #footer>
        <div class="t-theme-drawer__footer">
          <NPopconfirm
            :positive-text="translate('admin.theme.reset')"
            @positive-click="resetAll"
          >
            <template #trigger>
              <NButton size="small">
                {{ translate('admin.theme.reset') }}
              </NButton>
            </template>
            {{ translate('admin.theme.resetConfirm') }}
          </NPopconfirm>
          <NButton type="primary" size="small" @click="onCopy">
            {{ translate('admin.theme.preset.copy') }}
          </NButton>
        </div>
      </template>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped>
/* Group container — no border-bottom now that NDivider provides the
   visual separator between groups. The old `border-bottom: 1px solid`
   stacked with the next NDivider and rendered as two parallel lines. */
.t-theme-drawer__section {
  padding: var(--tnzi-space-md, 16px) 0;
}
.t-theme-drawer__section-title {
  margin: 0 0 var(--tnzi-space-sm, 8px) 0;
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-base-text-muted, #4b5563);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.t-theme-drawer__hint {
  margin: 0 0 8px 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #6b7280);
}
.t-theme-drawer__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  /* Phase D: drop the dashed border between rows. soybean uses a flat
     12px-gap stack — the dashes added visual noise and made the panel
     look busier than it is. */
  padding: 8px 0;
  gap: 16px;
  flex-wrap: nowrap;
}

/* Phase D: persistent footer for the drawer. soybean keeps Reset + Copy
   reachable from every tab via a footer bar. */
.t-theme-drawer__footer {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}
.t-theme-drawer__row-label {
  font-size: 13px;
  color: var(--tnzi-base-text, #374151);
  flex: 1 1 auto;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-theme-drawer__color-grid {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 4px 0;
}
.t-theme-drawer__color-row {
  display: grid;
  grid-template-columns: 1fr 140px;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px dashed var(--tnzi-border, #e5e7eb);
  cursor: pointer;
}
.t-theme-drawer__color-row:last-child {
  border-bottom: none;
}
.t-theme-drawer__color-label {
  font-size: 13px;
  color: var(--tnzi-base-text, #374151);
}
.t-theme-drawer__color-picker {
  width: 140px;
}
.t-theme-drawer__row-actions {
  display: flex;
  gap: 8px;
}
/* Theme scheme tabs (light/dark/auto) — center the segmented control. */
.t-theme-drawer__mode-tabs {
  display: flex;
  justify-content: center;
}
/* Phase G — soybean parity: 6 layout cards arrange as 2 rows x 3 cols.
   `repeat(3, 1fr)` evenly partitions the drawer width into 3 cells so
   the column gap reads as a tight 16px (not the previous 42px from
   `space-between` over fixed-96px columns). Each card is fixed 96px
   wide so it centres inside its cell via `justify-self: center` (set
   on .t-layout-card itself in TLayoutModeCard). */
.t-theme-drawer__layout-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  column-gap: 16px;
  row-gap: 12px;
}
/* Phase D: .t-theme-drawer__reset deleted — reset moved to the persistent
   footer slot using NButton, no custom button styling needed. */

/* Palette cards in the Preset tab.
 * - 3-col grid, generous gap so the swatch column reads as a clear field
 *   of colour rather than a row of pills.
 * - Each card stacks a sizeable colour block above a monospace hex code,
 *   with a hover lift + active-palette ring. The check icon overlays the
 *   active swatch (white check on the colour for high contrast).
 * - Replaces the prior 2-col "tiny coloured-button + name = hex string"
 *   design that read as accidental rather than designed. */
.t-theme-drawer__preset-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-top: 4px;
}

/* Phones (<sm, viewport <640px): drop both card grids to 2 columns so the
   fixed-96px cards never push past the full-width drawer edge. Mirrors the
   former `compactGridCols` intent without the teleport-broken v-bind var. */
@media (max-width: 639.98px) {
  .t-theme-drawer__layout-grid,
  .t-theme-drawer__preset-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}
.t-theme-drawer__preset-card {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 6px;
  padding: 6px;
  background: var(--tnzi-container-bg, #fff);
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  cursor: pointer;
  transition:
    transform 0.18s ease,
    border-color 0.18s ease,
    box-shadow 0.18s ease;
}
.t-theme-drawer__preset-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-theme-drawer__preset-card--active {
  border-color: var(--tnzi-primary, #646cff);
  box-shadow: 0 0 0 2px rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.2);
}
.t-theme-drawer__preset-color {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 48px;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  color: #ffffff;
  /* subtle inset border so very light swatches stay distinguishable
     against the card background. */
  box-shadow: inset 0 0 0 1px rgb(0 0 0 / 0.06);
}
.t-theme-drawer__preset-hex {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  text-align: center;
  color: var(--tnzi-base-text-muted, #6b7280);
  letter-spacing: 0.02em;
}
</style>
