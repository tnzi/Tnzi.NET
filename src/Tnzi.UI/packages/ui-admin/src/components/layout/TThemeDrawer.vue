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
import { useTheme, THint, type ThemeContext, type ThemeColors } from '@tnzi/ui'
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
import { applyThemeSnapshot, buildThemeSnapshot } from '../../theme/snapshot'
import {
  BUILTIN_APPEARANCE_PRESETS,
  applyAppearancePreset,
  type AdminThemePreset,
} from '../../theme/appearance-presets'
import { isDarkSurface } from '../../theme/surfaceTone'
import type { GlobalThemeController } from '../../headless/useGlobalTheme'
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
  /** Full appearance presets shown in the Preset tab (colors + mode + layout
   *  + backgrounds). Defaults to the built-in curated looks. */
  appearancePresets?: AdminThemePreset[]
  translate?: (key: string) => string
  /**
   * Drawer variant.
   * - `'full'` (default) - every theme knob + the global save/reset footer
   *   when a `globalTheme` controller is supplied. For privileged users
   *   (system.appearance.update - super admins by default).
   * - `'presets'` - preset color-scheme picker only. What non-privileged
   *   users get; their choice persists locally and overlays the global theme.
   */
  mode?: 'full' | 'presets'
  /**
   * Global-theme controller (from `useGlobalTheme`). When present in full
   * mode, the footer gains "save for all users" + the reset action also
   * clears the server snapshot; in presets mode it re-applies the saved
   * global colors when the user clears their choice.
   */
  globalTheme?: GlobalThemeController | null
}

const props = withDefaults(defineProps<Props>(), {
  themeContext: undefined,
  presets: undefined,
  appearancePresets: undefined,
  translate: undefined,
  mode: 'full',
  globalTheme: null,
})

const emit = defineEmits<{
  'update:show': [value: boolean]
}>()

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
  'top-hybrid-header-first',
]

const LAYOUT_LABEL_KEY: Record<AdminLayoutMode, string> = {
  'vertical': 'admin.theme.layout.vertical',
  'horizontal': 'admin.theme.layout.horizontal',
  'vertical-mix': 'admin.theme.layout.verticalMix',
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
// var - NDrawer teleports its content to <body>, and Vue's `v-bind()` writes
// the var onto the component root (which stays in place), so teleported nodes
// can't resolve it and `repeat(var(--x), 1fr)` collapses to a single column.
// Scoped data-v selectors DO follow teleported nodes, so a media query works.

// I.7.10 - 5-tab → 4-tab consolidation: watermark settings merged into the
// `general` tab (matches soybean's drawer layout).
const activeTab = ref<'appearance' | 'layout' | 'general' | 'preset'>('appearance')
const importBuffer = ref('')

/** Full appearance presets (whole looks) - rendered in the admin's Preset tab
 *  AND in the non-privileged users' preset drawer (they pick a whole look). */
const resolvedAppearancePresets = computed<AdminThemePreset[]>(
  () => props.appearancePresets ?? BUILTIN_APPEARANCE_PRESETS,
)

/** Resolved swatches a look's mini-preview renders - the real sider / header /
 *  canvas colors the look would produce (accounting for mode + invert). */
interface LookPreview {
  sider: string
  header: string
  canvas: string
  menu: string
  menuActive: string
  /** Header-hosted menu contrast (horizontal / hybrid put the menu in the header). */
  headerMenu: string
  headerMenuActive: string
  card: string
}
function lookPreview(preset: AdminThemePreset): LookPreview {
  const dark = preset.mode === 'dark'
  const sider = preset.siderBg ?? (preset.invertSider ? '#0b1220' : dark ? '#1f1f1f' : '#ffffff')
  const header = preset.headerBg ?? (dark ? '#1f1f1f' : '#ffffff')
  const canvas = preset.contentBg ?? (dark ? '#121212' : '#f2f4f7')
  const siderDark = isDarkSurface(sider)
  const headerDark = isDarkSurface(header)
  return {
    sider,
    header,
    canvas,
    menu: siderDark ? 'rgba(255,255,255,0.42)' : 'rgba(0,0,0,0.22)',
    menuActive: siderDark ? 'rgba(255,255,255,0.92)' : preset.primary,
    headerMenu: headerDark ? 'rgba(255,255,255,0.42)' : 'rgba(0,0,0,0.22)',
    headerMenuActive: headerDark ? 'rgba(255,255,255,0.92)' : preset.primary,
    // A look that paints its cards shows the REAL card color (dark elevated
    // cards / warm paper cards are part of the look's identity); otherwise a
    // subtle overlay hints "a card sits on the canvas here".
    card: preset.cardBg ?? (dark ? 'rgba(255,255,255,0.09)' : 'rgba(0,0,0,0.05)'),
  }
}
/** The current layout the preset previews should mock (presets are appearance-
 *  only, so every card mirrors the live layout, not a per-preset one). */
const previewLayout = computed<AdminLayoutMode>(() => themeStore.layoutMode)
/** [{ preset, pv }] - `pv` is memoized per preset (static from the preset). */
const appearanceLooks = computed(() =>
  resolvedAppearancePresets.value.map((preset) => ({ preset, pv: lookPreview(preset) })),
)

function appearancePresetLabel(preset: AdminThemePreset): string {
  return preset.label ?? tr(`admin.theme.preset.looks.${preset.name}`)
}
function applyLook(preset: AdminThemePreset): void {
  // The "default" look IS the factory default - mirror the drawer's
  // "Reset to default" (clear every surface + accent + radius + mode) so the
  // two are guaranteed identical. Applying its literal fields instead would
  // drift from the app-configured default primary / radius.
  if (preset.name === 'default') {
    ctx.reset()
    themeStore.reset()
    return
  }
  applyAppearancePreset(preset, themeStore, ctx)
}
/** Whether a look matches the live state - accent + mode + inverted shorthand +
 *  all five surface overrides. Case-insensitive; treats null / '' alike. */
function isLookActive(preset: AdminThemePreset): boolean {
  const live = ctx.settings.value
  const hexEq = (a?: string | null, b?: string | null): boolean =>
    (a ?? '').toLowerCase() === (b ?? '').toLowerCase()
  // "default" is the factory look - active when nothing is customised (no
  // surface overrides + built-in sider + default radius). The primary is left
  // out because the factory primary is app-configured, not the preset literal.
  if (preset.name === 'default') {
    return (
      themeStore.siderBg == null && themeStore.headerBg == null && themeStore.tabBg == null &&
      themeStore.footerBg == null && themeStore.contentBg == null &&
      themeStore.pageHeaderBg == null && themeStore.cardBg == null &&
      themeStore.invertSider === true && themeStore.themeRadius === 4
    )
  }
  return (
    hexEq(live.colors.primary, preset.primary) &&
    (preset.mode == null || live.mode === preset.mode) &&
    (preset.invertSider == null || themeStore.invertSider === preset.invertSider) &&
    hexEq(themeStore.siderBg, preset.siderBg) &&
    hexEq(themeStore.headerBg, preset.headerBg) &&
    hexEq(themeStore.tabBg, preset.tabBg) &&
    hexEq(themeStore.footerBg, preset.footerBg) &&
    hexEq(themeStore.contentBg, preset.contentBg) &&
    hexEq(themeStore.pageHeaderBg, preset.pageHeaderBg) &&
    hexEq(themeStore.cardBg, preset.cardBg)
  )
}

function tr(key: string): string {
  return props.translate ? props.translate(key) : key
}

// ─── Appearance handlers ──────────────────────────────────────────────────

function onSetColor(role: keyof ThemeColors, value: string | null): void {
  if (!value) return
  ctx.setColor(role, value)
  // Phase F - when infoFollowPrimary is on, primary changes propagate to
  // info too (mirrors soybean's `theme-color.vue:67-69` "Info follows
  // primary" checkbox behaviour).
  if (role === 'primary' && themeStore.infoFollowPrimary) {
    ctx.setColor('info' as keyof ThemeColors, value)
  }
}

/** "Info color follows primary" toggle - turning it ON must sync info to the
 *  CURRENT primary immediately (otherwise the switch looks inert until the next
 *  primary change). Turning it off leaves the current info color in place. */
function onToggleInfoFollowPrimary(v: boolean): void {
  themeStore.setInfoFollowPrimary(v)
  if (v) {
    const primary = ctx.settings.value.colors.primary
    if (primary) ctx.setColor('info' as keyof ThemeColors, primary)
  }
}

/** Per-surface rows in the Appearance → Backgrounds group. Each row pairs a
 *  background color picker with a text color picker: the text auto-adapts to
 *  the chosen background, and the text picker (empty = Auto) freely overrides. */
interface BgSurface {
  key: string
  labelKey: string
  get: () => string | null
  set: (v: string | null) => void
  reset: () => void
  fg: () => string | null
  setFg: (v: string | null) => void
  /** Layout-awareness - hide a surface row when the layout has no such element
   *  (e.g. the sidebar in `horizontal`, the tab/footer bars when turned off). */
  show?: () => boolean
}
const BG_SURFACES: BgSurface[] = [
  { key: 'sider', labelKey: 'admin.theme.appearance.siderBg', get: () => themeStore.siderBg, set: (v) => themeStore.setSiderBg(v), reset: () => themeStore.resetSiderBg(), fg: () => themeStore.siderTextColor, setFg: (v) => themeStore.setSiderTextColor(v), show: () => themeStore.layoutMode !== 'horizontal' },
  { key: 'header', labelKey: 'admin.theme.appearance.headerBg', get: () => themeStore.headerBg, set: (v) => themeStore.setHeaderBg(v), reset: () => themeStore.resetHeaderBg(), fg: () => themeStore.headerTextColor, setFg: (v) => themeStore.setHeaderTextColor(v) },
  { key: 'tab', labelKey: 'admin.theme.appearance.tabBg', get: () => themeStore.tabBg, set: (v) => themeStore.setTabBg(v), reset: () => themeStore.resetTabBg(), fg: () => themeStore.tabTextColor, setFg: (v) => themeStore.setTabTextColor(v), show: () => themeStore.tabVisible },
  { key: 'footer', labelKey: 'admin.theme.appearance.footerBg', get: () => themeStore.footerBg, set: (v) => themeStore.setFooterBg(v), reset: () => themeStore.resetFooterBg(), fg: () => themeStore.footerTextColor, setFg: (v) => themeStore.setFooterTextColor(v), show: () => themeStore.footerVisible },
  { key: 'content', labelKey: 'admin.theme.appearance.contentBg', get: () => themeStore.contentBg, set: (v) => themeStore.setContentBg(v), reset: () => themeStore.resetContentBg(), fg: () => themeStore.contentTextColor, setFg: (v) => themeStore.setContentTextColor(v) },
  { key: 'pageHeader', labelKey: 'admin.theme.appearance.pageHeaderBg', get: () => themeStore.pageHeaderBg, set: (v) => themeStore.setPageHeaderBg(v), reset: () => themeStore.resetPageHeaderBg(), fg: () => themeStore.pageHeaderTextColor, setFg: (v) => themeStore.setPageHeaderTextColor(v) },
  { key: 'card', labelKey: 'admin.theme.appearance.cardBg', get: () => themeStore.cardBg, set: (v) => themeStore.setCardBg(v), reset: () => themeStore.resetCardBg(), fg: () => themeStore.cardTextColor, setFg: (v) => themeStore.setCardTextColor(v) },
]
/** Backgrounds rows filtered to the ones the current layout actually renders. */
const visibleBgSurfaces = computed<BgSurface[]>(() => BG_SURFACES.filter((s) => (s.show ? s.show() : true)))
// Preset swatch palettes shown at the bottom of the color pickers (same
// affordance across all pickers). Naive lays swatches out 8-per-row, so each
// palette is 24 colors = exactly 3 full rows.

/** Accent (theme color) swatches - a vibrant hue spectrum (Tailwind 500/600). */
const ACCENT_SWATCHES: string[] = [
  '#EF4444', '#F97316', '#F59E0B', '#EAB308', '#84CC16', '#22C55E', '#10B981', '#14B8A6',
  '#06B6D4', '#0EA5E9', '#3B82F6', '#2080F0', '#6366F1', '#8B5CF6', '#A855F7', '#D946EF',
  '#EC4899', '#F43F5E', '#E11D48', '#DB2777', '#4F46E5', '#0891B2', '#0F766E', '#64748B',
]
/** Surface background swatches - light neutrals / tinted canvases, dark
 *  neutrals (slate / zinc / stone), then saturated brand darks (read white). */
const SURFACE_SWATCHES: string[] = [
  '#FFFFFF', '#F8FAFC', '#F1F5F9', '#E5E7EB', '#FFF7ED', '#FEFCE8', '#F0FDF4', '#F0F9FF',
  '#0F172A', '#1E293B', '#334155', '#18181B', '#27272A', '#171717', '#1C1917', '#292524',
  '#0C4A6E', '#172554', '#1E1B4B', '#3B0764', '#4A044E', '#064E3B', '#14532D', '#4C0519',
]
/** Text (foreground) swatches - lights for dark surfaces, darks for light
 *  surfaces, then a row of accent text colors. */
const TEXT_SWATCHES: string[] = [
  '#FFFFFF', '#F8FAFC', '#E5E7EB', '#CBD5E1', '#94A3B8', '#E0E7FF', '#FEF3C7', '#D1FAE5',
  '#000000', '#0F172A', '#1E293B', '#334155', '#475569', '#64748B', '#111827', '#1F2937',
  '#2563EB', '#7C3AED', '#DB2777', '#DC2626', '#EA580C', '#059669', '#0891B2', '#CA8A04',
]

function setMode(mode: 'light' | 'dark' | 'auto'): void {
  ctx.setMode(mode)
}

// ─── Layout handlers ──────────────────────────────────────────────────────

function selectLayoutMode(mode: AdminLayoutMode): void {
  themeStore.setLayoutMode(mode)
}

// ─── User preset picker (presets mode) ────────────────────────────────────

/**
 * Non-privileged user picks a WHOLE appearance look (a coordinated preset -
 * accent + mode + surfaces + radius, not just a color). The choice is a personal
 * override: it applies live, persists locally (`userPresetLook`), and re-applies
 * on top of the admin's global theme at boot (see overlayUserPreset).
 *
 * The "default" look means "follow the admin's global theme" - clear the
 * personal override and re-apply the remote snapshot (or factory reset when no
 * global theme exists).
 */
function selectUserLook(preset: AdminThemePreset): void {
  if (preset.name === 'default') {
    themeStore.setUserPresetLook(null)
    themeStore.setUserPresetColor(null)
    if (props.globalTheme?.remote.value) {
      props.globalTheme.applyRemote()
    } else {
      ctx.reset()
      themeStore.reset()
    }
    return
  }
  applyAppearancePreset(preset, themeStore, ctx)
  themeStore.setUserPresetLook(preset.name)
  // A full look supersedes the legacy color-only overlay.
  themeStore.setUserPresetColor(null)
}
/** Which look the non-privileged user currently has (default = following global). */
function isUserLookActive(preset: AdminThemePreset): boolean {
  if (preset.name === 'default') return !themeStore.userPresetLook
  return themeStore.userPresetLook === preset.name
}

// ─── Preset handlers ──────────────────────────────────────────────────────

function buildSnapshot(): AdminThemeSnapshot {
  return buildThemeSnapshot(themeStore, ctx)
}

async function onCopy(): Promise<void> {
  const ok = await copySnapshotToClipboard(buildSnapshot())
  if (ok) {
    message.success(tr('admin.theme.preset.clipboardOk'))
  } else {
    message.warning(tr('admin.theme.preset.clipboardFail'))
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
        ? tr('admin.theme.preset.invalidSnapshot')
        : tr('admin.theme.preset.invalidJson'),
    )
    return
  }
  applySnapshot(snapshot)
  importBuffer.value = ''
  message.success(tr('admin.theme.preset.apply'))
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
      message.error(tr('admin.theme.preset.invalidJson'))
    }
    reader.readAsText(file)
  }
  input.click()
}

function applySnapshot(snapshot: AdminThemeSnapshot): void {
  applyThemeSnapshot(snapshot, themeStore, ctx)
}

// ─── Global theme (save for all users) ────────────────────────────────────

const globalEnabled = computed(() => props.mode !== 'presets' && (props.globalTheme?.enabled ?? false))
const globalSaving = computed(() => props.globalTheme?.saving.value ?? false)
const globalDirty = computed(() => props.globalTheme?.isDirty.value ?? false)

async function onSaveGlobal(): Promise<void> {
  if (!props.globalTheme) return
  const ok = await props.globalTheme.save()
  if (ok) {
    message.success(tr('admin.theme.global.saved'))
  } else {
    message.error(tr('admin.theme.global.saveFailed'))
  }
}

async function resetAll(): Promise<void> {
  ctx.reset()
  themeStore.reset()
  // With global sync active, "reset" persists the FACTORY snapshot as the
  // new global theme (instead of deleting the server row): other clients
  // hold the last applied theme in their local cache, and only a saved
  // snapshot reaches them - a bare delete would leave everyone on the
  // stale theme forever.
  if (globalEnabled.value && props.globalTheme) {
    const ok = await props.globalTheme.save()
    if (!ok) {
      message.error(tr('admin.theme.global.resetFailed'))
      return
    }
  }
  message.success(tr('admin.theme.reset'))
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
      :title="mode === 'presets' ? tr('admin.theme.userPreset.title') : tr('admin.theme.title')"
      closable
      :native-scrollbar="false"
    >
      <!-- Presets mode: the "follows the admin's global theme" explainer rides
           as a compact THint beside the title instead of a standing paragraph
           (same convention as the User Center section hints). -->
      <template v-if="mode === 'presets'" #header>
        <span class="t-theme-drawer__title-row">
          {{ tr('admin.theme.userPreset.title') }}
          <THint type="help" :content="tr('admin.theme.userPreset.hint')" />
        </span>
      </template>
      <!-- ── Presets-only variant: what non-privileged users get. Their only
           theme knob is the color scheme (plus the header's dark-mode cycle
           button when the admin keeps it visible); everything else follows
           the global theme managed by the super admin. ── -->
      <template v-if="mode === 'presets'">
        <!-- Whole coordinated looks - a non-privileged user picks a complete
             appearance (accent + mode + surfaces + radius), not just a color.
             The mini preview mirrors the current layout, same as the admin's
             Preset tab. -->
        <div class="t-theme-drawer__preset-grid">
          <button
            v-for="look in appearanceLooks"
            :key="look.preset.name"
            type="button"
            class="t-theme-drawer__look-card"
            :class="{ 't-theme-drawer__look-card--active': isUserLookActive(look.preset) }"
            :aria-label="appearancePresetLabel(look.preset)"
            @click="selectUserLook(look.preset)"
          >
            <span
              class="t-theme-drawer__look-preview"
              :class="`t-theme-drawer__look-preview--${previewLayout}`"
              :style="{ background: look.pv.canvas }"
            >
              <span
                v-if="previewLayout === 'vertical'"
                class="t-theme-drawer__look-sider"
                :style="{ background: look.pv.sider }"
              >
                <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menuActive, width: '78%' }" />
                <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '62%' }" />
                <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '70%' }" />
              </span>
              <template v-else-if="previewLayout === 'vertical-mix'">
                <span class="t-theme-drawer__look-rail" :style="{ background: look.pv.sider }">
                  <span class="t-theme-drawer__look-dot" :style="{ background: look.pv.menuActive }" />
                  <span class="t-theme-drawer__look-dot" :style="{ background: look.pv.menu }" />
                  <span class="t-theme-drawer__look-dot" :style="{ background: look.pv.menu }" />
                </span>
                <span class="t-theme-drawer__look-submenu" :style="{ background: look.pv.sider }">
                  <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menuActive, width: '80%' }" />
                  <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '64%' }" />
                </span>
              </template>
              <span class="t-theme-drawer__look-body">
                <span class="t-theme-drawer__look-header" :style="{ background: look.pv.header }">
                  <template v-if="previewLayout === 'horizontal' || previewLayout === 'top-hybrid-header-first'">
                    <span class="t-theme-drawer__look-hmenu t-theme-drawer__look-hmenu--active" :style="{ background: look.pv.headerMenuActive }" />
                    <span class="t-theme-drawer__look-hmenu" :style="{ background: look.pv.headerMenu }" />
                    <span class="t-theme-drawer__look-hmenu" :style="{ background: look.pv.headerMenu }" />
                  </template>
                </span>
                <span class="t-theme-drawer__look-lower">
                  <span
                    v-if="previewLayout === 'top-hybrid-header-first'"
                    class="t-theme-drawer__look-subsider"
                    :style="{ background: look.pv.sider }"
                  >
                    <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menuActive, width: '72%' }" />
                    <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '56%' }" />
                  </span>
                  <span class="t-theme-drawer__look-canvas" :style="{ background: look.pv.canvas }">
                    <span class="t-theme-drawer__look-accent" :style="{ background: look.preset.primary }" />
                    <span class="t-theme-drawer__look-card-hint" :style="{ background: look.pv.card }" />
                  </span>
                </span>
              </span>
              <Icon
                v-if="isUserLookActive(look.preset)"
                class="t-theme-drawer__look-check"
                icon="mdi:check-circle"
                width="18"
                height="18"
              />
            </span>
            <span class="t-theme-drawer__look-name">{{ appearancePresetLabel(look.preset) }}</span>
          </button>
        </div>

        <!-- Personal accessibility filters - a PER-USER preference every user
             controls for themselves (persisted locally, never part of the super
             admin's global theme). Non-privileged users get them here too. -->
        <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.appearance.accessibility') }}</NDivider>
        <section class="t-theme-drawer__row">
          <span class="t-theme-drawer__row-label">{{ tr('admin.theme.appearance.grayscale') }}</span>
          <NSwitch :value="themeStore.grayscale" @update:value="themeStore.setGrayscale" />
        </section>
        <section class="t-theme-drawer__row">
          <span class="t-theme-drawer__row-label">{{ tr('admin.theme.appearance.colourWeakness') }}</span>
          <NSwitch :value="themeStore.colourWeakness" @update:value="themeStore.setColourWeakness" />
        </section>
      </template>

      <NTabs v-else v-model:value="activeTab" type="segment" justify-content="space-evenly">
        <!-- ── Tab 1: Appearance - 3 NDivider groups (Scheme/Color/Radius) ── -->
        <NTabPane
          name="appearance"
          :tab="tr('admin.theme.tabs.appearance')"
        >
          <!-- Group 1: Theme scheme - icon-only segmented tabs, horizontally
               centered. Mirrors soybean's theme-schema.vue (NTabs type=segment
               + SvgIcon per tab, w-214px center via `.i-flex-center`). -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.appearance.mode') }}</NDivider>
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
          <!-- "Inverted sider" is the built-in dark-sider shorthand - it only
               applies in light mode + a vertical-family layout, AND only when no
               custom Sidebar background is set (a custom siderBg WINS over it, so
               showing the toggle then would be inert). Hidden otherwise so it's
               never a dead switch. -->
          <section
            v-if="!ctx.isDark.value && themeStore.layoutMode.startsWith('vertical') && !themeStore.siderBg"
            class="t-theme-drawer__row"
          >
            <span class="t-theme-drawer__row-label t-theme-drawer__row-label--hint">
              {{ tr('admin.theme.layout.invertSider') }}
              <THint type="info" :content="tr('admin.theme.layout.invertSiderHint')" />
            </span>
            <NSwitch :value="themeStore.invertSider" @update:value="themeStore.toggleInvertSider" />
          </section>
          <!-- Accessibility filters - a PERSONAL per-user preference (persisted
               locally, NOT saved into the global theme). Shown to every user. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.appearance.accessibility') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.appearance.grayscale') }}</span>
            <NSwitch :value="themeStore.grayscale" @update:value="themeStore.setGrayscale" />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.appearance.colourWeakness') }}</span>
            <NSwitch :value="themeStore.colourWeakness" @update:value="themeStore.setColourWeakness" />
          </section>

          <!-- Group 2: Theme color (recommend toggle + 5 pickers + follow primary).
               Soybean parity: preset swatches are embedded INSIDE NColorPicker
               via `:swatches`, not rendered as a separate row of buttons. That
               removes the duplicate "swatches block above + picker below"
               feel of the previous design. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.appearance.themeColor') }}</NDivider>
          <div class="t-theme-drawer__color-grid">
            <label
              v-for="role in COLOR_ROLES"
              :key="role.role"
              class="t-theme-drawer__color-row"
            >
              <span class="t-theme-drawer__color-label">{{ tr(role.key) }}</span>
              <NColorPicker
                :value="ctx.settings.value.colors[role.role]"
                :modes="['hex']"
                :show-alpha="false"
                :swatches="ACCENT_SWATCHES"
                size="small"
                class="t-theme-drawer__color-picker"
                @update:value="(v: string | null) => onSetColor(role.role, v)"
              />
            </label>
          </div>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.appearance.infoFollowPrimary') }}</span>
            <NSwitch :value="themeStore.infoFollowPrimary" @update:value="onToggleInfoFollowPrimary" />
          </section>

          <!-- Group 3: Radius (number input - soybean parity) -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.appearance.themeRadius') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.appearance.borderRadius') }}</span>
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

          <!-- Group 4: Per-surface backgrounds. Each surface adapts its
               foreground to the chosen color (dark color → light text), so any
               color stays readable. Null value = default token fallback. -->
          <NDivider class="t-theme-drawer__divider">
            <span class="t-theme-drawer__divider-title">
              {{ tr('admin.theme.appearance.backgrounds') }}
              <THint type="info" :content="tr('admin.theme.appearance.backgroundsHint')" />
            </span>
          </NDivider>
          <div class="t-theme-drawer__color-grid">
            <div
              v-for="s in visibleBgSurfaces"
              :key="s.key"
              class="t-theme-drawer__bg-surface"
            >
              <span class="t-theme-drawer__color-label">{{ tr(s.labelKey) }}</span>
              <!-- Front: background color. Pass `null` (not `undefined`) when
                   cleared so the picker stays controlled and repaints empty. -->
              <div class="t-theme-drawer__bg-cell">
                <NColorPicker
                  :value="s.get()"
                  :modes="['hex']"
                  :show-alpha="false"
                  :swatches="SURFACE_SWATCHES"
                  size="small"
                  @update:value="(v: string | null) => s.set(v)"
                />
                <NTooltip>
                  <template #trigger>
                    <NButton
                      quaternary
                      size="tiny"
                      class="t-theme-drawer__bg-reset"
                      :disabled="s.get() === null"
                      @click="s.reset()"
                    >
                      <Icon icon="mdi:restore" width="13" height="13" />
                    </NButton>
                  </template>
                  {{ tr('admin.theme.appearance.resetBg') }}
                </NTooltip>
              </div>
              <!-- Back: text color. Empty picker = Auto (derives from the bg,
                   or from the picked text color's own luminance). Always shown. -->
              <div class="t-theme-drawer__bg-cell">
                <NTooltip>
                  <template #trigger>
                    <Icon icon="mdi:format-color-text" class="t-theme-drawer__bg-text-icon" width="15" height="15" />
                  </template>
                  {{ tr('admin.theme.appearance.textColor') }}
                </NTooltip>
                <NColorPicker
                  :value="s.fg()"
                  :modes="['hex']"
                  :show-alpha="false"
                  :swatches="TEXT_SWATCHES"
                  size="small"
                  @update:value="(v: string | null) => s.setFg(v)"
                />
                <NTooltip>
                  <template #trigger>
                    <NButton
                      quaternary
                      size="tiny"
                      class="t-theme-drawer__bg-reset"
                      :disabled="s.fg() === null"
                      @click="s.setFg(null)"
                    >
                      <Icon icon="mdi:restore" width="13" height="13" />
                    </NButton>
                  </template>
                  {{ tr('admin.theme.appearance.textAuto') }}
                </NTooltip>
              </div>
            </div>
          </div>
        </NTabPane>

        <!-- ── Tab 2: Layout - soybean parity (Mode/Sider/Header/Tab/Footer/Content) ── -->
        <NTabPane
          name="layout"
          :tab="tr('admin.theme.tabs.layout')"
        >
          <!-- Group 1: Layout mode (6 visual cards) -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.layout.mode') }}</NDivider>
          <section class="t-theme-drawer__section">
            <div class="t-theme-drawer__layout-grid">
              <TLayoutModeCard
                v-for="m in LAYOUT_MODES"
                :key="m"
                :mode="m"
                :active="themeStore.layoutMode === m"
                :label="tr(LAYOUT_LABEL_KEY[m])"
                @select="selectLayoutMode"
              />
            </div>
          </section>

          <!-- Group 2: Sider - hide entirely in horizontal mode (no sider exists) -->
          <template v-if="themeStore.layoutMode !== 'horizontal'">
            <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.group.sider') }}</NDivider>
            <section v-if="themeStore.layoutMode === 'vertical'" class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.siderWidth') }}</span>
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
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.siderCollapsedWidth') }}</span>
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
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.mixSiderWidth') }}</span>
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
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.mixCollapsedWidth') }}</span>
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
              v-if="themeStore.layoutMode === 'vertical-mix'"
              class="t-theme-drawer__row"
            >
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.mixChildMenuWidth') }}</span>
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
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.autoSelectFirstMenu') }}</span>
              <NSwitch
                :value="themeStore.autoSelectFirstMenu"
                @update:value="themeStore.setAutoSelectFirstMenu"
              />
            </section>
          </template>

          <!-- Group 3: Header - "show header" toggle removed; in non-vertical
               modes the header hosts the menu (hiding strands the user) and
               in vertical modes there's no reason to hide it. Footer/tab
               keep their visibility switches because they truly are
               optional. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.group.header') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.headerHeight') }}</span>
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
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.showBreadcrumb') }}</span>
            <NSwitch :value="themeStore.breadcrumbVisible" @update:value="themeStore.setBreadcrumbVisible" />
          </section>
          <section v-if="themeStore.breadcrumbVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.breadcrumbShowIcon') }}</span>
            <NSwitch :value="themeStore.breadcrumbShowIcon" @update:value="themeStore.setBreadcrumbShowIcon" />
          </section>

          <!-- Group 4: Tab - sub-rows gated on `tabVisible` so disabled
               knobs don't crowd the panel when the bar itself is off. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.group.tab') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.showTab') }}</span>
            <NSwitch :value="themeStore.tabVisible" @update:value="themeStore.setTabVisible" />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.tabHeight') }}</span>
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
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.tabStyle') }}</span>
            <NSelect
              :value="themeStore.tabStyle"
              size="small"
              class="w-160px"
              :options="TAB_STYLE_OPTIONS.map((s) => ({ value: s, label: tr(TAB_STYLE_LABEL_KEY[s]) }))"
              @update:value="(v: TabStyle) => themeStore.setTabStyle(v)"
            />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.tabCache') }}</span>
            <NSwitch :value="themeStore.tabCache" @update:value="themeStore.setTabCache" />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.closeTabByMiddleClick') }}</span>
            <NSwitch
              :value="themeStore.closeTabByMiddleClick"
              @update:value="themeStore.setCloseTabByMiddleClick"
            />
          </section>
          <section v-if="themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.tabScrollAnimation') }}</span>
            <NSwitch
              :value="themeStore.tabScrollAnimation"
              @update:value="themeStore.setTabScrollAnimation"
            />
          </section>

          <!-- Group 5: Footer -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.group.footer') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.showFooter') }}</span>
            <NSwitch :value="themeStore.footerVisible" @update:value="themeStore.setFooterVisible" />
          </section>
          <section v-if="themeStore.footerVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.footerHeight') }}</span>
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
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.group.content') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.scrollMode') }}</span>
            <NSelect
              :value="themeStore.scrollMode"
              size="small"
              class="w-160px"
              :options="[
                { value: 'content', label: tr('admin.theme.layout.scrollModeContent') },
                { value: 'wrapper', label: tr('admin.theme.layout.scrollModeWrapper') },
              ]"
              @update:value="themeStore.setScrollMode"
            />
          </section>
          <section v-if="themeStore.scrollMode === 'wrapper'" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.fixedHeader') }}</span>
            <NSwitch :value="themeStore.fixedHeader" @update:value="themeStore.setFixedHeader" />
          </section>
          <section v-if="themeStore.scrollMode === 'wrapper' && themeStore.tabVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.fixedTab') }}</span>
            <NSwitch :value="themeStore.fixedTab" @update:value="themeStore.setFixedTab" />
          </section>
          <section v-if="themeStore.scrollMode === 'wrapper' && themeStore.footerVisible" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.layout.fixedFooter') }}</span>
            <NSwitch :value="themeStore.fixedFooter" @update:value="themeStore.setFixedFooter" />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.pageAnimate') }}</span>
            <NSwitch :value="themeStore.pageAnimate" @update:value="themeStore.setPageAnimate" />
          </section>
          <section v-if="themeStore.pageAnimate" class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.pageAnimateMode') }}</span>
            <NSelect
              :value="themeStore.pageTransition"
              size="small"
              class="w-160px"
              :options="TRANSITION_OPTIONS.map((t) => ({ value: t, label: tr(TRANSITION_LABEL_KEY[t]) }))"
              @update:value="(v: PageTransition) => themeStore.setPageTransition(v)"
            />
          </section>
        </NTabPane>

        <!-- ── Tab 3: General - 2 NDivider groups (Global/Watermark) ── -->
        <NTabPane
          name="general"
          :tab="tr('admin.theme.tabs.general')"
        >
          <!-- Group 1: Global - header chrome visibility toggles. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.group.global') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.multilingualVisible') }}</span>
            <NSwitch
              :value="themeStore.multilingualVisible"
              @update:value="themeStore.setMultilingualVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.globalSearchVisible') }}</span>
            <NSwitch
              :value="themeStore.globalSearchVisible"
              @update:value="themeStore.setGlobalSearchVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.fullscreenVisible') }}</span>
            <NSwitch
              :value="themeStore.fullscreenVisible"
              @update:value="themeStore.setFullscreenVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.themeSchemaVisible') }}</span>
            <NSwitch
              :value="themeStore.themeSchemaVisible"
              @update:value="themeStore.setThemeSchemaVisible"
            />
          </section>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.reloadVisible') }}</span>
            <NSwitch
              :value="themeStore.reloadVisible"
              @update:value="themeStore.setReloadVisible"
            />
          </section>
          <!-- Whether non-privileged users get the preset color-scheme
               picker (palette button in the header). Part of the global
               snapshot - save to apply for everyone. -->
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.general.presetPickerVisible') }}</span>
            <NSwitch
              :value="themeStore.presetPickerVisible"
              @update:value="themeStore.setPresetPickerVisible"
            />
          </section>

          <!-- Group 2: Watermark - sub-rows gated on `enabled` rather
               than disabled, so a turned-off watermark hides its
               configuration knobs entirely. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.tabs.watermark') }}</NDivider>
          <section class="t-theme-drawer__row">
            <span class="t-theme-drawer__row-label">{{ tr('admin.theme.watermark.enabled') }}</span>
            <NSwitch
              :value="themeStore.watermark.enabled"
              @update:value="(v: boolean) => themeStore.setWatermark({ enabled: v })"
            />
          </section>
          <template v-if="themeStore.watermark.enabled">
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.watermark.text') }}</span>
              <NInput
                :value="themeStore.watermark.text"
                size="small"
                class="w-180px"
                @update:value="(v: string) => themeStore.setWatermark({ text: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.watermark.includeUserName') }}</span>
              <NSwitch
                :value="themeStore.watermark.includeUserName"
                @update:value="(v: boolean) => themeStore.setWatermark({ includeUserName: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.watermark.includeDate') }}</span>
              <NSwitch
                :value="themeStore.watermark.includeDate"
                @update:value="(v: boolean) => themeStore.setWatermark({ includeDate: v })"
              />
            </section>
            <section class="t-theme-drawer__row">
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.watermark.opacity') }}</span>
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
              <span class="t-theme-drawer__row-label">{{ tr('admin.theme.watermark.fontSize') }}</span>
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

        <!-- ── Tab 4: Preset - full appearance looks + export/import ── -->
        <NTabPane
          name="preset"
          :tab="tr('admin.theme.tabs.preset')"
        >
          <!-- Appearance looks: each card applies a COMPLETE look - accent
               color + light/dark mode + layout + corner radius + tab style +
               the per-surface background colors - not just a primary swatch.
               The mini preview shows the sider/header/accent the look sets. -->
          <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.preset.palette') }}</NDivider>
          <div class="t-theme-drawer__preset-grid">
            <button
              v-for="look in appearanceLooks"
              :key="look.preset.name"
              type="button"
              class="t-theme-drawer__look-card"
              :class="{ 't-theme-drawer__look-card--active': isLookActive(look.preset) }"
              :aria-label="appearancePresetLabel(look.preset)"
              @click="applyLook(look.preset)"
            >
              <!-- Mini admin mockup - mirrors the CURRENT layout mode: full
                   sider (vertical), narrow rail + submenu (vertical-mix), top
                   menu only (horizontal), or top menu + sub-sider (hybrid).
                   Colors are the REAL surfaces the look produces (mode/invert). -->
              <span
                class="t-theme-drawer__look-preview"
                :class="`t-theme-drawer__look-preview--${previewLayout}`"
                :style="{ background: look.pv.canvas }"
              >
                <!-- Vertical: full sider -->
                <span
                  v-if="previewLayout === 'vertical'"
                  class="t-theme-drawer__look-sider"
                  :style="{ background: look.pv.sider }"
                >
                  <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menuActive, width: '78%' }" />
                  <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '62%' }" />
                  <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '70%' }" />
                </span>
                <!-- Vertical-mix: narrow icon rail + submenu column -->
                <template v-else-if="previewLayout === 'vertical-mix'">
                  <span class="t-theme-drawer__look-rail" :style="{ background: look.pv.sider }">
                    <span class="t-theme-drawer__look-dot" :style="{ background: look.pv.menuActive }" />
                    <span class="t-theme-drawer__look-dot" :style="{ background: look.pv.menu }" />
                    <span class="t-theme-drawer__look-dot" :style="{ background: look.pv.menu }" />
                  </span>
                  <span class="t-theme-drawer__look-submenu" :style="{ background: look.pv.sider }">
                    <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menuActive, width: '80%' }" />
                    <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '64%' }" />
                  </span>
                </template>
                <span class="t-theme-drawer__look-body">
                  <!-- Header strip - hosts the menu in horizontal / hybrid. -->
                  <span class="t-theme-drawer__look-header" :style="{ background: look.pv.header }">
                    <template v-if="previewLayout === 'horizontal' || previewLayout === 'top-hybrid-header-first'">
                      <span class="t-theme-drawer__look-hmenu t-theme-drawer__look-hmenu--active" :style="{ background: look.pv.headerMenuActive }" />
                      <span class="t-theme-drawer__look-hmenu" :style="{ background: look.pv.headerMenu }" />
                      <span class="t-theme-drawer__look-hmenu" :style="{ background: look.pv.headerMenu }" />
                    </template>
                  </span>
                  <span class="t-theme-drawer__look-lower">
                    <!-- Hybrid: a sub-sider sits left of the canvas, below the header. -->
                    <span
                      v-if="previewLayout === 'top-hybrid-header-first'"
                      class="t-theme-drawer__look-subsider"
                      :style="{ background: look.pv.sider }"
                    >
                      <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menuActive, width: '72%' }" />
                      <span class="t-theme-drawer__look-line" :style="{ background: look.pv.menu, width: '56%' }" />
                    </span>
                    <span class="t-theme-drawer__look-canvas" :style="{ background: look.pv.canvas }">
                      <span class="t-theme-drawer__look-accent" :style="{ background: look.preset.primary }" />
                      <span class="t-theme-drawer__look-card-hint" :style="{ background: look.pv.card }" />
                    </span>
                  </span>
                </span>
                <Icon
                  v-if="isLookActive(look.preset)"
                  class="t-theme-drawer__look-check"
                  icon="mdi:check-circle"
                  width="18"
                  height="18"
                />
              </span>
              <span class="t-theme-drawer__look-name">{{ appearancePresetLabel(look.preset) }}</span>
            </button>
          </div>

          <section class="t-theme-drawer__section">
            <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.preset.export') }}</NDivider>
            <p class="t-theme-drawer__hint">
              {{ tr('admin.theme.preset.exportDescription') }}
            </p>
            <div class="t-theme-drawer__row-actions">
              <NButton size="small" @click="onCopy">
                {{ tr('admin.theme.preset.copy') }}
              </NButton>
              <NButton size="small" @click="onDownload">
                {{ tr('admin.theme.preset.download') }}
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
            <NDivider class="t-theme-drawer__divider">{{ tr('admin.theme.preset.import') }}</NDivider>
            <NInput
              v-model:value="importBuffer"
              type="textarea"
              :rows="5"
              size="small"
              :placeholder="tr('admin.theme.preset.importPlaceholder')"
              class="font-mono text-11px"
            />
            <div class="t-theme-drawer__row-actions mt-8px">
              <NButton size="small" @click="onChooseFile">
                {{ tr('admin.theme.preset.chooseFile') }}
              </NButton>
              <NButton type="primary" size="small" :disabled="!importBuffer.trim()" @click="onImport">
                {{ tr('admin.theme.preset.apply') }}
              </NButton>
            </div>
          </section>

          <!-- Phase D: reset/copy lifted into the drawer footer (visible
               from every tab). The Preset tab now only carries the
               export/import surfaces - destructive + clipboard ops live
               in the persistent footer below. -->
        </NTabPane>
      </NTabs>

      <!-- Persistent footer (full mode only) - Reset stays reachable from
           every tab; with global sync active the primary action becomes
           "save for all users" (with an unsaved-changes dot), otherwise the
           legacy Copy button. -->
      <template v-if="mode !== 'presets'" #footer>
        <div class="t-theme-drawer__footer">
          <NPopconfirm
            :positive-text="tr('admin.theme.reset')"
            @positive-click="resetAll"
          >
            <template #trigger>
              <NButton size="small">
                {{ tr('admin.theme.reset') }}
              </NButton>
            </template>
            {{ globalEnabled ? tr('admin.theme.global.resetConfirm') : tr('admin.theme.resetConfirm') }}
          </NPopconfirm>
          <NButton
            v-if="globalEnabled"
            type="primary"
            size="small"
            :loading="globalSaving"
            @click="onSaveGlobal"
          >
            <span v-if="globalDirty" class="t-theme-drawer__dirty-dot" aria-hidden="true" />
            {{ tr('admin.theme.global.save') }}
          </NButton>
          <NButton v-else type="primary" size="small" @click="onCopy">
            {{ tr('admin.theme.preset.copy') }}
          </NButton>
        </div>
      </template>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped>
/* Group divider (with title) - naive's default `margin: 24px 0` leaves a
   cavernous gap above and below each section title inside the drawer.
   Tighten it: a little breathing room above the title to separate groups,
   a small gap below before the group's controls. The scoped attribute
   (`.t-theme-drawer__divider[data-v-…]`, specificity 0,2,0) outranks
   naive's `.n-divider` (0,1,0) without `!important`. */
.t-theme-drawer__divider {
  margin-top: 14px;
  margin-bottom: 8px;
}
/* The first divider in a tab pane sits right under the tabs - no extra
   top gap needed. */
.t-theme-drawer__divider:first-child {
  margin-top: 2px;
}

/* Group container - no border-bottom now that NDivider provides the
   visual separator between groups. The old `border-bottom: 1px solid`
   stacked with the next NDivider and rendered as two parallel lines. */
.t-theme-drawer__section {
  padding: var(--tnzi-space-sm, 8px) 0;
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
/* Presets-mode drawer title + its THint explainer (replaces the old standing
   hint paragraph). Rendered inside naive's drawer header slot, so typography
   is inherited from the drawer title styles. */
.t-theme-drawer__title-row {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.t-theme-drawer__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  /* Phase D: drop the dashed border between rows. soybean uses a flat
     12px-gap stack - the dashes added visual noise and made the panel
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
/* Per-surface row - label + [bg picker | text picker] on one line. The text
   cell (with its own picker + reset) only mounts once a background is set. */
.t-theme-drawer__bg-surface {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 0;
  border-bottom: 1px dashed var(--tnzi-border, #e5e7eb);
}
.t-theme-drawer__bg-surface:last-child {
  border-bottom: none;
}
.t-theme-drawer__bg-surface .t-theme-drawer__color-label {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.t-theme-drawer__bg-cell {
  display: flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
}
/* Size the picker via :deep on the rendered root - naive's NColorPicker does
   not forward the `class` / scoped data-v onto its root, so a plain
   `.t-theme-drawer__bg-picker { width }` rule never matches (the picker then
   collapses to ~2px in the flex row). */
.t-theme-drawer__bg-cell :deep(.n-color-picker) {
  width: 76px;
}
.t-theme-drawer__bg-text-icon {
  color: var(--tnzi-base-text-muted, #6b7280);
  flex-shrink: 0;
}
.t-theme-drawer__bg-reset {
  padding: 0;
  height: 20px;
  width: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
/* Divider title with an inline hint icon - gap keeps the ⓘ off the text. */
.t-theme-drawer__divider-title {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.t-theme-drawer__row-actions {
  display: flex;
  gap: 8px;
}
/* Theme scheme tabs (light/dark/auto) - center the segmented control. */
.t-theme-drawer__mode-tabs {
  display: flex;
  justify-content: center;
}
/* 2026-06-27: with 4 layout modes (2 buggy ones removed earlier), the cards
   fit a single row - `repeat(4, 1fr)` + a tight 8px gap. Cards flex to fill
   their cell (capped at the original 96px canvas) via TLayoutModeCard. */
.t-theme-drawer__layout-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  column-gap: 8px;
  row-gap: 12px;
}
/* Phase D: .t-theme-drawer__reset deleted - reset moved to the persistent
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

/* Inline hint icon next to a row / divider label. */
.t-theme-drawer__row-label--hint {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

/* Appearance "look" cards - a mini admin mockup (sider + menu lines + header
   strip + canvas with accent + content-card) above the look name. Applying a
   card sets the whole appearance. */
.t-theme-drawer__look-card {
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
.t-theme-drawer__look-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-theme-drawer__look-card--active {
  border-color: var(--tnzi-primary, #646cff);
  box-shadow: 0 0 0 2px rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.2);
}
.t-theme-drawer__look-preview {
  position: relative;
  display: flex;
  height: 52px;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  overflow: hidden;
  box-shadow: inset 0 0 0 1px rgb(0 0 0 / 0.08);
}
.t-theme-drawer__look-sider {
  width: 32%;
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 6px 5px;
}
.t-theme-drawer__look-line {
  height: 3px;
  border-radius: 2px;
  flex-shrink: 0;
}
.t-theme-drawer__look-body {
  flex: 1 1 auto;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-theme-drawer__look-header {
  height: 30%;
  border-bottom: 1px solid rgb(0 0 0 / 0.06);
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 3px;
  padding: 0 5px;
}
/* Horizontal / hybrid: the header IS the primary nav band → a touch taller. */
.t-theme-drawer__look-preview--horizontal .t-theme-drawer__look-header,
.t-theme-drawer__look-preview--top-hybrid-header-first .t-theme-drawer__look-header {
  height: 34%;
}
.t-theme-drawer__look-hmenu {
  height: 3px;
  width: 10px;
  border-radius: 2px;
  flex-shrink: 0;
}
.t-theme-drawer__look-hmenu--active {
  width: 14px;
}
/* Lower area - a row so the hybrid sub-sider can sit beside the canvas. */
.t-theme-drawer__look-lower {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: row;
}
.t-theme-drawer__look-canvas {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 5px;
}
/* Vertical-mix: narrow icon rail + a submenu column. */
.t-theme-drawer__look-rail {
  width: 16%;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 6px 0;
  flex-shrink: 0;
}
.t-theme-drawer__look-dot {
  width: 6px;
  height: 6px;
  border-radius: 2px;
  flex-shrink: 0;
}
.t-theme-drawer__look-submenu {
  width: 22%;
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 6px 4px;
  flex-shrink: 0;
}
/* Hybrid: sub-sider below the header, left of the canvas. */
.t-theme-drawer__look-subsider {
  width: 26%;
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 5px 4px;
  flex-shrink: 0;
}
.t-theme-drawer__look-accent {
  height: 5px;
  width: 42%;
  border-radius: 3px;
  flex-shrink: 0;
}
.t-theme-drawer__look-card-hint {
  flex: 1 1 auto;
  border-radius: 3px;
  min-height: 8px;
}
.t-theme-drawer__look-check {
  position: absolute;
  top: 3px;
  right: 3px;
  color: var(--tnzi-primary, #646cff);
  background: #fff;
  border-radius: 50%;
}
.t-theme-drawer__look-name {
  font-size: 11px;
  text-align: center;
  color: var(--tnzi-base-text, #374151);
  text-transform: capitalize;
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
/* "Default" card in the user preset picker - a neutral hatched swatch that
   reads as "no personal choice, follow the global theme". */
.t-theme-drawer__preset-color--default {
  background: repeating-linear-gradient(
    45deg,
    var(--tnzi-layout-bg, #f3f4f6),
    var(--tnzi-layout-bg, #f3f4f6) 6px,
    var(--tnzi-border, #e5e7eb) 6px,
    var(--tnzi-border, #e5e7eb) 12px
  );
  color: var(--tnzi-base-text, #374151);
}
/* Unsaved-changes dot inside the "save for all users" button. */
.t-theme-drawer__dirty-dot {
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-right: 6px;
  border-radius: 50%;
  background: #ffffff;
  opacity: 0.9;
  animation: t-theme-drawer-pulse 1.6s ease-in-out infinite;
}
@keyframes t-theme-drawer-pulse {
  0%,
  100% {
    opacity: 0.9;
  }
  50% {
    opacity: 0.35;
  }
}
</style>
